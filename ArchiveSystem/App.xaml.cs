using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Data;
using Dapper;
using System.IO;
using System.Windows;

namespace ArchiveSystem
{
    public partial class App : Application
    {
        public static DatabaseContext Database { get; private set; } = null!;
        public static string DbPath { get; private set; } = string.Empty;
        public static BackupService Backup { get; private set; } = null!;
        /// <summary>Assembly version string, e.g. "1.0.0".</summary>
        public static string AppVersion { get; private set; } = "1.0.0";

        /// <summary>
        /// The FontScale key read from AppSettings on startup ("Normal" or "Large").
        /// Windows call <see cref="FontScaleManager.ToMultiplier"/> to convert this
        /// to a numeric scale factor.  SettingsPage updates this after saving.
        /// </summary>
        public static string FontScaleSetting { get; internal set; } = FontScaleManager.KeyNormal;

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

            WriteVersionToDatabase();

            // ── Read font scale so every subsequent window can apply it ──────
            try
            {
                using var conn = Database.CreateConnection();
                FontScaleSetting = conn.QueryFirstOrDefault<string>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = @K",
                    new { K = SettingKeys.FontScale })
                    ?? FontScaleManager.KeyNormal;
            }
            catch { /* not yet seeded — keep default Normal */ }

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

        /// <summary>
        /// Reads the assembly version (set in .csproj &lt;Version&gt;) and upserts it
        /// into AppSettings so the running version is always recorded in the DB.
        /// </summary>
        private static void WriteVersionToDatabase()
        {
            try
            {
                var raw = System.Reflection.Assembly
                              .GetExecutingAssembly()
                              .GetName().Version;

                // Format as "Major.Minor.Patch" — drop the always-zero revision
                string version = raw != null
                    ? $"{raw.Major}.{raw.Minor}.{raw.Build}"
                    : "1.0.0";

                AppVersion = version;   // ← ADD HERE, before the DB write


                using var conn = Database.CreateConnection();
                conn.Execute(@"
            INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt)
            VALUES (@Key, @Value, @Now)
            ON CONFLICT(SettingKey)
            DO UPDATE SET SettingValue = @Value, UpdatedAt = @Now",
                    new
                    {
                        Key = SettingKeys.AppVersion,
                        Value = version,
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });
            }
            catch { /* non-critical — never block startup */ }
        }
    }
}