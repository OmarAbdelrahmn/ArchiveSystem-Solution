using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class BackupRecord
    {
        public int BackupId { get; set; }
        public string BackupPath { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long? FileSizeBytes { get; set; }
        public string? CreatedByName { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(BackupPath);
        public string FileSizeDisplay =>
            FileSizeBytes.HasValue ? $"{FileSizeBytes.Value / 1024.0:F1} KB" : "—";
    }

    public class BackupService(DatabaseContext db, string dbPath)
    {
        private readonly DatabaseContext _db = db;
        private readonly string _dbPath = dbPath;

        // ── CREATE BACKUP ─────────────────────────────────────────────────────

        /// <summary>Creates a backup copy of the SQLite database file.</summary>
        public (string? Error, string? BackupPath) CreateBackup(
          string? backupFolder = null, string backupType = "Manual")
        {
            try
            {
                string defaultFolder = GetDefaultAppDataBackupFolder();
                System.IO.Directory.CreateDirectory(defaultFolder);

                // Also get the user-configured folder (if any and different from passed folder)
                string? userConfiguredFolder = GetUserConfiguredBackupFolder();

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"dms_backup_{timestamp}.db";

                // Primary destination: always the folder passed in (or default app folder)
                string primaryFolder = backupFolder ?? defaultFolder;
                System.IO.Directory.CreateDirectory(primaryFolder);
                string destPath = System.IO.Path.Combine(primaryFolder, fileName);

                // WAL checkpoint before copy
                using (var conn = _db.CreateConnection())
                    conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");

                System.IO.File.Copy(_dbPath, destPath, overwrite: false);
                long fileSize = new System.IO.FileInfo(destPath).Length;

                // Secondary copy: user-configured folder (if set and different from primary)
                string? secondaryError = null;
                if (!string.IsNullOrWhiteSpace(userConfiguredFolder)
                    && !string.Equals(
                        System.IO.Path.GetFullPath(userConfiguredFolder),
                        System.IO.Path.GetFullPath(primaryFolder),
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(userConfiguredFolder);
                        string secondaryPath = System.IO.Path.Combine(userConfiguredFolder, fileName);
                        System.IO.File.Copy(_dbPath, secondaryPath, overwrite: false);
                    }
                    catch (Exception ex)
                    {
                        secondaryError = ex.Message;
                    }
                }

                using var conn2 = _db.CreateConnection();
                conn2.Execute(@"
            INSERT INTO Backups
                (BackupPath, BackupType, Status, FileSizeBytes, CreatedByUserId, CreatedAt, Notes)
            VALUES (@Path, @Type, 'Success', @Size, @UserId, @Now, @Notes)",
                    new
                    {
                        Path = destPath,
                        Type = backupType,
                        Size = fileSize,
                        UserId = UserSession.CurrentUser?.UserId,
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                        Notes = secondaryError != null
                            ? $"تحذير: فشل النسخ إلى المجلد الثانوي: {secondaryError}"
                            : (object)DBNull.Value
                    });

                conn2.Execute(@"
            INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
            VALUES (@UserId, 'BackupCreated', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = secondaryError != null
                            ? $"نسخة احتياطية ({backupType}): {System.IO.Path.GetFileName(destPath)} — تحذير: فشل النسخ الثانوي"
                            : $"نسخة احتياطية ({backupType}): {System.IO.Path.GetFileName(destPath)}",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return (null, destPath);
            }
            catch (Exception ex)
            {
                try
                {
                    using var conn = _db.CreateConnection();
                    conn.Execute(@"
                INSERT INTO Backups
                    (BackupPath, BackupType, Status, CreatedByUserId, CreatedAt, Notes)
                VALUES ('', @Type, 'Failed', @UserId, @Now, @Notes)",
                        new
                        {
                            Type = backupType,
                            UserId = UserSession.CurrentUser?.UserId,
                            Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Notes = ex.Message
                        });
                }
                catch { }

                return ($"خطأ أثناء إنشاء النسخة الاحتياطية: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Always-available fallback inside AppData — never null, always writable.
        /// </summary>
        private static string GetDefaultAppDataBackupFolder() =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DMS_ArchiveSystem", "Backups");

        /// <summary>
        /// Returns the user-configured folder from AppSettings, or null if not set.
        /// This is distinct from GetDefaultBackupFolder() which returns the configured
        /// folder OR falls back to Documents — here we only return it if explicitly set.
        /// </summary>
        private string? GetUserConfiguredBackupFolder()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var setting = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupPath'");
                return string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
            }
            catch { return null; }
        }

        // ── AUTO DAILY BACKUP ─────────────────────────────────────────────────

        public void ScheduleDailyBackupIfNeeded()
        {
            Task.Run(() =>
            {
                try
                {
                    string todayPrefix = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    using var conn = _db.CreateConnection();

                    string backupTimeStr = conn.ExecuteScalar<string?>(
                        "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupTime'")
                        ?? "02:00";

                    TimeSpan.TryParse(backupTimeStr, out var scheduledTime);
                    var now = DateTime.Now.TimeOfDay;

                    if (now < scheduledTime) return;

                    int todayCount = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Backups
                WHERE Status = 'Success'
                AND   BackupType = 'Automatic'
                AND   CreatedAt LIKE @Prefix",
                        new { Prefix = todayPrefix + "%" });

                    if (todayCount == 0)
                    {
                        var backupFolder = GetDefaultBackupFolder();
                        CreateBackup(backupFolder, "Automatic");
                        var retentionDays = GetRetentionDays();
                        CleanOldBackups(backupFolder, retentionDays);
                    }
                }
                catch { }
            });
        }

        // ── HISTORY ───────────────────────────────────────────────────────────

        public List<BackupRecord> GetBackupHistory(int limit = 20)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<BackupRecord>(@"
                SELECT b.BackupId, b.BackupPath, b.BackupType, b.Status,
                       b.FileSizeBytes, b.CreatedAt,
                       u.FullName AS CreatedByName
                FROM Backups b
                LEFT JOIN Users u ON u.UserId = b.CreatedByUserId
                ORDER BY b.CreatedAt DESC
                LIMIT @Limit",
                new { Limit = limit }).AsList();
        }


        /// <summary>
        /// Creates a lightweight backup containing only Users, UserRoles,
        /// Roles, and RolePermissions tables.  The output is a valid SQLite
        /// database that can be used to restore the permission structure on
        /// a fresh installation.
        /// </summary>
        public (string? Error, string? BackupPath) CreateUsersBackup(string targetFolder)
        {
            try
            {
                System.IO.Directory.CreateDirectory(targetFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"dms_users_backup_{timestamp}.db";
                string destPath = System.IO.Path.Combine(targetFolder, fileName);

                // ── 1. WAL checkpoint so all writes are flushed ────────────────
                using (var srcConn = _db.CreateConnection())
                    srcConn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");

                // ── 2. Open a fresh destination database ───────────────────────
                var connStr = $"Data Source={destPath}";
                using var dst = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                dst.Open();
                dst.Execute("PRAGMA journal_mode = WAL;");
                dst.Execute("PRAGMA foreign_keys = OFF;");

                // ── 3. Recreate schema for the four tables ─────────────────────
                dst.Execute(@"
            CREATE TABLE Users (
                UserId         INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName       TEXT    NOT NULL,
                Username       TEXT    NOT NULL UNIQUE,
                EmployeeNumber TEXT,
                PasswordHash   TEXT    NOT NULL,
                PasswordSalt   TEXT,
                IsActive       INTEGER NOT NULL DEFAULT 1,
                CreatedAt      TEXT    NOT NULL,
                UpdatedAt      TEXT,
                LastLoginAt    TEXT
            );
            CREATE TABLE Roles (
                RoleId       INTEGER PRIMARY KEY AUTOINCREMENT,
                RoleName     TEXT    NOT NULL UNIQUE,
                Description  TEXT,
                IsSystemRole INTEGER NOT NULL DEFAULT 0,
                CreatedAt    TEXT    NOT NULL,
                UpdatedAt    TEXT
            );
            CREATE TABLE UserRoles (
                UserId    INTEGER NOT NULL,
                RoleId    INTEGER NOT NULL,
                CreatedAt TEXT    NOT NULL,
                PRIMARY KEY (UserId, RoleId)
            );
            CREATE TABLE RolePermissions (
                RolePermissionId INTEGER PRIMARY KEY AUTOINCREMENT,
                RoleId           INTEGER NOT NULL,
                PermissionKey    TEXT    NOT NULL,
                IsAllowed        INTEGER NOT NULL DEFAULT 0,
                UpdatedAt        TEXT,
                UpdatedByUserId  INTEGER,
                UNIQUE (RoleId, PermissionKey)
            );
        ");

                // ── 4. Copy data from the live database ────────────────────────
                using var src = _db.CreateConnection();

                var users = src.Query("SELECT * FROM Users").AsList();
                foreach (var u in users)
                    dst.Execute(@"
                INSERT INTO Users
                    (UserId,FullName,Username,EmployeeNumber,PasswordHash,
                     PasswordSalt,IsActive,CreatedAt,UpdatedAt,LastLoginAt)
                VALUES
                    (@UserId,@FullName,@Username,@EmployeeNumber,@PasswordHash,
                     @PasswordSalt,@IsActive,@CreatedAt,@UpdatedAt,@LastLoginAt)",
                        new
                        {
                            u.UserId,
                            u.FullName,
                            u.Username,
                            u.EmployeeNumber,
                            u.PasswordHash,
                            u.PasswordSalt,
                            u.IsActive,
                            u.CreatedAt,
                            u.UpdatedAt,
                            u.LastLoginAt
                        });

                var roles = src.Query("SELECT * FROM Roles").AsList();
                foreach (var r in roles)
                    dst.Execute(@"
                INSERT INTO Roles
                    (RoleId,RoleName,Description,IsSystemRole,CreatedAt,UpdatedAt)
                VALUES
                    (@RoleId,@RoleName,@Description,@IsSystemRole,@CreatedAt,@UpdatedAt)",
                        new
                        {
                            r.RoleId,
                            r.RoleName,
                            r.Description,
                            r.IsSystemRole,
                            r.CreatedAt,
                            r.UpdatedAt
                        });

                var userRoles = src.Query("SELECT * FROM UserRoles").AsList();
                foreach (var ur in userRoles)
                    dst.Execute(@"
                INSERT INTO UserRoles (UserId,RoleId,CreatedAt)
                VALUES (@UserId,@RoleId,@CreatedAt)",
                        new { ur.UserId, ur.RoleId, ur.CreatedAt });

                var perms = src.Query("SELECT * FROM RolePermissions").AsList();
                foreach (var rp in perms)
                    dst.Execute(@"
                INSERT INTO RolePermissions
                    (RolePermissionId,RoleId,PermissionKey,IsAllowed,
                     UpdatedAt,UpdatedByUserId)
                VALUES
                    (@RolePermissionId,@RoleId,@PermissionKey,@IsAllowed,
                     @UpdatedAt,@UpdatedByUserId)",
                        new
                        {
                            rp.RolePermissionId,
                            rp.RoleId,
                            rp.PermissionKey,
                            rp.IsAllowed,
                            rp.UpdatedAt,
                            rp.UpdatedByUserId
                        });

                dst.Execute("PRAGMA wal_checkpoint(TRUNCATE);");

                // ── 5. Audit log entry ─────────────────────────────────────────
                src.Execute(@"
            INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
            VALUES (@UserId, 'BackupCreated', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"نسخة احتياطية للمستخدمين والصلاحيات: {fileName}",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return (null, destPath);
            }
            catch (Exception ex)
            {
                return ($"خطأ أثناء إنشاء النسخة الاحتياطية للمستخدمين: {ex.Message}", null);
            }
        }

        // ── RESTORE ───────────────────────────────────────────────────────────

        public string? RestoreBackup(string backupPath)
        {
            if (!System.IO.File.Exists(backupPath))
                return "ملف النسخة الاحتياطية غير موجود.";

            try
            {
                CreateBackup(System.IO.Path.GetDirectoryName(_dbPath), "BeforeRestore");

                System.IO.File.Copy(backupPath, _dbPath, overwrite: true);

                using var conn = _db.CreateConnection();
                conn.Execute(@"
                    INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                    VALUES (@UserId, 'RestoreCompleted', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"استعادة من: {System.IO.Path.GetFileName(backupPath)}",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء الاستعادة: {ex.Message}";
            }
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        public string GetDefaultBackupFolder()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var setting = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupPath'");
                if (!string.IsNullOrWhiteSpace(setting)) return setting;
            }
            catch { }

            return GetDefaultAppDataBackupFolder();
        }

        private int GetRetentionDays()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var v = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupRetentionDays'");
                if (v != null && int.TryParse(v, out int days)) return days;
            }
            catch { /* ignore */ }
            return 365;
        }

        /// <summary>Deletes automatic backup files older than retentionDays.</summary>
        public void CleanOldBackups(string backupFolder, int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in System.IO.Directory.GetFiles(
                    backupFolder, "dms_backup_*.db"))
                {
                    if (System.IO.File.GetCreationTime(file) < cutoff)
                        System.IO.File.Delete(file);
                }
            }
            catch { /* ignore */ }
        }
    }
}