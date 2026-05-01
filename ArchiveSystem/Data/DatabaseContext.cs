using Dapper;
using Microsoft.Data.Sqlite;

namespace ArchiveSystem.Data
{
    public class DatabaseContext(string dbPath)
    {
        private readonly string _dbPath = dbPath;

        public SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            conn.Execute("PRAGMA foreign_keys = ON;");
            conn.Execute("PRAGMA journal_mode = WAL;");
            conn.Execute("PRAGMA busy_timeout = 5000;");
            return conn;
        }

        public void InitializeDatabase()
        {
            using var conn = CreateConnection();
            RunMigrations(conn);
        }

        private void RunMigrations(SqliteConnection conn)
        {
            // migrations will be added here in Milestone 2
            // for now this just ensures the method exists
        }
    }
}