using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class RoleService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<Role> GetAllRoles()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Role>(
                "SELECT * FROM Roles ORDER BY IsSystemRole DESC, RoleName")
                .AsList();
        }

        public List<RolePermission> GetRolePermissions(int roleId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<RolePermission>(
                "SELECT * FROM RolePermissions WHERE RoleId = @RoleId",
                new { RoleId = roleId }).AsList();
        }

        /// <summary>
        /// Returns ALL permission keys with IsAllowed filled for a given role.
        /// Keys that have no row yet are returned as IsAllowed = false.
        /// </summary>
        public List<PermissionEntry> GetAllPermissionsForRole(int roleId)
        {
            using var conn = _db.CreateConnection();

            var existing = conn.Query<RolePermission>(
                "SELECT * FROM RolePermissions WHERE RoleId = @RoleId",
                new { RoleId = roleId })
                .ToDictionary(p => p.PermissionKey);

            var allKeys = GetAllPermissionKeys();

            return allKeys.Select(key => new PermissionEntry
            {
                PermissionKey = key.Key,
                ArabicLabel = key.Value,
                IsAllowed = existing.TryGetValue(key.Key, out var p) && p.IsAllowed
            }).ToList();
        }

        public string? CreateRole(string roleName, string? description)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return "اسم الدور مطلوب.";

            using var conn = _db.CreateConnection();

            int exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Roles WHERE RoleName = @RoleName",
                new { RoleName = roleName.Trim() });
            if (exists > 0) return "هذا الدور موجود مسبقاً.";

            conn.Execute(@"
                INSERT INTO Roles (RoleName, Description, IsSystemRole, CreatedAt)
                VALUES (@RoleName, @Description, 0, @CreatedAt)",
                new
                {
                    RoleName = roleName.Trim(),
                    Description = description,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            WriteAudit(conn, $"تم إنشاء دور جديد: {roleName}");
            return null;
        }

        public string? UpdateRole(int roleId, string roleName, string? description)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return "اسم الدور مطلوب.";

            using var conn = _db.CreateConnection();

            // check not duplicate name (exclude self)
            int exists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Roles
                WHERE RoleName = @RoleName AND RoleId != @RoleId",
                new { RoleName = roleName.Trim(), RoleId = roleId });
            if (exists > 0) return "هذا الاسم مستخدم من قبل.";

            conn.Execute(@"
                UPDATE Roles SET RoleName = @RoleName,
                    Description = @Description,
                    UpdatedAt = @UpdatedAt
                WHERE RoleId = @RoleId",
                new
                {
                    RoleName = roleName.Trim(),
                    Description = description,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    RoleId = roleId
                });

            WriteAudit(conn, $"تم تعديل الدور: {roleName}");
            return null;
        }

        public string? DeleteRole(int roleId)
        {
            using var conn = _db.CreateConnection();

            var role = conn.QuerySingleOrDefault<Role>(
                "SELECT * FROM Roles WHERE RoleId = @RoleId",
                new { RoleId = roleId });

            if (role == null) return "الدور غير موجود.";
            if (role.IsSystemRole) return "لا يمكن حذف الأدوار الأساسية للنظام.";

            int userCount = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM UserRoles WHERE RoleId = @RoleId",
                new { RoleId = roleId });
            if (userCount > 0)
                return "لا يمكن حذف دور مرتبط بمستخدمين. قم بإلغاء تعيينه أولاً.";

            conn.Execute("DELETE FROM RolePermissions WHERE RoleId = @RoleId",
                new { RoleId = roleId });
            conn.Execute("DELETE FROM Roles WHERE RoleId = @RoleId",
                new { RoleId = roleId });

            WriteAudit(conn, $"تم حذف الدور: {role.RoleName}");
            return null;
        }

        /// <summary>
        /// Sets a single permission on/off for a role.
        /// Creates the row if it doesn't exist, updates it if it does.
        /// </summary>
        public void SetPermission(int roleId, string permissionKey, bool isAllowed)
        {
            using var conn = _db.CreateConnection();

            conn.Execute(@"
                INSERT INTO RolePermissions (RoleId, PermissionKey, IsAllowed, UpdatedAt)
                VALUES (@RoleId, @PermissionKey, @IsAllowed, @UpdatedAt)
                ON CONFLICT(RoleId, PermissionKey)
                DO UPDATE SET IsAllowed = @IsAllowed, UpdatedAt = @UpdatedAt",
                new
                {
                    RoleId = roleId,
                    PermissionKey = permissionKey,
                    IsAllowed = isAllowed ? 1 : 0,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            WriteAudit(conn,
                $"تم تعديل صلاحية '{permissionKey}' للدور: {(isAllowed ? "مفعّل" : "معطّل")}");
        }

        // ── All permission keys with Arabic labels ──
        public static Dictionary<string, string> GetAllPermissionKeys() => new()
        {
            [Permissions.DeleteDossier] = "حذف الدوسيات",   // ← add
            [Permissions.SearchRecords] = "البحث في السجلات",
            [Permissions.ViewDossier] = "عرض الدوسيات",
            [Permissions.AddRecord] = "إضافة سجل جديد",
            [Permissions.EditRecord] = "تعديل السجلات",
            [Permissions.DeleteRecord] = "حذف السجلات",
            [Permissions.CreateDossier] = "إنشاء دوسية جديدة",
            [Permissions.EditDossier] = "تعديل الدوسيات",
            [Permissions.MoveDossier] = "تحريك الدوسيات",
            [Permissions.PrintReports] = "طباعة التقارير",
            [Permissions.PrintDossierFace] = "طباعة واجهة الدوسية",
            [Permissions.ImportExcel] = "استيراد Excel",
            [Permissions.ApproveExcelImport] = "اعتماد استيراد Excel",
            [Permissions.ViewStatistics] = "عرض الإحصاءات",
            [Permissions.ManageArchiveStructure] = "إدارة هيكل الأرشيف",
            [Permissions.ManageCustomFields] = "إدارة الحقول المخصصة",
            [Permissions.ManageUsers] = "إدارة المستخدمين",
            [Permissions.ManageSettings] = "إدارة الإعدادات",
            [Permissions.CreateBackup] = "إنشاء نسخة احتياطية",
            [Permissions.RestoreBackup] = "استعادة نسخة احتياطية",
            [Permissions.ViewAuditLog] = "عرض سجل المراجعة",
            [Permissions.ManageFieldSuggestions] = "إدارة قوائم الاقتراحات (الجنسيات وغيرها)",
            [Permissions.ManageManagements] = "إدارة الإدارات والشعب",
        };

        private void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection conn,
            string description)
        {
            conn.Execute(@"
                INSERT INTO AuditLog
                    (UserId, ActionType, Description, CreatedAt)
                VALUES
                    (@UserId, @ActionType, @Description, @CreatedAt)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    ActionType = AuditActions.RoleChanged,
                    Description = description,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
        }
    }

    // Simple DTO used only in the UI layer
    public class PermissionEntry
    {
        public string PermissionKey { get; set; } = string.Empty;
        public string ArabicLabel { get; set; } = string.Empty;
        public bool IsAllowed { get; set; }
    }
}