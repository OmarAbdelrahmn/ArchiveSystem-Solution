using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using BCrypt.Net;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class AuthService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        /// <summary>
        /// Returns the user if credentials are valid and account is active.
        /// Returns null if username not found, password wrong, or account inactive.
        /// </summary>
        public User? Login(string username, string password)
        {
            using var conn = _db.CreateConnection();

            // 1 — find user by username
            var user = conn.QuerySingleOrDefault<User>(
                "SELECT * FROM Users WHERE Username = @Username",
                new { Username = username.Trim() });

            // 2 — username not found
            if (user == null)
            {
                WriteAuditLog(conn, null, AuditActions.LoginFailure,
                    $"تسجيل دخول فاشل - اسم المستخدم غير موجود: {username}");
                return null;
            }

            // 3 — account disabled
            if (!user.IsActive)
            {
                WriteAuditLog(conn, user.UserId, AuditActions.LoginFailure,
                    $"تسجيل دخول فاشل - الحساب معطل: {username}");
                return null;
            }

            // 4 — verify password
            bool passwordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            if (!passwordValid)
            {
                WriteAuditLog(conn, user.UserId, AuditActions.LoginFailure,
                    $"تسجيل دخول فاشل - كلمة مرور خاطئة: {username}");
                return null;
            }

            // 5 — update last login time
            conn.Execute(
                "UPDATE Users SET LastLoginAt = @Now WHERE UserId = @UserId",
                new { Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"), user.UserId });

            // 6 — load permissions for this user
            var permissions = conn.Query<string>(@"
                SELECT DISTINCT rp.PermissionKey
                FROM   RolePermissions rp
                JOIN   UserRoles       ur ON ur.RoleId = rp.RoleId
                WHERE  ur.UserId    = @UserId
                AND    rp.IsAllowed = 1",
                new { user.UserId });

            UserSession.SetPermissions(permissions);
            UserSession.Login(user);

            // 7 — audit success
            WriteAuditLog(conn, user.UserId, AuditActions.LoginSuccess,
                $"تسجيل دخول ناجح: {user.FullName}");

            return user;
        }

        public void Logout()
        {
            if (UserSession.CurrentUser != null)
            {
                using var conn = _db.CreateConnection();
                WriteAuditLog(conn, UserSession.CurrentUser.UserId,
                    "Logout", $"تسجيل خروج: {UserSession.CurrentUser.FullName}");
            }
            UserSession.Logout();
        }


        public long GetTotalFileCount()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");
        }

        public long GetTotalFolderCount()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM Dossiers WHERE DeletedAt IS NULL");
        }

        /// <summary>
        /// Creates the first archive manager account during first-run setup.
        /// Only works if no users exist yet.
        /// </summary>
        public bool CreateFirstAdmin(string fullName, string username, string password)
        {
            using var conn = _db.CreateConnection();

            int existingCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Users");
            if (existingCount > 0) return false;

            string hash = BCrypt.Net.BCrypt.HashPassword(password);
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            // insert user
            var userId = conn.ExecuteScalar<int>(@"
                INSERT INTO Users (FullName, Username, PasswordHash, IsActive, CreatedAt)
                VALUES (@FullName, @Username, @PasswordHash, 1, @CreatedAt);
                SELECT last_insert_rowid();",
                new
                {
                    FullName = fullName,
                    Username = username,
                    PasswordHash = hash,
                    CreatedAt = now
                });

            // assign Archive Manager role
            var managerRoleId = conn.ExecuteScalar<int>(
                "SELECT RoleId FROM Roles WHERE RoleName = 'مدير الأرشيف'");

            conn.Execute(@"
                INSERT INTO UserRoles (UserId, RoleId, CreatedAt)
                VALUES (@UserId, @RoleId, @CreatedAt)",
                new { UserId = userId, RoleId = managerRoleId, CreatedAt = now });

            WriteAuditLog(conn, userId, AuditActions.UserChanged,
                $"تم إنشاء أول مدير أرشيف: {fullName}");

            return true;
        }

        public bool HasUsers()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Users") > 0;
        }

        public string? GetLastBackupTime()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<string?>(@"
                SELECT CreatedAt FROM Backups
                WHERE  Status = 'Success'
                ORDER  BY CreatedAt DESC
                LIMIT  1");
        }

        private void WriteAuditLog(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            int? userId, string actionType, string description)
        {
            conn.Execute(@"
                INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                VALUES (@UserId, @ActionType, @Description, @CreatedAt)",
                new
                {
                    UserId = userId,
                    ActionType = actionType,
                    Description = description,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
        }
    }
}