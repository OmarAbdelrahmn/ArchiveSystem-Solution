using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    // ── Filter parameters DTO ─────────────────────────────────────────────────
    public record AllDataFilter
    {
        public string? NameQuery { get; set; }
        public string? PrisonerNumber { get; set; }
        public int? DossierNumber { get; set; }
        public int? HijriYear { get; set; }
        public int? HijriMonth { get; set; }
        public int? HallwayNumber { get; set; }
        public int? CabinetNumber { get; set; }
        public int? ShelfNumber { get; set; }

        /// <summary>"Active" | "Deleted" | "All"</summary>
        public string StatusFilter { get; set; } = "Active";

        public string? SortColumn { get; set; } = "DossierNumber";
        public bool SortAscending { get; set; } = true;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 100;

        // custom field filters: key = CustomFieldId, value = filter text ("__EMPTY__" means null/empty)
        public Dictionary<int, string> CustomFieldFilters { get; set; } = new();
    }

    // ── Row returned to the UI ─────────────────────────────────────────────────
    public class AllDataRow
    {
        public int RecordId { get; set; }
        public int DossierId { get; set; }
        public int DossierNumber { get; set; }
        public int SequenceNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string PrisonerNumber { get; set; } = string.Empty;
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int? HallwayNumber { get; set; }
        public int? CabinetNumber { get; set; }
        public int? ShelfNumber { get; set; }
        public string? UpdatedAt { get; set; }
        public string? CreatedAt { get; set; }
        public string? DeletedAt { get; set; }

        // filled after the base query by a separate join
        public Dictionary<int, string?> CustomValues { get; set; } = new();

        public string LocationDisplay =>
            HallwayNumber.HasValue
                ? $"ممر {HallwayNumber} - كبينة {CabinetNumber} - رف {ShelfNumber}"
                : "غير محدد";

        public string HijriDisplay => $"{HijriMonth}/{HijriYear}هـ";

        public string StatusDisplay => DeletedAt == null ? "نشط" : "محذوف";
    }

    // ── Paged result ──────────────────────────────────────────────────────────
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    // ─────────────────────────────────────────────────────────────────────────
    public class AllDataService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        // ── Active custom fields that should appear in AllData ────────────────
        public List<CustomField> GetAllDataCustomFields()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInAllData = 1
                ORDER BY SortOrder, ArabicLabel").AsList();
        }

        // ── Paged filtered query ──────────────────────────────────────────────
        public PagedResult<AllDataRow> GetFiltered(AllDataFilter filter)
        {
            using var conn = _db.CreateConnection();

            var (where, p) = BuildWhere(filter);

            // Count
            int total = conn.ExecuteScalar<int>($@"
                SELECT COUNT(DISTINCT r.RecordId)
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                {where}",
                p);

            // Sort
            string orderBy = filter.SortColumn switch
            {
                "PersonName" => "r.PersonName",
                "PrisonerNumber" => "r.PrisonerNumber",
                "SequenceNumber" => "r.SequenceNumber",
                "HijriYear" => "d.HijriYear, d.HijriMonth",
                "Location" => "l.HallwayNumber, l.CabinetNumber, l.ShelfNumber",
                "UpdatedAt" => "COALESCE(r.UpdatedAt, r.CreatedAt)",
                "Status" => "r.DeletedAt",
                _ => "d.DossierNumber"
            };
            string dir = filter.SortAscending ? "ASC" : "DESC";
            int offset = (filter.Page - 1) * filter.PageSize;

            var queryParams = new DynamicParameters(p);
            queryParams.Add("PageSize", filter.PageSize);
            queryParams.Add("Offset", offset);

            var rows = conn.Query<AllDataRow>($@"
                SELECT
                    r.RecordId,
                    r.DossierId,
                    d.DossierNumber,
                    r.SequenceNumber,
                    r.PersonName,
                    r.PrisonerNumber,
                    d.HijriMonth,
                    d.HijriYear,
                    l.HallwayNumber,
                    l.CabinetNumber,
                    l.ShelfNumber,
                    r.UpdatedAt,
                    r.CreatedAt,
                    r.DeletedAt
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                {where}
                ORDER BY {orderBy} {dir}
                LIMIT @PageSize OFFSET @Offset",
                queryParams).AsList();

            // Attach custom field values for this page
            if (rows.Count > 0)
            {
                var recordIds = rows.Select(r => r.RecordId).ToList();
                var values = conn.Query<RecordCustomFieldValue>($@"
                    SELECT rcfv.RecordId, rcfv.CustomFieldId, rcfv.ValueText
                    FROM RecordCustomFieldValues rcfv
                    WHERE rcfv.RecordId IN ({string.Join(",", recordIds)})").AsList();

                var lookup = values.GroupBy(v => v.RecordId)
                    .ToDictionary(g => g.Key,
                                  g => g.ToDictionary(v => v.CustomFieldId, v => v.ValueText));

                foreach (var row in rows)
                {
                    if (lookup.TryGetValue(row.RecordId, out var cv))
                        row.CustomValues = cv!;
                }
            }

            return new PagedResult<AllDataRow>
            {
                Items = rows,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        // ── ALL record IDs matching the current filter (no paging) ────────────
        /// <summary>
        /// Returns every active RecordId that satisfies <paramref name="filter"/>,
        /// ignoring Page / PageSize entirely.  Used by "تعبئة الكل" so the user
        /// can bulk-fill across all pages, not just the visible one.
        /// Deleted records are always excluded regardless of StatusFilter because
        /// soft-deleted records must never be bulk-filled.
        /// </summary>
        public List<int> GetFilteredIds(AllDataFilter filter)
        {
            using var conn = _db.CreateConnection();

            // Always force active-only for bulk fill
            var activeFilter = filter with { StatusFilter = "Active" };
            var (where, p) = BuildWhere(activeFilter);

            return conn.Query<int>($@"
                SELECT DISTINCT r.RecordId
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                {where}
                ORDER BY r.RecordId",
                p).AsList();
        }

        // ── Bulk fill custom field ─────────────────────────────────────────────
        public (string? Error, int Count) BulkFillCustomField(
            List<int> recordIds,
            int customFieldId,
            string? value)
        {
            if (recordIds.Count == 0)
                return ("لم يتم اختيار أي سجل.", 0);

            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;

                foreach (var rid in recordIds)
                {
                    conn.Execute(@"
                        INSERT INTO RecordCustomFieldValues
                            (RecordId, CustomFieldId, ValueText, UpdatedAt, UpdatedByUserId)
                        VALUES (@RecordId, @FieldId, @Value, @Now, @UserId)
                        ON CONFLICT(RecordId, CustomFieldId)
                        DO UPDATE SET
                            ValueText       = @Value,
                            UpdatedAt       = @Now,
                            UpdatedByUserId = @UserId",
                        new
                        {
                            RecordId = rid,
                            FieldId = customFieldId,
                            Value = value,
                            Now = now,
                            UserId = userId
                        }, tx);
                }

                var fieldLabel = conn.ExecuteScalar<string>(
                    "SELECT ArabicLabel FROM CustomFields WHERE CustomFieldId = @Id",
                    new { Id = customFieldId }, tx) ?? customFieldId.ToString();

                conn.Execute(@"
                    INSERT INTO AuditLog
                        (UserId, ActionType, EntityType, Description, CreatedAt)
                    VALUES (@UserId, @ActionType, 'Record', @Desc, @Now)",
                    new
                    {
                        UserId = userId,
                        ActionType = AuditActions.BulkFieldUpdate,
                        Desc = $"تعبئة جماعية لحقل '{fieldLabel}' = '{value}' على {recordIds.Count} سجل",
                        Now = now
                    }, tx);

                conn.Execute(@"
                    INSERT INTO BulkFieldUpdateBatches
                        (CustomFieldId, NewValue, RecordCount, ExecutedByUserId, ExecutedAt)
                    VALUES (@FieldId, @Value, @Count, @UserId, @Now)",
                    new
                    {
                        FieldId = customFieldId,
                        Value = value,
                        Count = recordIds.Count,
                        UserId = userId,
                        Now = now
                    }, tx);

                tx.Commit();
                return (null, recordIds.Count);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return ($"خطأ أثناء التعبئة: {ex.Message}", 0);
            }
        }

        // ── Suggestion list for TextWithSuggestions ────────────────────────────
        public List<string> GetRecentSuggestions(int customFieldId, int limit = 8)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<string>(@"
                SELECT DISTINCT ValueText
                FROM RecordCustomFieldValues
                WHERE CustomFieldId = @Id
                AND   ValueText IS NOT NULL
                AND   ValueText != ''
                ORDER BY UpdatedAt DESC
                LIMIT @Limit",
                new { Id = customFieldId, Limit = limit }).AsList();
        }

        // ── Export to list (for CSV export) ───────────────────────────────────
        public List<AllDataRow> GetAllForExport(AllDataFilter filter)
        {
            var unlimitedFilter = filter with { Page = 1, PageSize = 99_999 };
            return GetFiltered(unlimitedFilter).Items;
        }

        // ── Distinct year list (for filter dropdown) ───────────────────────────
        public List<int> GetDistinctYears()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<int>(@"
                SELECT DISTINCT HijriYear FROM Dossiers
                WHERE DeletedAt IS NULL
                ORDER BY HijriYear DESC").AsList();
        }

        // ── WHERE clause builder ──────────────────────────────────────────────
        private (string Sql, DynamicParameters Params) BuildWhere(AllDataFilter f)
        {
            var conditions = new List<string>();
            var p = new DynamicParameters();

            conditions.Add("d.DeletedAt IS NULL");

            switch (f.StatusFilter)
            {
                case "Active":
                    conditions.Add("r.DeletedAt IS NULL");
                    break;
                case "Deleted":
                    conditions.Add("r.DeletedAt IS NOT NULL");
                    break;
                    // "All" → no condition
            }

            if (!string.IsNullOrWhiteSpace(f.NameQuery))
            {
                conditions.Add("r.PersonName LIKE @Name");
                p.Add("Name", $"%{f.NameQuery.Trim()}%");
            }
            if (!string.IsNullOrWhiteSpace(f.PrisonerNumber))
            {
                conditions.Add("r.PrisonerNumber LIKE @PNum");
                p.Add("PNum", $"%{f.PrisonerNumber.Trim()}%");
            }
            if (f.DossierNumber.HasValue)
            {
                conditions.Add("d.DossierNumber = @DossierNum");
                p.Add("DossierNum", f.DossierNumber.Value);
            }
            if (f.HijriYear.HasValue)
            {
                conditions.Add("d.HijriYear = @HYear");
                p.Add("HYear", f.HijriYear.Value);
            }
            if (f.HijriMonth.HasValue)
            {
                conditions.Add("d.HijriMonth = @HMonth");
                p.Add("HMonth", f.HijriMonth.Value);
            }
            if (f.HallwayNumber.HasValue)
            {
                conditions.Add("l.HallwayNumber = @Hallway");
                p.Add("Hallway", f.HallwayNumber.Value);
            }
            if (f.CabinetNumber.HasValue)
            {
                conditions.Add("l.CabinetNumber = @Cabinet");
                p.Add("Cabinet", f.CabinetNumber.Value);
            }
            if (f.ShelfNumber.HasValue)
            {
                conditions.Add("l.ShelfNumber = @Shelf");
                p.Add("Shelf", f.ShelfNumber.Value);
            }

            // custom field filters via EXISTS sub-select
            int cfIdx = 0;
            foreach (var (cfId, cfVal) in f.CustomFieldFilters)
            {
                string pName = $"cfVal{cfIdx}";
                if (cfVal == "__EMPTY__")
                {
                    conditions.Add($@"NOT EXISTS (
                        SELECT 1 FROM RecordCustomFieldValues
                        WHERE RecordId = r.RecordId
                        AND CustomFieldId = {cfId}
                        AND ValueText IS NOT NULL AND ValueText != '')");
                }
                else
                {
                    conditions.Add($@"EXISTS (
                        SELECT 1 FROM RecordCustomFieldValues
                        WHERE RecordId = r.RecordId
                        AND CustomFieldId = {cfId}
                        AND ValueText LIKE @{pName})");
                    p.Add(pName, $"%{cfVal}%");
                }
                cfIdx++;
            }

            string sql = conditions.Count > 0
                ? "WHERE " + string.Join(" AND ", conditions)
                : string.Empty;

            return (sql, p);
        }
    }
}