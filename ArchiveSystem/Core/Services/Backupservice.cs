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

        /// <summary>Creates a backup copy of the SQLite database file.</summary>
        public (string? Error, string? BackupPath) CreateBackup(
            string? backupFolder = null, string backupType = "Manual")
        {
            try
            {
                backupFolder ??= GetDefaultBackupFolder();
                System.IO.Directory.CreateDirectory(backupFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string destPath = System.IO.Path.Combine(
                    backupFolder, $"archive_backup_{timestamp}.db");

                // WAL checkpoint before copy
                using (var conn = _db.CreateConnection())
                    conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");

                System.IO.File.Copy(_dbPath, destPath, overwrite: false);
                long fileSize = new System.IO.FileInfo(destPath).Length;

                using var conn2 = _db.CreateConnection();
                conn2.Execute(@"
                    INSERT INTO Backups
                        (BackupPath, BackupType, Status, FileSizeBytes, CreatedByUserId, CreatedAt)
                    VALUES (@Path, @Type, 'Success', @Size, @UserId, @Now)",
                    new
                    {
                        Path = destPath,
                        Type = backupType,
                        Size = fileSize,
                        UserId = UserSession.CurrentUser?.UserId,
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                conn2.Execute(@"
                    INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                    VALUES (@UserId, 'BackupCreated', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"نسخة احتياطية: {System.IO.Path.GetFileName(destPath)}",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return (null, destPath);
            }
            catch (Exception ex)
            {
                return ($"خطأ أثناء إنشاء النسخة الاحتياطية: {ex.Message}", null);
            }
        }

        public List<BackupRecord> GetBackupHistory(int limit = 20)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<BackupRecord>(@"
                SELECT b.BackupId, b.BackupPath, b.BackupType, b.Status,
                       b.FileSizeBytes, b.CreatedAt, u.FullName AS CreatedByName
                FROM Backups b
                LEFT JOIN Users u ON u.UserId = b.CreatedByUserId
                ORDER BY b.CreatedAt DESC
                LIMIT @Limit",
                new { Limit = limit }).AsList();
        }

        public string? RestoreBackup(string backupPath)
        {
            if (!System.IO.File.Exists(backupPath))
                return "ملف النسخة الاحتياطية غير موجود.";

            try
            {
                // First backup current state
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

        public string GetDefaultBackupFolder()
        {
            try
            {
                using var conn = _db.CreateConnection();
                var setting = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupPath'");
                if (!string.IsNullOrWhiteSpace(setting)) return setting;
            }
            catch { /* ignore */ }

            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ArchiveBackups");
        }

        /// <summary>Deletes backup files older than retentionDays.</summary>
        public void CleanOldBackups(string backupFolder, int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in System.IO.Directory.GetFiles(
                    backupFolder, "archive_backup_*.db"))
                {
                    if (System.IO.File.GetCreationTime(file) < cutoff)
                        System.IO.File.Delete(file);
                }
            }
            catch { /* ignore */ }
        }
    }
}