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
        WHERE d.DeletedAt IS NULL          -- add this line
        ORDER BY d.DossierNumber DESC",
                (d, l) => { d.CurrentLocation = l; return d; },
                splitOn: "LocationId").AsList();
        }

        /// <summary>
        /// Soft-deletes a dossier and cascades to all its active records.
        /// Returns (null, recordsDeleted) on success, (errorString, 0) on failure.
        /// </summary>
        public (string? Error, int RecordsDeleted) DeleteDossier(int dossierId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return ("سبب الحذف مطلوب.", 0);

            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;

                // Cascade soft-delete all active records in this dossier
                int recordsDeleted = conn.Execute(@"
            UPDATE Records
            SET DeletedAt       = @Now,
                DeletedByUserId = @UserId,
                Status          = 'Deleted',
                Notes           = COALESCE(Notes,'') || ' | حُذف مع الدوسية: ' || @Reason
            WHERE DossierId = @DossierId
            AND   DeletedAt IS NULL",
                    new { Now = now, UserId = userId, Reason = reason, DossierId = dossierId }, tx);

                // Soft-delete the dossier itself
                conn.Execute(@"
            UPDATE Dossiers
            SET DeletedAt       = @Now,
                DeletedByUserId = @UserId,
                Status          = 'Archived',
                UpdatedAt       = @Now
            WHERE DossierId = @DossierId",
                    new { Now = now, UserId = userId, DossierId = dossierId }, tx);

                WriteAudit(conn, AuditActions.DossierDeleted,
                    $"حذف دوسية {dossierId} — السبب: {reason} — السجلات المحذوفة: {recordsDeleted}",
                    "Dossier", dossierId, tx);

                tx.Commit();
                return (null, recordsDeleted);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return ($"خطأ أثناء حذف الدوسية: {ex.Message}", 0);
            }
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
                WHERE d.DossierNumber = @DossierNumber 
                AND d.DeletedAt IS NULL",
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

            int exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Dossiers WHERE DossierNumber = @N",
                new { N = dossierNumber });
            if (exists > 0)
                return ($"رقم الدوسية {dossierNumber} موجود مسبقاً.", 0);

            using var tx = conn.BeginTransaction();
            try
            {
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
            SqliteTransaction? tx = null,
            string? oldJson = null,
            string? newJson = null)
        {
            conn.Execute(@"
                INSERT INTO AuditLog
                    (UserId, ActionType, EntityType, EntityId,
                     Description, OldValueJson, NewValueJson, CreatedAt)
                VALUES
                    (@UserId, @ActionType, @EntityType, @EntityId,
                     @Description, @OldJson, @NewJson, @CreatedAt)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    ActionType = actionType,
                    EntityType = entityType,
                    EntityId = entityId,
                    Description = description,
                    OldJson = oldJson,
                    NewJson = newJson,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                }, tx);
        }

        /// <summary>
        /// Updates dossier metadata: hijri date, expected count, and location.
        /// Auto-creates the location if it does not exist.
        /// Returns error string or null on success.
        /// </summary>
        public string? UpdateDossier(
            int dossierId,
            int hijriMonth,
            int hijriYear,
            int? expectedFileCount,
            int hallway,
            int cabinet,
            int shelf)
        {
            if (hijriMonth < 1 || hijriMonth > 12)
                return "الشهر الهجري يجب أن يكون بين 1 و 12.";
            if (hijriYear < 1400 || hijriYear > 1600)
                return "السنة الهجرية غير صحيحة.";
            if (hallway <= 0 || cabinet <= 0 || shelf <= 0)
                return "يرجى إدخال أرقام الممر والكبينة والرف.";

            using var conn = _db.CreateConnection();

            // ── Capture old values BEFORE the update ─────────────────────────
            var old = conn.Query<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierId = @Id",
                (d, l) => { d.CurrentLocation = l; return d; },
                new { Id = dossierId },
                splitOn: "LocationId").FirstOrDefault();

            using var tx = conn.BeginTransaction();
            try
            {
                int locationId = GetOrCreateLocation(conn, hallway, cabinet, shelf, tx);
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

                conn.Execute(@"
                    UPDATE Dossiers
                    SET HijriMonth        = @Month,
                        HijriYear         = @Year,
                        ExpectedFileCount = @Expected,
                        CurrentLocationId = @LocId,
                        UpdatedAt         = @Now
                    WHERE DossierId = @DossierId",
                    new
                    {
                        Month = hijriMonth,
                        Year = hijriYear,
                        Expected = expectedFileCount.HasValue
                                        ? (object)expectedFileCount.Value
                                        : System.DBNull.Value,
                        LocId = locationId,
                        Now = now,
                        DossierId = dossierId
                    }, tx);

                // ── Serialize old / new snapshots ─────────────────────────────
                string? oldJson = old == null ? null : System.Text.Json.JsonSerializer.Serialize(new
                {
                    old.HijriMonth,
                    old.HijriYear,
                    ExpectedFileCount = old.ExpectedFileCount,
                    Hallway = old.CurrentLocation?.HallwayNumber,
                    Cabinet = old.CurrentLocation?.CabinetNumber,
                    Shelf = old.CurrentLocation?.ShelfNumber
                });

                string newJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    HijriMonth = hijriMonth,
                    HijriYear = hijriYear,
                    ExpectedFileCount = expectedFileCount,
                    Hallway = hallway,
                    Cabinet = cabinet,
                    Shelf = shelf
                });

                WriteAudit(conn, AuditActions.DossierEdited,
                    $"تم تعديل بيانات دوسية {dossierId}",
                    "Dossier", dossierId, tx,
                    oldJson: oldJson, newJson: newJson);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return $"خطأ أثناء تعديل الدوسية: {ex.Message}";
            }
        }

        /// <summary>
        /// Moves a dossier to a new physical location and records a movement entry.
        /// Auto-creates the destination location if it does not exist.
        /// Returns error string or null on success.
        /// </summary>
        public string? MoveDossier(int dossierId, int hallway, int cabinet, int shelf, string? reason)
        {
            if (hallway <= 0 || cabinet <= 0 || shelf <= 0)
                return "يرجى إدخال أرقام الممر والكبينة والرف.";

            using var conn = _db.CreateConnection();

            // ── Capture old location BEFORE the move ─────────────────────────
            var oldLocation = conn.QuerySingleOrDefault<Location>(@"
                SELECT l.*
                FROM Dossiers d
                JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierId = @Id",
                new { Id = dossierId });

            using var tx = conn.BeginTransaction();
            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

                int? fromLocationId = conn.ExecuteScalar<int?>(
                    "SELECT CurrentLocationId FROM Dossiers WHERE DossierId = @Id",
                    new { Id = dossierId }, tx);

                int toLocationId = GetOrCreateLocation(conn, hallway, cabinet, shelf, tx);

                conn.Execute(@"
                    INSERT INTO DossierMovements
                        (DossierId, FromLocationId, ToLocationId, Reason, MovedByUserId, MovedAt)
                    VALUES
                        (@DossId, @FromId, @ToId, @Reason, @UserId, @Now)",
                    new
                    {
                        DossId = dossierId,
                        FromId = fromLocationId.HasValue ? (object)fromLocationId.Value : System.DBNull.Value,
                        ToId = toLocationId,
                        Reason = string.IsNullOrWhiteSpace(reason) ? (object)System.DBNull.Value : reason.Trim(),
                        UserId = UserSession.CurrentUser?.UserId,
                        Now = now
                    }, tx);

                conn.Execute(@"
                    UPDATE Dossiers
                    SET CurrentLocationId = @LocId,
                        UpdatedAt         = @Now
                    WHERE DossierId = @Id",
                    new { LocId = toLocationId, Now = now, Id = dossierId }, tx);

                // ── Serialize old / new location snapshots ────────────────────
                string? oldJson = oldLocation == null ? null : System.Text.Json.JsonSerializer.Serialize(new
                {
                    Hallway = oldLocation.HallwayNumber,
                    Cabinet = oldLocation.CabinetNumber,
                    Shelf = oldLocation.ShelfNumber
                });

                string newJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Hallway = hallway,
                    Cabinet = cabinet,
                    Shelf = shelf,
                    Reason = reason?.Trim()
                });

                WriteAudit(conn, AuditActions.DossierMoved,
                    $"نقل دوسية {dossierId} إلى ممر {hallway} - كبينة {cabinet} - رف {shelf}",
                    "Dossier", dossierId, tx,
                    oldJson: oldJson, newJson: newJson);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return $"خطأ أثناء تسجيل الحركة: {ex.Message}";
            }
        }

        /// <summary>
        /// Changes the dossier status (Open / Complete / Archived).
        /// Sets ClosedAt when status is not Open.
        /// Returns error string or null on success.
        /// </summary>
        public string? SetDossierStatus(int dossierId, string newStatus)
        {
            if (newStatus is not ("Open" or "Complete" or "Archived"))
                return "حالة غير مدعومة.";

            using var conn = _db.CreateConnection();
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            // ── Capture old status ────────────────────────────────────────────
            string? oldStatus = conn.ExecuteScalar<string>(
                "SELECT Status FROM Dossiers WHERE DossierId = @Id",
                new { Id = dossierId });

            conn.Execute(@"
                UPDATE Dossiers
                SET Status    = @Status,
                    ClosedAt  = @ClosedAt,
                    UpdatedAt = @Now
                WHERE DossierId = @Id",
                new
                {
                    Status = newStatus,
                    ClosedAt = newStatus == "Open"
                                   ? (object)System.DBNull.Value
                                   : now,
                    Now = now,
                    Id = dossierId
                });

            string? oldJson = oldStatus == null ? null
                : System.Text.Json.JsonSerializer.Serialize(new { Status = oldStatus });
            string newJson = System.Text.Json.JsonSerializer.Serialize(new { Status = newStatus });

            WriteAudit(conn, AuditActions.DossierEdited,
                $"تغيير حالة الدوسية {dossierId} إلى {newStatus}",
                "Dossier", dossierId,
                oldJson: oldJson, newJson: newJson);

            return null;
        }
    }
}