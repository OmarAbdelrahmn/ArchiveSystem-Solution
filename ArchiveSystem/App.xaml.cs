using System.IO;
using System.Windows;
using ArchiveSystem.Data;

namespace ArchiveSystem
{
    public partial class App : Application
    {
        public static DatabaseContext Database { get; private set; } = null!;
        public static string DbPath { get; private set; } = string.Empty;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize database
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ArchiveSystem");

            Directory.CreateDirectory(appDataFolder);

            DbPath = Path.Combine(appDataFolder, "archive.db");
            Database = new DatabaseContext(DbPath);
            Database.InitializeDatabase();

            // Open login window
            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}