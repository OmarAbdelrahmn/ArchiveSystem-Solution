using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ArchiveSystem.Core.Services
{
    public class DossierService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<Dossier> GetAllDossiers()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                ORDER BY d.DossierNumber DESC",
                (d, l) => { d.CurrentLocation = l; return d; },
                splitOn: "LocationId").AsList();
        }

        public Dossier? GetDossierById(int dossierId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierId = @DossierId",
                (d, l) => { d.CurrentLocation = l; return d; },
                new { DossierId = dossierId },
                splitOn: "LocationId").FirstOrDefault();
        }

        public Dossier? GetDossierByNumber(int dossierNumber)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierNumber = @DossierNumber",
                (d, l) => { d.CurrentLocation = l; return d; },
                new { DossierNumber = dossierNumber },
                splitOn: "LocationId").FirstOrDefault();
        }

        public int GetNextDossierNumber()
        {
            using var conn = _db.CreateConnection();
            int max = conn.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(DossierNumber), 0) FROM Dossiers");
            return max + 1;
        }

        /// <summary>
        /// Creates a new dossier. Auto-creates the location if it does not exist.
        /// Returns error string or null on success.
        /// </summary>
        public (string? Error, int DossierId) CreateDossier(
            int dossierNumber,
            int hijriMonth,
            int hijriYear,
            int? expectedFileCount,
            int hallway,
            int cabinet,
            int shelf)
        {
            if (dossierNumber <= 0)
                return ("رقم الدوسية غير صحيح.", 0);
            if (hijriMonth < 1 || hijriMonth > 12)
                return ("الشهر الهجري يجب أن يكون بين 1 و 12.", 0);
            if (hijriYear < 1400 || hijriYear > 1600)
                return ("السنة الهجرية غير صحيحة.", 0);
            if (hallway <= 0 || cabinet <= 0 || shelf <= 0)
                return ("يرجى إدخال أرقام الممر والكبينة والرف.", 0);

            using var conn = _db.CreateConnection();

            // check duplicate dossier number
            int exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Dossiers WHERE DossierNumber = @N",
                new { N = dossierNumber });
            if (exists > 0)
                return ($"رقم الدوسية {dossierNumber} موجود مسبقاً.", 0);

            using var tx = conn.BeginTransaction();
            try
            {
                // get or create location
                int locationId = GetOrCreateLocation(
                    conn, hallway, cabinet, shelf, tx);

                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

                int dossierId = conn.ExecuteScalar<int>(@"
                    INSERT INTO Dossiers
                        (DossierNumber, HijriMonth, HijriYear,
                         ExpectedFileCount, CurrentLocationId,
                         Status, CreatedByUserId, CreatedAt)
                    VALUES
                        (@DossierNumber, @HijriMonth, @HijriYear,
                         @ExpectedFileCount, @CurrentLocationId,
                         'Open', @CreatedByUserId, @CreatedAt);
                    SELECT last_insert_rowid();",
                    new
                    {
                        DossierNumber = dossierNumber,
                        HijriMonth = hijriMonth,
                        HijriYear = hijriYear,
                        ExpectedFileCount = expectedFileCount,
                        CurrentLocationId = locationId,
                        CreatedByUserId = UserSession.CurrentUser?.UserId,
                        CreatedAt = now
                    }, tx);

                WriteAudit(conn, AuditActions.DossierCreated,
                    $"تم إنشاء دوسية رقم {dossierNumber}",
                    "Dossier", dossierId, tx);

                tx.Commit();
                return (null, dossierId);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return ($"خطأ أثناء إنشاء الدوسية: {ex.Message}", 0);
            }
        }

        public List<DossierMovement> GetMovementHistory(int dossierId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<DossierMovement, Location, Location, DossierMovement>(@"
                SELECT dm.*, fl.*, tl.*
                FROM DossierMovements dm
                LEFT JOIN Locations fl ON fl.LocationId = dm.FromLocationId
                LEFT JOIN Locations tl ON tl.LocationId = dm.ToLocationId
                WHERE dm.DossierId = @DossierId
                ORDER BY dm.MovedAt DESC",
                (dm, from, to) =>
                {
                    dm.FromLocation = from;
                    dm.ToLocation = to;
                    return dm;
                },
                new { DossierId = dossierId },
                splitOn: "LocationId,LocationId").AsList();
        }

        // ── LOCATION HELPER ──────────────────────────

        private int GetOrCreateLocation(SqliteConnection conn,
            int hallway, int cabinet, int shelf,
            SqliteTransaction tx)
        {
            var existingId = conn.ExecuteScalar<int?>(@"
                SELECT LocationId FROM Locations
                WHERE HallwayNumber = @H
                AND   CabinetNumber = @C
                AND   ShelfNumber   = @S",
                new { H = hallway, C = cabinet, S = shelf }, tx);

            if (existingId.HasValue) return existingId.Value;

            return conn.ExecuteScalar<int>(@"
                INSERT INTO Locations
                    (HallwayNumber, CabinetNumber, ShelfNumber,
                     IsActive, CreatedAt)
                VALUES
                    (@H, @C, @S, 1, @Now);
                SELECT last_insert_rowid();",
                new
                {
                    H = hallway,
                    C = cabinet,
                    S = shelf,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                }, tx);
        }

        private void WriteAudit(SqliteConnection conn,
            string actionType, string description,
            string entityType, int entityId,
            SqliteTransaction? tx = null)
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