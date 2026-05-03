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

        public Record? GetRecordById(int recordId)
        {
            using var conn = _db.CreateConnection();
            return conn.QuerySingleOrDefault<Record>(
                "SELECT * FROM Records WHERE RecordId = @Id",
                new { Id = recordId });
        }

        public int GetNextSequenceNumber(int dossierId)
        {
            using var conn = _db.CreateConnection();
            int max = conn.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(SequenceNumber), 0)
                FROM Records
                WHERE DossierId = @DossierId AND DeletedAt IS NULL",
                new { DossierId = dossierId });
            return max + 1;
        }

        /// <summary>Adds a new prisoner record to an existing dossier.</summary>
        public (string? Error, int RecordId) AddRecord(
            int dossierId, int sequenceNumber,
            string personName, string prisonerNumber,
            string? notes = null)
        {
            if (string.IsNullOrWhiteSpace(personName))
                return ("اسم السجين مطلوب.", 0);

            prisonerNumber = prisonerNumber?.Trim() ?? "";
            if (prisonerNumber.Length != 10)
                return ("رقم السجين يجب أن يكون 10 أرقام بالضبط.", 0);
            if (!prisonerNumber.All(char.IsDigit))
                return ("رقم السجين يجب أن يحتوي على أرقام فقط.", 0);

            using var conn = _db.CreateConnection();

            int existsInDb = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE PrisonerNumber = @PrisonerNumber AND DeletedAt IS NULL",
                new { PrisonerNumber = prisonerNumber });
            if (existsInDb > 0)
                return ($"رقم السجين {prisonerNumber} موجود مسبقاً في النظام.", 0);

            int seqExists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE DossierId = @DossierId AND SequenceNumber = @SequenceNumber
                AND DeletedAt IS NULL",
                new { DossierId = dossierId, SequenceNumber = sequenceNumber });
            if (seqExists > 0)
                return ($"التسلسل رقم {sequenceNumber} موجود مسبقاً في هذه الدوسية.", 0);

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
                        Notes = string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes.Trim(),
                        CreatedByUserId = UserSession.CurrentUser?.UserId,
                        CreatedAt = now
                    }, tx);

                conn.Execute("UPDATE Dossiers SET UpdatedAt = @Now WHERE DossierId = @DossierId",
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

        /// <summary>Updates an existing record's editable fields.</summary>
        public string? UpdateRecord(int recordId, string personName,
            string prisonerNumber, string? notes)
        {
            if (string.IsNullOrWhiteSpace(personName))
                return "اسم السجين مطلوب.";

            prisonerNumber = prisonerNumber?.Trim() ?? "";
            if (prisonerNumber.Length != 10 || !prisonerNumber.All(char.IsDigit))
                return "رقم السجين يجب أن يكون 10 أرقام.";

            using var conn = _db.CreateConnection();

            // ── Capture old values BEFORE the update ─────────────────────────
            var old = conn.QuerySingleOrDefault<Record>(
                "SELECT * FROM Records WHERE RecordId = @Id",
                new { Id = recordId });

            // Check duplicate prisoner number (excluding this record)
            int dupCheck = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE PrisonerNumber = @PNum AND RecordId != @RecordId AND DeletedAt IS NULL",
                new { PNum = prisonerNumber, RecordId = recordId });
            if (dupCheck > 0)
                return $"رقم السجين {prisonerNumber} موجود مسبقاً في النظام.";

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            conn.Execute(@"
                UPDATE Records
                SET PersonName      = @PersonName,
                    PrisonerNumber  = @PrisonerNumber,
                    Notes           = @Notes,
                    UpdatedAt       = @Now
                WHERE RecordId = @RecordId",
                new
                {
                    PersonName = personName.Trim(),
                    PrisonerNumber = prisonerNumber,
                    Notes = string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes.Trim(),
                    Now = now,
                    RecordId = recordId
                });

            // ── Serialize old / new snapshots ─────────────────────────────────
            string? oldJson = old == null ? null : System.Text.Json.JsonSerializer.Serialize(new
            {
                old.PersonName,
                old.PrisonerNumber,
                Notes = old.Notes ?? ""
            });

            string newJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                PersonName = personName.Trim(),
                PrisonerNumber = prisonerNumber,
                Notes = notes?.Trim() ?? ""
            });

            WriteAudit(conn, AuditActions.RecordEdited,
                $"تم تعديل السجل رقم {recordId}: {personName} ({prisonerNumber})",
                "Record", recordId,
                oldJson: oldJson, newJson: newJson);

            return null;
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

        // ── Search ──────────────────────────────────
        public List<SearchResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();
            using var conn = _db.CreateConnection();
            return conn.Query<SearchResult>(@"
                SELECT r.RecordId, r.PersonName, r.PrisonerNumber, r.SequenceNumber,
                       d.DossierId, d.DossierNumber, d.HijriMonth, d.HijriYear,
                       l.HallwayNumber, l.CabinetNumber, l.ShelfNumber
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE r.DeletedAt IS NULL
                AND (r.PrisonerNumber LIKE @Q OR r.PersonName LIKE @Q
                     OR CAST(d.DossierNumber AS TEXT) LIKE @Q)
                ORDER BY d.DossierNumber, r.SequenceNumber
                LIMIT 200",
                new { Q = $"%{query.Trim()}%" }).AsList();
        }

        /// <summary>Search including a custom-field value filter.</summary>
        public List<SearchResult> SearchWithCustomField(
            string query, int customFieldId, string fieldValue)
        {
            if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(fieldValue))
                return new();

            using var conn = _db.CreateConnection();
            var conditions = new List<string> { "r.DeletedAt IS NULL" };
            var p = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(query))
            {
                conditions.Add("(r.PrisonerNumber LIKE @Q OR r.PersonName LIKE @Q OR CAST(d.DossierNumber AS TEXT) LIKE @Q)");
                p.Add("Q", $"%{query.Trim()}%");
            }
            if (!string.IsNullOrWhiteSpace(fieldValue))
            {
                conditions.Add(@"EXISTS (
                    SELECT 1 FROM RecordCustomFieldValues
                    WHERE RecordId = r.RecordId
                    AND CustomFieldId = @CfId AND ValueText LIKE @CfVal)");
                p.Add("CfId", customFieldId);
                p.Add("CfVal", $"%{fieldValue.Trim()}%");
            }

            string where = "WHERE " + string.Join(" AND ", conditions);
            return conn.Query<SearchResult>($@"
                SELECT r.RecordId, r.PersonName, r.PrisonerNumber, r.SequenceNumber,
                       d.DossierId, d.DossierNumber, d.HijriMonth, d.HijriYear,
                       l.HallwayNumber, l.CabinetNumber, l.ShelfNumber
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                {where}
                ORDER BY d.DossierNumber, r.SequenceNumber
                LIMIT 200", p).AsList();
        }

        public (int TotalDossiers, int TotalRecords, int RecordsToday, int RecordsThisMonth, int NationalityCount)
           GetDashboardStats()
        {
            using var conn = _db.CreateConnection();
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string monthStart = DateTime.UtcNow.ToString("yyyy-MM-") + "01";

            int td = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Dossiers");
            int tr = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");
            int today_ = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL AND CreatedAt LIKE @D",
                new { D = today + "%" });
            int month = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL AND CreatedAt >= @S",
                new { S = monthStart });

            int natCount = conn.ExecuteScalar<int>(@"
        SELECT COUNT(DISTINCT rcfv.ValueText)
        FROM RecordCustomFieldValues rcfv
        JOIN CustomFields cf ON cf.CustomFieldId = rcfv.CustomFieldId
        JOIN Records r ON r.RecordId = rcfv.RecordId
        WHERE cf.FieldKey = 'nationality'
        AND   r.DeletedAt IS NULL
        AND   rcfv.ValueText IS NOT NULL
        AND   rcfv.ValueText != ''");

            return (td, tr, today_, month, natCount);
        }

        // ── Recent dossiers (last modified) ──────────
        public List<RecentDossierEntry> GetRecentDossiers(int limit = 8)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<RecentDossierEntry>(@"
                SELECT d.DossierId, d.DossierNumber, d.HijriMonth, d.HijriYear,
                       COUNT(r.RecordId) AS RecordCount,
                       MAX(COALESCE(r.UpdatedAt, r.CreatedAt)) AS LastActivity,
                       l.HallwayNumber, l.CabinetNumber, l.ShelfNumber
                FROM Dossiers d
                LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                GROUP BY d.DossierId
                ORDER BY LastActivity DESC NULLS LAST
                LIMIT @Limit",
                new { Limit = limit }).AsList();
        }

        private void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection conn,
            string actionType, string description,
            string entityType, int entityId,
            Microsoft.Data.Sqlite.SqliteTransaction? tx = null,
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
    }

    // ── DTOs ──────────────────────────────────────────────
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

        public string HijriDisplay => $"{HijriMonth}/{HijriYear}هـ";
    }

    public class RecentDossierEntry
    {
        public int DossierId { get; set; }
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int RecordCount { get; set; }
        public string? LastActivity { get; set; }
        public int? HallwayNumber { get; set; }
        public int? CabinetNumber { get; set; }
        public int? ShelfNumber { get; set; }
        public string HijriDisplay => $"{HijriMonth}/{HijriYear}هـ";
        public string LocationDisplay =>
            HallwayNumber.HasValue
                ? $"ممر {HallwayNumber}-{CabinetNumber}-{ShelfNumber}"
                : "—";
    }
}