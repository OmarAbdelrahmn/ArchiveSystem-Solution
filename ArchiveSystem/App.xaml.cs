using System.IO;
using System.Windows;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Data;

namespace ArchiveSystem
{
    public partial class App : Application
    {
        public static DatabaseContext Database { get; private set; } = null!;
        public static string DbPath { get; private set; } = string.Empty;
        public static BackupService Backup { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Initialize database ──────────────────────────────────────────
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ArchiveSystem");

            Directory.CreateDirectory(appDataFolder);

            DbPath = Path.Combine(appDataFolder, "archive.db");
            Database = new DatabaseContext(DbPath);
            Database.InitializeDatabase();

            // ── Initialize backup service ────────────────────────────────────
            Backup = new BackupService(Database, DbPath);

            // ── Schedule auto-daily backup (runs on background thread) ───────
            // NOTE: UserSession is not yet logged in here, so BackupCreated audit
            //       entries will have UserId = null (logged as system).
            Backup.ScheduleDailyBackupIfNeeded();

            // ── Open login window ────────────────────────────────────────────
            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}