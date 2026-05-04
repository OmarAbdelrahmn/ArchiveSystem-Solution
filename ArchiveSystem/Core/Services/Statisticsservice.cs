using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    // ─── Summary card numbers ───────────────────────────────────────────────────
    public class ArchiveSummary
    {
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public int RecordsToday { get; set; }
        public int RecordsThisMonth { get; set; }
        public int IncompleteDossiers { get; set; }
        public int CompleteDossiers { get; set; }
        public int TotalLocations { get; set; }
    }

    // ─── Row for "records by month" chart ──────────────────────────────────────
    public class MonthlyCount
    {
        public int HijriYear { get; set; }
        public int HijriMonth { get; set; }
        public int Count { get; set; }
        public string Label => $"{HijriMonth}/{HijriYear}";
    }

    // ─── Row for "records by week" chart ───────────────────────────────────────
    public class WeeklyCount
    {
        public int Year { get; set; }
        public int Week { get; set; }
        public int Count { get; set; }

        /// <summary>ISO week label shown in the grid, e.g. "2024 - أسبوع 03"</summary>
        public string Label => $"{Year} - أسبوع {Week:D2}";
    }

    // ─── Row for custom field statistics ────────────────────────────────────────
    public class CustomFieldStat
    {
        public string Value { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool IsEmpty { get; set; }
    }

    // ─── Row for records-per-location ───────────────────────────────────────────
    public class LocationStat
    {
        public int HallwayNumber { get; set; }
        public int CabinetNumber { get; set; }
        public int ShelfNumber { get; set; }
        public int DossierCount { get; set; }
        public int RecordCount { get; set; }
        public string Display => $"ممر {HallwayNumber} - كبينة {CabinetNumber} - رف {ShelfNumber}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    public class StatisticsService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        // ── Top-level summary ──────────────────────────────────────────────────
        public ArchiveSummary GetSummary()
        {
            using var conn = _db.CreateConnection();

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string thisMonthStart = DateTime.UtcNow.ToString("yyyy-MM-") + "01";

            return new ArchiveSummary
            {
                TotalDossiers = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Dossiers WHERE DeletedAt IS NULL"),

                TotalRecords = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL"),

                RecordsToday = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL AND CreatedAt LIKE @Day",
                    new { Day = today + "%" }),

                RecordsThisMonth = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL AND CreatedAt >= @Start",
                    new { Start = thisMonthStart }),

                CompleteDossiers = conn.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM Dossiers d
                    WHERE ExpectedFileCount IS NOT NULL
                    AND   ExpectedFileCount = (
                        SELECT COUNT(*) FROM Records r
                        WHERE r.DossierId = d.DossierId AND r.DeletedAt IS NULL)"),

                IncompleteDossiers = conn.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM Dossiers d
                    WHERE ExpectedFileCount IS NOT NULL
                    AND   ExpectedFileCount != (
                        SELECT COUNT(*) FROM Records r
                        WHERE r.DossierId = d.DossierId AND r.DeletedAt IS NULL)"),

                TotalLocations = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Locations WHERE IsActive = 1"),
            };
        }

        public double GetAverageDailyEntries()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<double>(@"
        SELECT CAST(COUNT(*) AS REAL) /
               MAX(1, CAST(julianday('now') - julianday(MIN(CreatedAt)) AS INTEGER))
        FROM Records
        WHERE DeletedAt IS NULL");
        }


        // ── Records by Hijri month ─────────────────────────────────────────────
        public List<MonthlyCount> GetMonthlyBreakdown(int? filterYear = null, int topN = 18)
        {
            using var conn = _db.CreateConnection();

            string yearFilter = filterYear.HasValue
                ? $"AND d.HijriYear = {filterYear.Value}"
                : string.Empty;

            return conn.Query<MonthlyCount>($@"
    SELECT d.HijriYear, d.HijriMonth, COUNT(r.RecordId) AS Count
    FROM Dossiers d
    LEFT JOIN Records r 
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
    WHERE d.DeletedAt IS NULL
      {yearFilter}
    GROUP BY d.HijriYear, d.HijriMonth
    ORDER BY d.HijriYear DESC, d.HijriMonth DESC
    LIMIT @TopN",
            new { TopN = topN }).AsList();
        }

        // ── Records by Gregorian calendar week ────────────────────────────────
        public List<WeeklyCount> GetWeeklyBreakdown(int? filterYear = null, int topN = 16)
        {
            using var conn = _db.CreateConnection();

            string yearFilter = filterYear.HasValue
                ? $"AND strftime('%Y', CreatedAt) = '{filterYear.Value}'"
                : string.Empty;

            return conn.Query<WeeklyCount>($@"
                SELECT
                    CAST(strftime('%Y', CreatedAt) AS INTEGER) AS Year,
                    CAST(strftime('%W', CreatedAt) AS INTEGER) AS Week,
                    COUNT(*)                                    AS Count
                FROM Records
                WHERE DeletedAt IS NULL
                {yearFilter}
                GROUP BY Year, Week
                ORDER BY Year DESC, Week DESC
                LIMIT @TopN",
                new { TopN = topN }).AsList();
        }

        public List<int> GetDistinctRecordYears()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<int>(@"
                SELECT DISTINCT CAST(strftime('%Y', CreatedAt) AS INTEGER) AS Year
                FROM Records
                WHERE DeletedAt IS NULL
                ORDER BY Year DESC")
                .AsList();
        }

        // ── Dossier completion status breakdown ────────────────────────────────
        public (int Complete, int Incomplete, int Unknown) GetCompletionBreakdown()
        {
            using var conn = _db.CreateConnection();
            var rows = conn.Query<(string Status, int Count)>(@"
SELECT
    CASE
        WHEN ExpectedFileCount IS NULL THEN 'Unknown'
        WHEN ExpectedFileCount = ActualCount THEN 'Complete'
        ELSE 'Incomplete'
    END AS Status,
    COUNT(*) AS Count
FROM (
    SELECT
        d.DossierId,
        d.ExpectedFileCount,
        COUNT(r.RecordId) AS ActualCount
    FROM Dossiers d
    LEFT JOIN Records r
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
    WHERE d.DeletedAt IS NULL
    GROUP BY d.DossierId, d.ExpectedFileCount
) t
GROUP BY Status;").AsList();

            int complete = 0, incomplete = 0, unknown = 0;
            foreach (var (status, count) in rows)
            {
                if (status == "Complete") complete = count;
                else if (status == "Incomplete") incomplete = count;
                else unknown = count;
            }
            return (complete, incomplete, unknown);
        }

        // ── Top locations by dossier count ─────────────────────────────────────
        public List<LocationStat> GetLocationStats(int topN = 10)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<LocationStat>(@"
    SELECT
        l.HallwayNumber,
        l.CabinetNumber,
        l.ShelfNumber,
        COUNT(DISTINCT d.DossierId) AS DossierCount,
        COUNT(r.RecordId)           AS RecordCount
    FROM Locations l
    LEFT JOIN Dossiers d
        ON d.CurrentLocationId = l.LocationId
       AND d.DeletedAt IS NULL
    LEFT JOIN Records r
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
    WHERE l.IsActive = 1
    GROUP BY l.LocationId, l.HallwayNumber, l.CabinetNumber, l.ShelfNumber
    ORDER BY DossierCount DESC
    LIMIT @TopN",
                new { TopN = topN }).AsList();
        }

        // ── Custom field value distribution ───────────────────────────────────
        public List<CustomFieldStat> GetCustomFieldStats(int customFieldId,
       int topN = 15, int? hijriYear = null)
        {
            using var conn = _db.CreateConnection();

            // Build optional Hijri year JOIN condition
            string yearJoin = hijriYear.HasValue
                ? "JOIN Dossiers d ON d.DossierId = r.DossierId AND d.HijriYear = @HijriYear"
                : string.Empty;

            var p = new DynamicParameters();
            p.Add("Id", customFieldId);
            p.Add("TopN", topN);
            if (hijriYear.HasValue)
                p.Add("HijriYear", hijriYear.Value);

            // Filled values
            var filled = conn.Query<CustomFieldStat>($@"
        SELECT rcfv.ValueText AS Value, COUNT(*) AS Count, 0 AS IsEmpty
        FROM RecordCustomFieldValues rcfv
        JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
        {yearJoin}
        WHERE rcfv.CustomFieldId = @Id
        AND   rcfv.ValueText IS NOT NULL AND rcfv.ValueText != ''
        GROUP BY rcfv.ValueText
        ORDER BY Count DESC
        LIMIT @TopN", p).AsList();

            // Denominator
            string? fieldCreatedAt = conn.ExecuteScalar<string?>(
                "SELECT CreatedAt FROM CustomFields WHERE CustomFieldId = @Id",
                new { Id = customFieldId });

            int relevantTotal;
            if (hijriYear.HasValue)
            {
                // Scoped to the selected Hijri year only
                var tp = new DynamicParameters();
                tp.Add("HijriYear", hijriYear.Value);
                if (!string.IsNullOrEmpty(fieldCreatedAt))
                    tp.Add("FieldCreatedAt", fieldCreatedAt);

                relevantTotal = conn.ExecuteScalar<int>($@"
            SELECT COUNT(*)
            FROM Records r
            JOIN Dossiers d ON d.DossierId = r.DossierId AND d.HijriYear = @HijriYear
            WHERE r.DeletedAt IS NULL
            {(!string.IsNullOrEmpty(fieldCreatedAt) ? "AND r.CreatedAt >= @FieldCreatedAt" : "")}",
                    tp);
            }
            else if (string.IsNullOrEmpty(fieldCreatedAt))
            {
                relevantTotal = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");
            }
            else
            {
                relevantTotal = conn.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM Records
            WHERE DeletedAt IS NULL AND CreatedAt >= @FieldCreatedAt",
                    new { FieldCreatedAt = fieldCreatedAt });
            }

            // Filled count scoped to year if needed
            var fp = new DynamicParameters();
            fp.Add("Id", customFieldId);
            if (hijriYear.HasValue)
                fp.Add("HijriYear", hijriYear.Value);

            int filledCount = conn.ExecuteScalar<int>($@"
        SELECT COUNT(DISTINCT rcfv.RecordId)
        FROM RecordCustomFieldValues rcfv
        JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
        {yearJoin}
        WHERE rcfv.CustomFieldId = @Id
        AND   rcfv.ValueText IS NOT NULL AND rcfv.ValueText != ''", fp);

            int emptyCount = relevantTotal - filledCount;
            if (emptyCount > 0)
                filled.Add(new CustomFieldStat
                {
                    Value = "غير مدخلة",
                    Count = emptyCount,
                    IsEmpty = true
                });

            return filled;
        }

        // ── Distinct stat-enabled custom fields ────────────────────────────────
        public List<Models.CustomField> GetStatEnabledFields()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Models.CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND EnableStatistics = 1
                ORDER BY SortOrder, ArabicLabel").AsList();
        }
    }
}