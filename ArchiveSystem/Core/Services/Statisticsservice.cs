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
                LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL AND WHERE d.DeletedAt IS NULL
                WHERE 1=1 {yearFilter}
                GROUP BY d.HijriYear, d.HijriMonth
                ORDER BY d.HijriYear DESC, d.HijriMonth DESC
                LIMIT @TopN",
                new { TopN = topN }).AsList();
        }

        // ── Records by Gregorian calendar week ────────────────────────────────
        /// <summary>
        /// Returns record counts grouped by ISO calendar week (strftime %W = 00-53).
        /// <paramref name="topN"/> controls how many of the most-recent weeks are returned.
        /// Pass <paramref name="filterYear"/> to restrict to a single Gregorian year.
        /// </summary>
        public List<WeeklyCount> GetWeeklyBreakdown(int? filterYear = null, int topN = 16)
        {
            using var conn = _db.CreateConnection();

            // Build an optional year WHERE clause.
            // CreatedAt is stored as ISO 8601 text ("yyyy-MM-ddTHH:mm:ss"), so
            // strftime('%Y', ...) works correctly.
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

        /// <summary>
        /// Returns the distinct Gregorian years present in the Records table,
        /// newest first.  Used to populate the year filter drop-down for the
        /// weekly breakdown.
        /// </summary>
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
                    SELECT d.DossierId,
                           d.ExpectedFileCount,
                           COUNT(r.RecordId) AS ActualCount
                    FROM Dossiers d
                    LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL AND d.DeletedAt IS NULL
                    GROUP BY d.DossierId, d.ExpectedFileCount
                ) t
                GROUP BY Status").AsList();

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
                LEFT JOIN Dossiers d ON d.CurrentLocationId = l.LocationId
                LEFT JOIN Records  r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL AND d.DeletedAt IS NULL
                WHERE l.IsActive = 1
                GROUP BY l.LocationId, l.HallwayNumber, l.CabinetNumber, l.ShelfNumber
                ORDER BY DossierCount DESC
                LIMIT @TopN",
                new { TopN = topN }).AsList();
        }

        // ── Custom field value distribution ───────────────────────────────────
        public List<CustomFieldStat> GetCustomFieldStats(int customFieldId, int topN = 15)
        {
            using var conn = _db.CreateConnection();

            // filled values
            var filled = conn.Query<CustomFieldStat>(@"
                SELECT ValueText AS Value, COUNT(*) AS Count, 0 AS IsEmpty
                FROM RecordCustomFieldValues
                WHERE CustomFieldId = @Id
                AND   ValueText IS NOT NULL AND ValueText != ''
                GROUP BY ValueText
                ORDER BY Count DESC
                LIMIT @TopN",
                new { Id = customFieldId, TopN = topN }).AsList();

            // count of records with no value for this field
            int totalActive = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");

            int filledCount = conn.ExecuteScalar<int>(@"
                SELECT COUNT(DISTINCT RecordId)
                FROM RecordCustomFieldValues
                WHERE CustomFieldId = @Id
                AND   ValueText IS NOT NULL AND ValueText != ''",
                new { Id = customFieldId });

            int emptyCount = totalActive - filledCount;
            if (emptyCount > 0)
            {
                filled.Add(new CustomFieldStat
                {
                    Value = "غير مدخلة",
                    Count = emptyCount,
                    IsEmpty = true
                });
            }

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