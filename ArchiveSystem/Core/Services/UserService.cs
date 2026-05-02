using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class UserService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<User> GetAllUsers()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<User>("SELECT * FROM Users ORDER BY FullName").AsList();
        }

        public User? GetUserById(int userId)
        {
            using var conn = _db.CreateConnection();
            return conn.QuerySingleOrDefault<User>(
                "SELECT * FROM Users WHERE UserId = @UserId",
                new { UserId = userId });
        }

        public List<Role> GetUserRoles(int userId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Role>(@"
                SELECT r.* FROM Roles r
                JOIN UserRoles ur ON ur.RoleId = r.RoleId
                WHERE ur.UserId = @UserId
                ORDER BY r.RoleName", new { UserId = userId }).AsList();
        }

        /// <summary>
        /// Creates a new user and assigns a role.
        /// Returns error message string or null if success.
        /// </summary>
        public string? CreateUser(string fullName, string username,
                                   string password, int roleId,
                                   string? employeeNumber = null)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "الاسم الكامل مطلوب.";
            if (string.IsNullOrWhiteSpace(username))
                return "اسم المستخدم مطلوب.";
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                return "كلمة المرور يجب أن تكون 6 أحرف على الأقل.";

            using var conn = _db.CreateConnection();

            // check username unique
            int exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Users WHERE Username = @Username",
                new { Username = username.Trim() });
            if (exists > 0)
                return "اسم المستخدم مستخدم من قبل.";

            string hash = BCrypt.Net.BCrypt.HashPassword(password);
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            using var tx = conn.BeginTransaction();
            try
            {
                var userId = conn.ExecuteScalar<int>(@"
                    INSERT INTO Users
                        (FullName, Username, EmployeeNumber, PasswordHash, IsActive, CreatedAt)
                    VALUES
                        (@FullName, @Username, @EmployeeNumber, @PasswordHash, 1, @CreatedAt);
                    SELECT last_insert_rowid();",
                    new
                    {
                        FullName = fullName.Trim(),
                        Username = username.Trim(),
                        EmployeeNumber = employeeNumber,
                        PasswordHash = hash,
                        CreatedAt = now
                    }, tx);

                conn.Execute(@"
                    INSERT INTO UserRoles (UserId, RoleId, CreatedAt)
                    VALUES (@UserId, @RoleId, @CreatedAt)",
                    new { UserId = userId, RoleId = roleId, CreatedAt = now }, tx);

                WriteAudit(conn, AuditActions.UserChanged,
                    $"تم إنشاء مستخدم جديد: {fullName} ({username})",
                    "User", userId, tx);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return $"خطأ أثناء إنشاء المستخدم: {ex.Message}";
            }
        }

        public string? UpdateUser(int userId, string fullName,
                                   string? employeeNumber, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "الاسم الكامل مطلوب.";

            // cannot deactivate yourself
            if (!isActive && UserSession.CurrentUser?.UserId == userId)
                return "لا يمكنك تعطيل حسابك الخاص.";

            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE Users
                SET FullName = @FullName, EmployeeNumber = @EmployeeNumber,
                    IsActive = @IsActive, UpdatedAt = @UpdatedAt
                WHERE UserId = @UserId",
                new
                {
                    FullName = fullName.Trim(),
                    EmployeeNumber = employeeNumber,
                    IsActive = isActive ? 1 : 0,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    UserId = userId
                });

            WriteAudit(conn, AuditActions.UserChanged,
                $"تم تعديل بيانات المستخدم: {fullName}", "User", userId);
            return null;
        }

        public string? ChangePassword(int userId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return "كلمة المرور يجب أن تكون 6 أحرف على الأقل.";

            string hash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            using var conn = _db.CreateConnection();

            conn.Execute(@"
                UPDATE Users SET PasswordHash = @Hash,
                    UpdatedAt = @UpdatedAt WHERE UserId = @UserId",
                new
                {
                    Hash = hash,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    UserId = userId
                });

            WriteAudit(conn, AuditActions.UserChanged,
                "تم تغيير كلمة المرور", "User", userId);
            return null;
        }

        public string? AssignRole(int userId, int roleId)
        {
            using var conn = _db.CreateConnection();

            int exists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM UserRoles
                WHERE UserId = @UserId AND RoleId = @RoleId",
                new { UserId = userId, RoleId = roleId });

            if (exists > 0) return null; // already assigned

            conn.Execute(@"
                INSERT INTO UserRoles (UserId, RoleId, CreatedAt)
                VALUES (@UserId, @RoleId, @CreatedAt)",
                new
                {
                    UserId = userId,
                    RoleId = roleId,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            WriteAudit(conn, AuditActions.UserChanged,
                $"تم إضافة دور للمستخدم", "User", userId);
            return null;
        }

        public string? RemoveRole(int userId, int roleId)
        {
            // cannot remove archive manager role from yourself
            using var conn = _db.CreateConnection();

            if (UserSession.CurrentUser?.UserId == userId)
            {
                var role = conn.QuerySingleOrDefault<Role>(
                    "SELECT * FROM Roles WHERE RoleId = @RoleId",
                    new { RoleId = roleId });
                if (role?.RoleName == "مدير الأرشيف")
                    return "لا يمكنك إزالة دور مدير الأرشيف من نفسك.";
            }

            conn.Execute(@"
                DELETE FROM UserRoles
                WHERE UserId = @UserId AND RoleId = @RoleId",
                new { UserId = userId, RoleId = roleId });

            WriteAudit(conn, AuditActions.UserChanged,
                "تم إزالة دور من المستخدم", "User", userId);
            return null;
        }

        // helper
        private void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection conn,
            string actionType, string description,
            string? entityType = null, int? entityId = null,
            Microsoft.Data.Sqlite.SqliteTransaction? tx = null)
        {
            conn.Execute(@"
                INSERT INTO AuditLog
                    (UserId, ActionType, EntityType, EntityId, Description, CreatedAt)
                VALUES
                    (@UserId, @ActionType, @EntityType, @EntityId, @Description, @CreatedAt)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    ActionType = actionType,
                    EntityType = entityType,
                    EntityId = entityId,
                    Description = description,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                }, tx);
        }
    }
}