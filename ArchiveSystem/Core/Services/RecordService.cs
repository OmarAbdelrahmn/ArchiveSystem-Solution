using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class RecordService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<Record> GetRecordsByDossier(int dossierId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Record>(@"
                SELECT * FROM Records
                WHERE DossierId = @DossierId
                AND   DeletedAt IS NULL
                ORDER BY SequenceNumber",
                new { DossierId = dossierId }).AsList();
        }

        public int GetNextSequenceNumber(int dossierId)
        {
            using var conn = _db.CreateConnection();
            int max = conn.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(SequenceNumber), 0)
                FROM Records
                WHERE DossierId = @DossierId
                AND   DeletedAt IS NULL",
                new { DossierId = dossierId });
            return max + 1;
        }

        /// <summary>
        /// Adds a new prisoner record to an existing dossier.
        /// Returns error string or null on success.
        /// </summary>
        public (string? Error, int RecordId) AddRecord(
            int dossierId,
            int sequenceNumber,
            string personName,
            string prisonerNumber,
            string? notes = null)
        {
            // ── Validate ──────────────────────────────
            if (string.IsNullOrWhiteSpace(personName))
                return ("اسم السجين مطلوب.", 0);

            prisonerNumber = prisonerNumber?.Trim() ?? "";

            if (prisonerNumber.Length != 10)
                return ("رقم السجين يجب أن يكون 10 أرقام بالضبط.", 0);

            if (!prisonerNumber.All(char.IsDigit))
                return ("رقم السجين يجب أن يحتوي على أرقام فقط.", 0);

            using var conn = _db.CreateConnection();

            // check prisoner number not duplicate in DB
            int existsInDb = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE PrisonerNumber = @PrisonerNumber
                AND   DeletedAt IS NULL",
                new { PrisonerNumber = prisonerNumber });
            if (existsInDb > 0)
                return ($"رقم السجين {prisonerNumber} موجود مسبقاً في النظام.", 0);

            // check sequence not duplicate in dossier
            int seqExists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE DossierId      = @DossierId
                AND   SequenceNumber = @SequenceNumber
                AND   DeletedAt IS NULL",
                new { DossierId = dossierId, SequenceNumber = sequenceNumber });
            if (seqExists > 0)
                return ($"التسلسل رقم {sequenceNumber} موجود مسبقاً في هذه الدوسية.", 0);

            // ── Insert ────────────────────────────────
            using var tx = conn.BeginTransaction();
            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

                int recordId = conn.ExecuteScalar<int>(@"
                    INSERT INTO Records
                        (DossierId, SequenceNumber, PersonName,
                         PrisonerNumber, Notes, Status,
                         CreatedByUserId, CreatedAt)
                    VALUES
                        (@DossierId, @SequenceNumber, @PersonName,
                         @PrisonerNumber, @Notes, 'Active',
                         @CreatedByUserId, @CreatedAt);
                    SELECT last_insert_rowid();",
                    new
                    {
                        DossierId = dossierId,
                        SequenceNumber = sequenceNumber,
                        PersonName = personName.Trim(),
                        PrisonerNumber = prisonerNumber,
                        Notes = string.IsNullOrWhiteSpace(notes)
                                            ? null : notes.Trim(),
                        CreatedByUserId = UserSession.CurrentUser?.UserId,
                        CreatedAt = now
                    }, tx);

                // update dossier UpdatedAt
                conn.Execute(@"
                    UPDATE Dossiers SET UpdatedAt = @Now
                    WHERE DossierId = @DossierId",
                    new { Now = now, DossierId = dossierId }, tx);

                WriteAudit(conn, AuditActions.RecordCreated,
                    $"تم إضافة سجل: {personName} ({prisonerNumber})",
                    "Record", recordId, tx);

                tx.Commit();
                return (null, recordId);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return ($"خطأ أثناء إضافة السجل: {ex.Message}", 0);
            }
        }

        public string? DeleteRecord(int recordId, string reason)
        {
            using var conn = _db.CreateConnection();
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            conn.Execute(@"
                UPDATE Records
                SET DeletedAt       = @Now,
                    DeletedByUserId = @UserId,
                    Status          = 'Deleted',
                    Notes           = COALESCE(Notes,'') || ' | حُذف: ' || @Reason
                WHERE RecordId = @RecordId",
                new
                {
                    Now = now,
                    UserId = UserSession.CurrentUser?.UserId,
                    Reason = reason,
                    RecordId = recordId
                });

            WriteAudit(conn, AuditActions.RecordDeleted,
                $"تم حذف السجل رقم {recordId} - السبب: {reason}",
                "Record", recordId);

            return null;
        }

        // ── Search ────────────────────────────────────

        public List<SearchResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();

            using var conn = _db.CreateConnection();
            return conn.Query<SearchResult>(@"
                SELECT
                    r.RecordId,
                    r.PersonName,
                    r.PrisonerNumber,
                    r.SequenceNumber,
                    d.DossierId,
                    d.DossierNumber,
                    d.HijriMonth,
                    d.HijriYear,
                    l.HallwayNumber,
                    l.CabinetNumber,
                    l.ShelfNumber
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE r.DeletedAt IS NULL
                AND (
                    r.PrisonerNumber  LIKE @Q  OR
                    r.PersonName      LIKE @Q  OR
                    CAST(d.DossierNumber AS TEXT) LIKE @Q
                )
                ORDER BY d.DossierNumber, r.SequenceNumber
                LIMIT 200",
                new { Q = $"%{query.Trim()}%" }).AsList();
        }

        private void WriteAudit(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            string actionType, string description,
            string entityType, int entityId,
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

    // ── Search result DTO ─────────────────────────────
    public class SearchResult
    {
        public int RecordId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string PrisonerNumber { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public int DossierId { get; set; }
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int? HallwayNumber { get; set; }
        public int? CabinetNumber { get; set; }
        public int? ShelfNumber { get; set; }

        public string LocationDisplay =>
            HallwayNumber.HasValue
                ? $"ممر {HallwayNumber} - كبينة {CabinetNumber} - رف {ShelfNumber}"
                : "غير محدد";

        public string HijriDisplay =>
            $"{HijriMonth}/{HijriYear}هـ";
    }
}