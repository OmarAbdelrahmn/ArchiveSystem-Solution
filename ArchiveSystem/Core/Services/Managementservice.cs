using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class ManagementService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        // ── MANAGEMENTS ───────────────────────────────────────────────────────

        /// <summary>Returns all managements flat-list ordered parent-first, then children.</summary>
        public List<Management> GetAllManagements()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Management>(@"
                SELECT
                    m.ManagementId,
                    m.Name,
                    m.ParentManagementId,
                    m.Description,
                    m.IsActive,
                    m.CreatedAt,
                    m.UpdatedAt,
                    p.Name AS ParentName,
                    COUNT(md.ManagementDossierId) AS DossierCount
                FROM Managements m
                LEFT JOIN Managements p ON p.ManagementId = m.ParentManagementId
                LEFT JOIN ManagementDossiers md
                    ON md.ManagementId = m.ManagementId AND md.DeletedAt IS NULL
                GROUP BY m.ManagementId
                ORDER BY COALESCE(m.ParentManagementId, m.ManagementId),
                         m.ParentManagementId IS NULL DESC,
                         m.Name").AsList();
        }

        /// <summary>Returns only top-level managements (no parent).</summary>
        public List<Management> GetRootManagements()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Management>(@"
                SELECT m.*,
                    COUNT(md.ManagementDossierId) AS DossierCount
                FROM Managements m
                LEFT JOIN ManagementDossiers md
                    ON md.ManagementId = m.ManagementId AND md.DeletedAt IS NULL
                WHERE m.ParentManagementId IS NULL AND m.IsActive = 1
                GROUP BY m.ManagementId
                ORDER BY m.Name").AsList();
        }

        public List<Management> GetChildren(int parentId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Management>(@"
                SELECT m.*,
                    COUNT(md.ManagementDossierId) AS DossierCount
                FROM Managements m
                LEFT JOIN ManagementDossiers md
                    ON md.ManagementId = m.ManagementId AND md.DeletedAt IS NULL
                WHERE m.ParentManagementId = @ParentId AND m.IsActive = 1
                GROUP BY m.ManagementId
                ORDER BY m.Name",
                new { ParentId = parentId }).AsList();
        }

        public string? CreateManagement(string name, int? parentId, string? description)
        {
            if (string.IsNullOrWhiteSpace(name)) return "اسم الإدارة مطلوب.";

            using var conn = _db.CreateConnection();
            int dup = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Managements
                WHERE Name = @Name
                AND COALESCE(ParentManagementId,-1) = COALESCE(@ParentId,-1)",
                new { Name = name.Trim(), ParentId = parentId });
            if (dup > 0) return "هذه الإدارة موجودة مسبقاً.";

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            conn.Execute(@"
                INSERT INTO Managements
                    (Name, ParentManagementId, Description, IsActive, CreatedByUserId, CreatedAt)
                VALUES (@Name, @ParentId, @Desc, 1, @UserId, @Now)",
                new
                {
                    Name = name.Trim(),
                    ParentId = (object?)parentId ?? DBNull.Value,
                    Desc = string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description.Trim(),
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = now
                });

            WriteAudit(conn, $"إنشاء إدارة: {name}");
            return null;
        }

        public string? UpdateManagement(int managementId, string name, string? description)
        {
            if (string.IsNullOrWhiteSpace(name)) return "اسم الإدارة مطلوب.";

            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE Managements
                SET Name = @Name, Description = @Desc, UpdatedAt = @Now
                WHERE ManagementId = @Id",
                new
                {
                    Name = name.Trim(),
                    Desc = string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description.Trim(),
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Id = managementId
                });

            WriteAudit(conn, $"تعديل إدارة: {name}");
            return null;
        }

        public string? DeleteManagement(int managementId)
        {
            using var conn = _db.CreateConnection();

            int children = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Managements WHERE ParentManagementId = @Id AND IsActive = 1",
                new { Id = managementId });
            if (children > 0) return "لا يمكن حذف إدارة تحتوي على إدارات فرعية.";

            int dossiers = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ManagementDossiers WHERE ManagementId = @Id AND DeletedAt IS NULL",
                new { Id = managementId });
            if (dossiers > 0) return "لا يمكن حذف إدارة تحتوي على دوسيات.";

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            conn.Execute("UPDATE Managements SET IsActive = 0, UpdatedAt = @Now WHERE ManagementId = @Id",
                new { Now = now, Id = managementId });

            WriteAudit(conn, $"حذف إدارة رقم {managementId}");
            return null;
        }

        // ── DOSSIER TYPES ─────────────────────────────────────────────────────

        public List<ManagementDossierType> GetDossierTypes(int managementId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ManagementDossierType>(@"
                SELECT * FROM ManagementDossierTypes
                WHERE ManagementId = @Id AND IsActive = 1
                ORDER BY TypeName",
                new { Id = managementId }).AsList();
        }

        public string? AddDossierType(int managementId, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return "اسم النوع مطلوب.";

            using var conn = _db.CreateConnection();
            int dup = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM ManagementDossierTypes
                WHERE ManagementId = @Id AND TypeName = @Name AND IsActive = 1",
                new { Id = managementId, Name = typeName.Trim() });
            if (dup > 0) return "هذا النوع موجود مسبقاً.";

            conn.Execute(@"
                INSERT INTO ManagementDossierTypes
                    (ManagementId, TypeName, IsActive, CreatedAt)
                VALUES (@Id, @Name, 1, @Now)",
                new
                {
                    Id = managementId,
                    Name = typeName.Trim(),
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            return null;
        }

        public void DeleteDossierType(int typeId)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("UPDATE ManagementDossierTypes SET IsActive = 0 WHERE TypeId = @Id",
                new { Id = typeId });
        }

        // ── MANAGEMENT DOSSIERS ───────────────────────────────────────────────

        public List<ManagementDossier> GetDossiers(
            int managementId,
            int? hijriYear = null,
            int? hijriMonth = null,
            int? typeId = null,
            string? numberSearch = null,
            bool includeDeleted = false)
        {
            using var conn = _db.CreateConnection();

            var conditions = new List<string> { "md.ManagementId = @ManagementId" };
            var p = new DynamicParameters();
            p.Add("ManagementId", managementId);

            if (!includeDeleted) conditions.Add("md.DeletedAt IS NULL");
            if (hijriYear.HasValue) { conditions.Add("md.HijriYear = @Year"); p.Add("Year", hijriYear); }
            if (hijriMonth.HasValue) { conditions.Add("md.HijriMonth = @Month"); p.Add("Month", hijriMonth); }
            if (typeId.HasValue) { conditions.Add("md.TypeId = @TypeId"); p.Add("TypeId", typeId); }
            if (!string.IsNullOrWhiteSpace(numberSearch))
            {
                conditions.Add("CAST(md.DossierNumber AS TEXT) LIKE @Num");
                p.Add("Num", $"%{numberSearch.Trim()}%");
            }

            string where = "WHERE " + string.Join(" AND ", conditions);

            return conn.Query<ManagementDossier>($@"
                SELECT
                    md.*,
                    t.TypeName,
                    m.Name AS ManagementName
                FROM ManagementDossiers md
                LEFT JOIN ManagementDossierTypes t ON t.TypeId = md.TypeId
                LEFT JOIN Managements m ON m.ManagementId = md.ManagementId
                {where}
                ORDER BY md.HijriYear DESC, md.HijriMonth DESC, md.DossierNumber",
                p).AsList();
        }

        public List<int> GetDistinctYears(int managementId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<int>(@"
                SELECT DISTINCT HijriYear FROM ManagementDossiers
                WHERE ManagementId = @Id AND DeletedAt IS NULL
                ORDER BY HijriYear DESC",
                new { Id = managementId }).AsList();
        }

        public (string? Error, int Id) CreateDossier(
            int managementId, int dossierNumber,
            int hijriMonth, int hijriYear, int? typeId, string? notes)
        {
            if (dossierNumber <= 0) return ("رقم الدوسية غير صحيح.", 0);
            if (hijriMonth < 1 || hijriMonth > 12) return ("الشهر الهجري يجب أن يكون بين 1 و 12.", 0);
            if (hijriYear < 1400 || hijriYear > 1600) return ("السنة الهجرية غير صحيحة.", 0);

            using var conn = _db.CreateConnection();

            // Unique dossier number per management
            int dup = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM ManagementDossiers
                WHERE ManagementId = @MId AND DossierNumber = @Num AND DeletedAt IS NULL",
                new { MId = managementId, Num = dossierNumber });
            if (dup > 0) return ($"رقم الدوسية {dossierNumber} موجود مسبقاً في هذه الإدارة.", 0);

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            int id = conn.ExecuteScalar<int>(@"
                INSERT INTO ManagementDossiers
                    (ManagementId, DossierNumber, HijriMonth, HijriYear,
                     TypeId, Notes, Status, CreatedByUserId, CreatedAt)
                VALUES (@MId, @Num, @Month, @Year,
                        @TypeId, @Notes, 'Open', @UserId, @Now);
                SELECT last_insert_rowid();",
                new
                {
                    MId = managementId,
                    Num = dossierNumber,
                    Month = hijriMonth,
                    Year = hijriYear,
                    TypeId = typeId.HasValue ? (object)typeId.Value : DBNull.Value,
                    Notes = string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes.Trim(),
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = now
                });

            WriteAudit(conn, $"إضافة دوسية إدارية رقم {dossierNumber} للإدارة {managementId}");
            return (null, id);
        }

        public string? UpdateDossier(
            int dossierId, int dossierNumber,
            int hijriMonth, int hijriYear, int? typeId, string? notes)
        {
            if (dossierNumber <= 0) return "رقم الدوسية غير صحيح.";
            if (hijriMonth < 1 || hijriMonth > 12) return "الشهر الهجري يجب أن يكون بين 1 و 12.";
            if (hijriYear < 1400 || hijriYear > 1600) return "السنة الهجرية غير صحيحة.";

            using var conn = _db.CreateConnection();

            // Check dup excluding self
            int managementId = conn.ExecuteScalar<int>(
                "SELECT ManagementId FROM ManagementDossiers WHERE ManagementDossierId = @Id",
                new { Id = dossierId });

            int dup = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM ManagementDossiers
                WHERE ManagementId = @MId AND DossierNumber = @Num
                AND DeletedAt IS NULL AND ManagementDossierId != @Self",
                new { MId = managementId, Num = dossierNumber, Self = dossierId });
            if (dup > 0) return $"رقم الدوسية {dossierNumber} موجود مسبقاً في هذه الإدارة.";

            conn.Execute(@"
                UPDATE ManagementDossiers
                SET DossierNumber = @Num,
                    HijriMonth = @Month,
                    HijriYear = @Year,
                    TypeId = @TypeId,
                    Notes = @Notes,
                    UpdatedAt = @Now
                WHERE ManagementDossierId = @Id",
                new
                {
                    Num = dossierNumber,
                    Month = hijriMonth,
                    Year = hijriYear,
                    TypeId = typeId.HasValue ? (object)typeId.Value : DBNull.Value,
                    Notes = string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes.Trim(),
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Id = dossierId
                });

            WriteAudit(conn, $"تعديل دوسية إدارية رقم {dossierId}");
            return null;
        }

        public string? DeleteDossier(int dossierId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "سبب الحذف مطلوب.";

            using var conn = _db.CreateConnection();
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            conn.Execute(@"
                UPDATE ManagementDossiers
                SET DeletedAt = @Now, Status = 'Deleted',
                    Notes = COALESCE(Notes,'') || ' | حُذف: ' || @Reason
                WHERE ManagementDossierId = @Id",
                new { Now = now, Reason = reason, Id = dossierId });

            WriteAudit(conn, $"حذف دوسية إدارية رقم {dossierId} — السبب: {reason}");
            return null;
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection conn, string desc)
        {
            conn.Execute(@"
                INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                VALUES (@UserId, 'ManagementChanged', @Desc, @Now)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Desc = desc,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
        }
    }
}