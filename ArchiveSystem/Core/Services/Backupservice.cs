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