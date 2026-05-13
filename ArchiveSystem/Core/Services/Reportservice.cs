using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using ArchiveSystem.Views.Pages;
using Dapper;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArchiveSystem.Core.Services
{
    // ─── DTO for a single printable dossier face ────────────────────────────────
    public class DossierFaceData
    {
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int? ExpectedCount { get; set; }
        public string LocationDisplay { get; set; } = string.Empty;
        public List<DossierFaceRecord> Records { get; set; } = new();
    }

    public class DataQualityReportData
    {
        public string GeneratedAt { get; set; } = string.Empty;
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public int DossiersWithMismatch { get; set; }
        public int UnresolvedWarningsTotal { get; set; }
        public List<DataQualityWarningGroup> WarningGroups { get; set; } = new();
        public List<DataQualityMismatchRow> MismatchDossiers { get; set; } = new();
        public List<DataQualityFieldRow> MissingFieldRows { get; set; } = new();
    }

    public class DataQualityFieldRow
    {
        public string FieldLabel { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int TotalRecords { get; set; }
        public int FilledCount { get; set; }
        public int MissingCount => TotalRecords - FilledCount;
        public string FillRate => TotalRecords > 0
            ? $"{FilledCount * 100.0 / TotalRecords:F1}%"
            : "—";
    }

    public class DataQualityWarningGroup
    {
        public string WarningType { get; set; } = string.Empty;
        public int Count { get; set; }
        public string BatchFileName { get; set; } = string.Empty;
    }

    public class DataQualityMismatchRow
    {
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int ExpectedCount { get; set; }
        public int ActualCount { get; set; }
        public int Difference => ActualCount - ExpectedCount;
        public string HijriDisplay => $"{HijriMonth}/{HijriYear}هـ";
        public string DifferenceDisplay => Difference >= 0 ? $"+{Difference}" : Difference.ToString();
    }

    public class DossierFaceRecord
    {
        public int Sequence { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string PrisonerNumber { get; set; } = string.Empty;
        public string? Nationality { get; set; }
        public Dictionary<string, string?> ExtraFields { get; set; } = new();
    }

    // ─── DTO for monthly/weekly/yearly report ───────────────────────────────────
    public class PeriodReportData
    {
        public string Title { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public List<PeriodReportRow> Rows { get; set; } = new();
        public List<CustomField> ReportCustomFields { get; set; } = new();
        public Dictionary<int, Dictionary<int, string?>> RecordCustomValues { get; set; } = new();
        public Dictionary<string, List<(string Value, int Count)>> FieldAggregates { get; set; } = new();
    }

    public class PeriodReportRow
    {
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int RecordCount { get; set; }
        public string LocationDisplay { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    public class ReportService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        // ─── Shared style constants ────────────────────────────────────────────
        private static readonly string FontName = "Amiri";
        private static readonly string HeaderBg = Colors.Teal.Medium;
        private static readonly string HeaderFg = Colors.White;
        private static readonly string AltRowBg = Colors.Grey.Lighten4;
        private static readonly string TotalRowBg = Colors.Teal.Lighten4;
        private static readonly string AccentGreen = Colors.Teal.Darken3;
        private static readonly string AccentRed = Colors.Red.Darken3;
        private static readonly string AccentOrange = Colors.Orange.Darken3;

        // ─────────────────────────────────────────────────────────────────────
        // DATA LOADING
        // ─────────────────────────────────────────────────────────────────────

        public PeriodReportData LoadWeeklyReport(string gregorianDateFrom, string gregorianDateTo)
        {
            using var conn = _db.CreateConnection();

            var rows = conn.Query<PeriodReportRow>(@"
                SELECT
                    d.DossierNumber, d.HijriMonth, d.HijriYear, d.Status,
                    COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
                    COUNT(r.RecordId) AS RecordCount
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                LEFT JOIN Records r   ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.DeletedAt IS NULL
                  AND r.CreatedAt >= @From AND r.CreatedAt < @To
                GROUP BY d.DossierId
                ORDER BY d.HijriMonth, d.DossierNumber",
                new { From = gregorianDateFrom, To = gregorianDateTo }).AsList();

            var reportFields = LoadReportCustomFields(conn);
            var customValues = LoadCustomValuesForWeek(conn, gregorianDateFrom, gregorianDateTo, reportFields);

            return new PeriodReportData
            {
                Title = "تقرير أسبوعي",
                Period = $"{gregorianDateFrom} — {gregorianDateTo}",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows,
                ReportCustomFields = reportFields,
                RecordCustomValues = customValues,
                FieldAggregates = BuildFieldAggregates(customValues, reportFields)
            };
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // ADD THESE TWO METHODS to ReportService, inside the ReportService class body.
        //
        // Placement: paste them right before the TempPath() helper method, i.e. between
        // PrintAuditLogDirect() and the "SHARED CELL / LAYOUT HELPERS" region.
        // ─────────────────────────────────────────────────────────────────────────────

        // ─── DTO returned by GetBatchDossierRangeInfo ────────────────────────────────
        // Add this alongside the other DTOs at the top of the file (before ReportService class).
        public class BatchDossierRangeInfo
        {
            public int DossierCount { get; set; }
            public int RecordCount { get; set; }
        }

        // ─── Inside ReportService class ──────────────────────────────────────────────

        /// <summary>
        /// Returns a quick count of dossiers and total records that fall within the
        /// given DossierNumber range (inclusive).  Used by the UI to populate the
        /// info badge before the user commits to generating the PDF.
        /// </summary>
        public BatchDossierRangeInfo GetBatchDossierRangeInfo(int fromNumber, int toNumber)
        {
            using var conn = _db.CreateConnection();

            var info = conn.QueryFirstOrDefault<BatchDossierRangeInfo>(@"
        SELECT
            COUNT(DISTINCT d.DossierId)  AS DossierCount,
            COUNT(r.RecordId)            AS RecordCount
        FROM Dossiers d
        LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
        WHERE d.DeletedAt IS NULL
          AND d.DossierNumber >= @From
          AND d.DossierNumber <= @To",
                new { From = fromNumber, To = toNumber });

            return info ?? new BatchDossierRangeInfo();
        }

        /// <summary>
        /// Generates a single PDF file containing one dossier-face page per dossier
        /// whose DossierNumber falls in [fromNumber, toNumber].
        ///
        /// Dossiers with zero active records are silently skipped (matching the
        /// behaviour of the single-dossier face report).
        ///
        /// Returns null on success, or an Arabic error string on failure.
        /// </summary>
        public string? GenerateBatchDossierFacePdf(int fromNumber, int toNumber, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                // ── 1. Fetch all dossier IDs in range ─────────────────────────────
                using var conn = _db.CreateConnection();

                var dossierIds = conn.Query<int>(@"
            SELECT DossierId
            FROM   Dossiers
            WHERE  DeletedAt IS NULL
              AND  DossierNumber >= @From
              AND  DossierNumber <= @To
            ORDER BY DossierNumber",
                    new { From = fromNumber, To = toNumber }).AsList();

                if (dossierIds.Count == 0)
                    return $"لا توجد دوسيات في النطاق من {fromNumber} إلى {toNumber}.";

                // ── 2. Load face data for every dossier ───────────────────────────
                var faceDataList = new List<DossierFaceData>();
                foreach (var id in dossierIds)
                {
                    var face = LoadDossierFaceData(id);
                    if (face == null || face.Records.Count == 0)
                        continue;   // skip dossiers with no records (same as single-dossier behaviour)
                    faceDataList.Add(face);
                }

                if (faceDataList.Count == 0)
                    return $"جميع الدوسيات في النطاق من {fromNumber} إلى {toNumber} لا تحتوي على سجلات.";

                // ── 3. Build a single multi-page document ─────────────────────────
                string printDate = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");

                Document.Create(container =>
                {
                    foreach (var data in faceDataList)
                    {
                        var extraLabels = data.Records
                            .SelectMany(r => r.ExtraFields.Keys)
                            .Distinct()
                            .ToList();

                        bool showNat = data.Records.Any(r => r.Nationality != null);

                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4);
                            page.Margin(1.5f, Unit.Centimetre);
                            page.DefaultTextStyle(t => t.FontSize(10).FontFamily(FontName));
                            page.ContentFromRightToLeft();

                            // ── Header ────────────────────────────────────────────
                            page.Header().Column(col =>
                            {
                                col.Item().Height(6).Background(Colors.Teal.Medium);
                                col.Item().PaddingVertical(8).Row(row =>
                                {
                                    row.RelativeItem().Column(inner =>
                                    {
                                        inner.Item()
                                            .Text($"دوسية رقم  ({data.DossierNumber})")
                                            .FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                                        inner.Item().PaddingTop(2)
                                            .Text($"شهر {data.HijriMonth}  لعام {data.HijriYear}هـ")
                                            .FontSize(12).FontColor(Colors.Grey.Darken2);
                                    });

                                    row.ConstantItem(200).Column(inner =>
                                    {
                                        inner.Item().AlignLeft()
                                            .Text($"الموقع:  {data.LocationDisplay}")
                                            .FontSize(10).FontColor(Colors.Grey.Darken2);
                                        inner.Item().AlignLeft().PaddingTop(3)
                                            .Text($"عدد الملفات المتوقع:  {data.ExpectedCount?.ToString() ?? "غير محدد"}")
                                            .FontSize(10);
                                        inner.Item().AlignLeft().PaddingTop(3)
                                            .Text($"عدد السجلات الفعلي:  {data.Records.Count}")
                                            .FontSize(10);
                                        inner.Item().AlignLeft().PaddingTop(3)
                                            .Text($"تاريخ الطباعة:  {printDate}")
                                            .FontSize(9).FontColor(Colors.Grey.Medium);
                                    });
                                });
                                col.Item().PaddingBottom(4).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                            });

                            // ── Table ─────────────────────────────────────────────
                            page.Content().PaddingTop(8).Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(10);    // م
                                    cols.RelativeColumn(20);    // اسم السجين
                                    cols.RelativeColumn(30);    // رقم السجين
                                    if (showNat) cols.RelativeColumn(20);              // الجنسية
                                    foreach (var _ in extraLabels) cols.RelativeColumn(20); // extras
                                });

                                table.Header(h =>
                                {
                                    HeaderCell(h, "م");
                                    HeaderCell(h, "اسم السجين");
                                    HeaderCell(h, "رقم السجين");
                                    if (showNat) HeaderCell(h, "الجنسية");
                                    foreach (var lbl in extraLabels) HeaderCell(h, lbl);
                                });

                                int idx = 0;
                                foreach (var rec in data.Records)
                                {
                                    string bg = idx++ % 2 == 0 ? Colors.White : AltRowBg;
                                    DataCell(table, rec.Sequence.ToString(), bg, center: true);
                                    DataCell(table, rec.PersonName, bg);
                                    DataCell(table, rec.PrisonerNumber, bg, center: true);
                                    if (showNat) DataCell(table, rec.Nationality ?? "—", bg);
                                    foreach (var lbl in extraLabels)
                                    {
                                        rec.ExtraFields.TryGetValue(lbl, out var val);
                                        DataCell(table, val ?? "—", bg);
                                    }
                                }

                                // Totals row
                                int totalCols = 3 + (showNat ? 1 : 0) + extraLabels.Count;
                                TotalSpanCell(table, "إجمالي السجلات", TotalRowBg, colSpan: 2);
                                TotalCell(table, data.Records.Count.ToString(), TotalRowBg);
                                for (int i = 3; i < totalCols; i++)
                                    TotalCell(table, "", TotalRowBg);
                            });

                            // ── Footer ────────────────────────────────────────────
                            page.Footer().PaddingTop(4).Row(row =>
                            {
                                row.RelativeItem()
                                    .Text($"نظام أرشفة الملفات — {printDate}")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                row.ConstantItem(120).AlignCenter().Text(x =>
                                {
                                    x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                    x.CurrentPageNumber().FontSize(8);
                                    x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                    x.TotalPages().FontSize(8);
                                });
                                row.RelativeItem().AlignLeft()
                                    .Text($"دوسية رقم ({data.DossierNumber})")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }
                }).GeneratePdf(outputPath);

                // ── 4. Audit log entry ─────────────────────────────────────────────
                conn.Execute(@"
            INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
            VALUES (@UserId, 'ReportPrinted', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"طباعة مجموعة دوسيات من {fromNumber} إلى {toNumber} " +
                                 $"({faceDataList.Count} دوسية)",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء إنشاء ملف PDF المجمَّع: {ex.Message}";
            }
        }

        public DossierFaceData? LoadDossierFaceData(int dossierId)
        {
            using var conn = _db.CreateConnection();
            var dossier = conn.Query<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierId = @Id",
                (d, l) => { d.CurrentLocation = l; return d; },
                new { Id = dossierId }, splitOn: "LocationId").FirstOrDefault();

            if (dossier == null) return null;

            var records = conn.Query<Record>(@"
                SELECT * FROM Records
                WHERE DossierId = @DossierId AND DeletedAt IS NULL
                ORDER BY SequenceNumber",
                new { DossierId = dossierId }).AsList();

            if (records.Count == 0)
                return new DossierFaceData
                {
                    DossierNumber = dossier.DossierNumber,
                    HijriMonth = dossier.HijriMonth,
                    HijriYear = dossier.HijriYear,
                    ExpectedCount = dossier.ExpectedFileCount,
                    LocationDisplay = dossier.CurrentLocation?.DisplayName ?? "غير محدد",
                    Records = new()
                };

            var recIds = records.Select(r => r.RecordId).ToList();
            var natFieldId = conn.ExecuteScalar<int?>(
                "SELECT CustomFieldId FROM CustomFields WHERE FieldKey = 'nationality' AND IsActive = 1");

            var reportFields = conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInReports = 1
                ORDER BY SortOrder, ArabicLabel").AsList();

            Dictionary<int, Dictionary<int, string?>> allValues = new();
            if (reportFields.Count > 0 || natFieldId.HasValue)
            {
                var rawValues = conn.Query<RecordCustomFieldValue>($@"
                    SELECT RecordId, CustomFieldId, ValueText
                    FROM RecordCustomFieldValues
                    WHERE RecordId IN ({string.Join(",", recIds)})").AsList();

                foreach (var v in rawValues)
                {
                    if (!allValues.ContainsKey(v.RecordId)) allValues[v.RecordId] = new();
                    allValues[v.RecordId][v.CustomFieldId] = v.ValueText;
                }
            }

            var faceRecords = records.Select(r =>
            {
                allValues.TryGetValue(r.RecordId, out var vals);
                vals ??= new();

                string? nationality = natFieldId.HasValue && vals.TryGetValue(natFieldId.Value, out var n) ? n : null;

                var extras = new Dictionary<string, string?>();
                foreach (var cf in reportFields)
                {
                    if (natFieldId.HasValue && cf.CustomFieldId == natFieldId.Value) continue;
                    vals.TryGetValue(cf.CustomFieldId, out var val);
                    extras[cf.ArabicLabel] = val;
                }

                return new DossierFaceRecord
                {
                    Sequence = r.SequenceNumber,
                    PersonName = r.PersonName,
                    PrisonerNumber = r.PrisonerNumber,
                    Nationality = nationality,
                    ExtraFields = extras
                };
            }).ToList();

            return new DossierFaceData
            {
                DossierNumber = dossier.DossierNumber,
                HijriMonth = dossier.HijriMonth,
                HijriYear = dossier.HijriYear,
                ExpectedCount = dossier.ExpectedFileCount,
                LocationDisplay = dossier.CurrentLocation?.DisplayName ?? "غير محدد",
                Records = faceRecords
            };
        }

        public PeriodReportData LoadMonthlyReport(int hijriYear, int hijriMonth)
        {
            using var conn = _db.CreateConnection();

            var rows = conn.Query<PeriodReportRow>(@"
                SELECT
                    d.DossierNumber, d.HijriMonth, d.HijriYear, d.Status,
                    COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
                    COUNT(r.RecordId) AS RecordCount
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                LEFT JOIN Records r   ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.DeletedAt IS NULL
                  AND d.HijriYear = @Year AND d.HijriMonth = @Month
                GROUP BY d.DossierId
                ORDER BY d.DossierNumber",
                new { Year = hijriYear, Month = hijriMonth }).AsList();

            var reportFields = LoadReportCustomFields(conn);
            // FIX: was passing arguments in wrong order — now explicit named params
            var customValues = LoadCustomValuesForDossiers(conn, hijriMonth, hijriYear, null, reportFields);

            return new PeriodReportData
            {
                Title = "تقرير شهري",
                Period = $"شهر {hijriMonth} / {hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows,
                ReportCustomFields = reportFields,
                RecordCustomValues = customValues,
                FieldAggregates = BuildFieldAggregates(customValues, reportFields)
            };
        }

        public PeriodReportData LoadYearlyReport(int hijriYear)
        {
            using var conn = _db.CreateConnection();

            var rows = conn.Query<PeriodReportRow>(@"
                SELECT
                    d.DossierNumber, d.HijriMonth, d.HijriYear, d.Status,
                    COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
                    COUNT(r.RecordId) AS RecordCount
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                LEFT JOIN Records r   ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.DeletedAt IS NULL
                  AND d.HijriYear = @Year
                GROUP BY d.DossierId
                ORDER BY d.HijriMonth, d.DossierNumber",
                new { Year = hijriYear }).AsList();

            var reportFields = LoadReportCustomFields(conn);
            var customValues = LoadCustomValuesForDossiers(conn, null, null, hijriYear, reportFields);

            return new PeriodReportData
            {
                Title = "تقرير سنوي",
                Period = $"{hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows,
                ReportCustomFields = reportFields,
                RecordCustomValues = customValues,
                FieldAggregates = BuildFieldAggregates(customValues, reportFields)
            };
        }

        // ── Custom field helpers ───────────────────────────────────────────────

        private static List<CustomField> LoadReportCustomFields(
            Microsoft.Data.Sqlite.SqliteConnection conn) =>
            conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInReports = 1
                ORDER BY SortOrder, ArabicLabel").AsList();

        /// <summary>
        /// FIX: Replaced the old buggy overload (swapped month/year args, duplicate WHERE clauses).
        /// Parameters are now clearly named: hijriMonth=null means "all months".
        /// </summary>
        private static Dictionary<int, Dictionary<int, string?>> LoadCustomValuesForDossiers(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            int? hijriMonth, int? hijriYear, int? yearOnlyFilter,
            List<CustomField> fields)
        {
            if (fields.Count == 0) return new();

            var conditions = new List<string> { "d.DeletedAt IS NULL" };
            var p = new DynamicParameters();

            // Yearly-only filter (used by LoadYearlyReport)
            if (yearOnlyFilter.HasValue)
            {
                conditions.Add("d.HijriYear = @Year");
                p.Add("Year", yearOnlyFilter.Value);
            }
            // Month+year filter (used by LoadMonthlyReport)
            else if (hijriYear.HasValue && hijriMonth.HasValue)
            {
                conditions.Add("d.HijriYear  = @Year");
                conditions.Add("d.HijriMonth = @Month");
                p.Add("Year", hijriYear.Value);
                p.Add("Month", hijriMonth.Value);
            }

            string where = "WHERE " + string.Join(" AND ", conditions);
            string fieldIds = string.Join(",", fields.Select(f => f.CustomFieldId));

            var rawVals = conn.Query<RecordCustomFieldValue>($@"
                SELECT rcfv.RecordId, rcfv.CustomFieldId, rcfv.ValueText
                FROM RecordCustomFieldValues rcfv
                JOIN Records  r ON r.RecordId   = rcfv.RecordId  AND r.DeletedAt IS NULL
                JOIN Dossiers d ON d.DossierId  = r.DossierId
                {where}
                AND rcfv.CustomFieldId IN ({fieldIds})", p).AsList();

            var result = new Dictionary<int, Dictionary<int, string?>>();
            foreach (var v in rawVals)
            {
                if (!result.ContainsKey(v.RecordId)) result[v.RecordId] = new();
                result[v.RecordId][v.CustomFieldId] = v.ValueText;
            }
            return result;
        }

        private static Dictionary<int, Dictionary<int, string?>> LoadCustomValuesForWeek(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            string dateFrom, string dateTo,
            List<CustomField> fields)
        {
            if (fields.Count == 0) return new();

            string fieldIds = string.Join(",", fields.Select(f => f.CustomFieldId));
            var rawVals = conn.Query<RecordCustomFieldValue>($@"
                SELECT rcfv.RecordId, rcfv.CustomFieldId, rcfv.ValueText
                FROM RecordCustomFieldValues rcfv
                JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
                WHERE r.CreatedAt >= @From AND r.CreatedAt < @To
                  AND rcfv.CustomFieldId IN ({fieldIds})",
                new { From = dateFrom, To = dateTo }).AsList();

            var result = new Dictionary<int, Dictionary<int, string?>>();
            foreach (var v in rawVals)
            {
                if (!result.ContainsKey(v.RecordId)) result[v.RecordId] = new();
                result[v.RecordId][v.CustomFieldId] = v.ValueText;
            }
            return result;
        }

        private static Dictionary<string, List<(string Value, int Count)>> BuildFieldAggregates(
            Dictionary<int, Dictionary<int, string?>> recordCustomValues,
            List<CustomField> fields)
        {
            var result = new Dictionary<string, List<(string, int)>>();
            foreach (var cf in fields)
            {
                var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var perRecord in recordCustomValues.Values)
                {
                    if (!perRecord.TryGetValue(cf.CustomFieldId, out var val)) continue;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    val = val.Trim();
                    freq[val] = freq.TryGetValue(val, out int c) ? c + 1 : 1;
                }
                if (freq.Count == 0) continue;
                result[cf.ArabicLabel] = freq
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList();
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PDF GENERATION  (QuestPDF)
        // ─────────────────────────────────────────────────────────────────────

        public string? GenerateDossierFacePdf(DossierFaceData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                var extraLabels = data.Records.SelectMany(r => r.ExtraFields.Keys).Distinct().ToList();
                bool showNat = data.Records.Any(r => r.Nationality != null);
                string printDate = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily(FontName));
                        page.ContentFromRightToLeft();

                        // ── Header ────────────────────────────────────────────
                        page.Header().Column(col =>
                        {
                            // Top accent bar
                            col.Item().Height(6).Background(Colors.Teal.Medium);
                            col.Item().PaddingVertical(8).Row(row =>
                            {
                                // Right: dossier info block
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text($"دوسية رقم  ({data.DossierNumber})")
                                        .FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                                    inner.Item().PaddingTop(2)
                                        .Text($"شهر {data.HijriMonth}  لعام {data.HijriYear}هـ")
                                        .FontSize(12).FontColor(Colors.Grey.Darken2);
                                });

                                // Left: meta info
                                row.ConstantItem(200).Column(inner =>
                                {
                                    inner.Item().AlignLeft()
                                        .Text($"الموقع:  {data.LocationDisplay}")
                                        .FontSize(10).FontColor(Colors.Grey.Darken2);
                                    inner.Item().AlignLeft().PaddingTop(3)
                                        .Text($"عدد الملفات المتوقع:  {data.ExpectedCount?.ToString() ?? "غير محدد"}")
                                        .FontSize(10);
                                    inner.Item().AlignLeft().PaddingTop(3)
                                        .Text($"عدد السجلات الفعلي:  {data.Records.Count}")
                                        .FontSize(10);
                                    inner.Item().AlignLeft().PaddingTop(3)
                                        .Text($"تاريخ الطباعة:  {printDate}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                            });
                            col.Item().PaddingBottom(4).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                        });

                        // ── Table ─────────────────────────────────────────────
                        page.Content().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(10);     // م        = 5%
                                cols.RelativeColumn(20);    // اسم السجين = 40%
                                cols.RelativeColumn(30);    // رقم السجين = 25%
                                if (showNat) cols.RelativeColumn(20);          // الجنسية = 15%
                                foreach (var _ in extraLabels) cols.RelativeColumn(20); // each extra
                            });

                            table.Header(h =>
                            {
                                HeaderCell(h, "م");
                                HeaderCell(h, "اسم السجين");
                                HeaderCell(h, "رقم السجين");
                                if (showNat) HeaderCell(h, "الجنسية");
                                foreach (var lbl in extraLabels) HeaderCell(h, lbl);
                            });

                            // FIX: use index variable instead of Sequence for alternating rows
                            int idx = 0;
                            foreach (var rec in data.Records)
                            {
                                string bg = idx++ % 2 == 0 ? Colors.White : AltRowBg;
                                DataCell(table, rec.Sequence.ToString(), bg, center: true);
                                DataCell(table, rec.PersonName, bg);
                                DataCell(table, rec.PrisonerNumber, bg, center: true);
                                if (showNat) DataCell(table, rec.Nationality ?? "—", bg);
                                foreach (var lbl in extraLabels)
                                {
                                    rec.ExtraFields.TryGetValue(lbl, out var val);
                                    DataCell(table, val ?? "—", bg);
                                }
                            }

                            // ── Totals row ────────────────────────────────────
                            int totalCols = 3 + (showNat ? 1 : 0) + extraLabels.Count;
                            // Span label across first two cols, put count in third, blank the rest
                            TotalSpanCell(table, "إجمالي السجلات", TotalRowBg, colSpan: 2);
                            TotalCell(table, data.Records.Count.ToString(), TotalRowBg);
                            for (int i = 3; i < totalCols; i++)
                                TotalCell(table, "", TotalRowBg);
                        });

                        // ── Footer ────────────────────────────────────────────
                        page.Footer().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text($"نظام أرشفة الملفات — {printDate}")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                            row.ConstantItem(120).AlignCenter().Text(x =>
                            {
                                x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.CurrentPageNumber().FontSize(8);
                                x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.TotalPages().FontSize(8);
                            });
                            row.RelativeItem().AlignLeft()
                                .Text($"دوسية رقم ({data.DossierNumber})")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                }).GeneratePdf(outputPath);

                // Audit
                using var conn = _db.CreateConnection();
                conn.Execute(@"
                    INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                    VALUES (@UserId, 'ReportPrinted', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"طباعة واجهة دوسية رقم {data.DossierNumber}",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return null;
            }
            catch (Exception ex) { return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}"; }
        }

        // ── Period report (monthly / weekly / yearly) ─────────────────────────

        /// <summary>
        /// FIX: Removed the nested container.Page() call inside page.Content() which
        /// caused a QuestPDF render exception.  The aggregates summary is now a
        /// separate top-level page added to the same Document.Create() call.
        /// </summary>
        public string? GeneratePeriodReportPdf(PeriodReportData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                string printDate = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");

                Document.Create(container =>
                {
                    // ══ PAGE 1: dossier list ══════════════════════════════════
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily(FontName));
                        page.ContentFromRightToLeft();

                        page.Header().Column(col =>
                        {
                            col.Item().Height(6).Background(Colors.Teal.Medium);
                            col.Item().PaddingVertical(6).Row(row =>
                            {
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text(data.Title)
                                        .FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                                    inner.Item().PaddingTop(2).Text(data.Period)
                                        .FontSize(12).FontColor(Colors.Grey.Darken2);
                                });
                                row.ConstantItem(200).Column(inner =>
                                {
                                    inner.Item().AlignLeft()
                                        .Text($"إجمالي الدوسيات:  {data.TotalDossiers}")
                                        .FontSize(10);
                                    inner.Item().AlignLeft().PaddingTop(3)
                                        .Text($"إجمالي الملفات:  {data.TotalRecords}")
                                        .FontSize(10);
                                    inner.Item().AlignLeft().PaddingTop(3)
                                        .Text($"تاريخ الطباعة:  {printDate}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                            });
                            col.Item().PaddingBottom(4).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                        });

                        page.Content().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(23);    // رقم الدوسية  = 15%
                                cols.RelativeColumn(27);    // الشهر/السنة  = 20%
                                cols.RelativeColumn(17);    // عدد الملفات  = 13%
                                cols.RelativeColumn(10);    // الموقع       = 37%
                                cols.RelativeColumn(22);    // الحالة       = 15%
                                                            // total = 100
                            });

                            table.Header(h =>
                            {
                                HeaderCell(h, "رقم الدوسية");
                                HeaderCell(h, "الشهر / السنة");
                                HeaderCell(h, "عدد الملفات");
                                HeaderCell(h, "الموقع");
                                HeaderCell(h, "الحالة");
                            });

                            // FIX: index-based alternating — no O(n²) IndexOf
                            int idx = 0;
                            foreach (var row in data.Rows)
                            {
                                string bg = idx++ % 2 == 0 ? Colors.White : AltRowBg;
                                DataCell(table, row.DossierNumber.ToString(), bg, center: true);
                                DataCell(table, $"{row.HijriMonth}/{row.HijriYear}هـ", bg, center: true);
                                DataCell(table, row.RecordCount.ToString(), bg, center: true);
                                DataCell(table, row.LocationDisplay, bg);
                                DataCell(table, TranslateStatus(row.Status), bg, center: true);
                            }

                            // ── Totals row ────────────────────────────────────
                            TotalSpanCell(table, "الإجمالي", TotalRowBg, colSpan: 2);
                            TotalCell(table, data.TotalRecords.ToString(), TotalRowBg, center: true);
                            TotalCell(table, "", TotalRowBg);
                            TotalCell(table, "", TotalRowBg);
                        });

                        page.Footer().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text($"نظام أرشفة الملفات — {printDate}")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                            row.ConstantItem(120).AlignCenter().Text(x =>
                            {
                                x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.CurrentPageNumber().FontSize(8);
                                x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.TotalPages().FontSize(8);
                            });
                            row.RelativeItem().AlignLeft().Text(data.Period)
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // ══ PAGE 2: custom-field aggregates (if any) ═════════════
                    if (data.FieldAggregates.Count > 0)
                    {
                        container.Page(page2 =>
                        {
                            page2.Size(PageSizes.A4);
                            page2.Margin(1.5f, Unit.Centimetre);
                            page2.DefaultTextStyle(t => t.FontSize(10).FontFamily(FontName));
                            page2.ContentFromRightToLeft();

                            page2.Header().Column(col =>
                            {
                                col.Item().Height(6).Background(Colors.Teal.Medium);
                                col.Item().PaddingVertical(6).Column(inner =>
                                {
                                    inner.Item().AlignCenter()
                                        .Text($"ملخص الحقول المخصصة — {data.Period}")
                                        .FontSize(16).Bold().FontColor(Colors.Teal.Darken3);
                                    inner.Item().AlignCenter().PaddingTop(2)
                                        .Text($"تاريخ الطباعة:  {printDate}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                });
                                col.Item().PaddingBottom(4)
                                    .LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                            });

                            page2.Content().PaddingTop(8).Column(col =>
                            {
                                foreach (var (fieldLabel, values) in data.FieldAggregates)
                                {
                                    int fieldTotal = values.Sum(v => v.Count);

                                    col.Item().PaddingBottom(3).Row(row =>
                                    {
                                        row.RelativeItem()
                                            .Background(Colors.Teal.Lighten5)
                                            .Padding(6)
                                            .Text(fieldLabel)
                                            .Bold().FontSize(12).FontColor(Colors.Teal.Darken3);
                                    });

                                    col.Item().Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.RelativeColumn(3);
                                            c.ConstantColumn(65);
                                            c.ConstantColumn(75);
                                        });
                                        t.Header(h =>
                                        {
                                            HeaderCell(h, "القيمة");
                                            HeaderCell(h, "العدد");
                                            HeaderCell(h, "النسبة %");
                                        });

                                        int vi = 0;
                                        foreach (var (value, count) in values)
                                        {
                                            string bg = vi++ % 2 == 0 ? Colors.White : AltRowBg;
                                            double pct = fieldTotal > 0 ? count * 100.0 / fieldTotal : 0;
                                            DataCell(t, value, bg);
                                            DataCell(t, count.ToString(), bg, center: true);
                                            DataCell(t, $"{pct:F1}%", bg, center: true);
                                        }
                                        // Total row
                                        TotalCell(t, "الإجمالي", TotalRowBg);
                                        TotalCell(t, fieldTotal.ToString(), TotalRowBg, center: true);
                                        TotalCell(t, "100%", TotalRowBg, center: true);
                                    });

                                    col.Item().PaddingVertical(10);
                                }
                            });

                            page2.Footer().PaddingTop(4).Row(row =>
                            {
                                row.RelativeItem().Text($"نظام أرشفة الملفات — {printDate}")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                row.ConstantItem(120).AlignCenter().Text(x =>
                                {
                                    x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                    x.CurrentPageNumber().FontSize(8);
                                    x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                    x.TotalPages().FontSize(8);
                                });
                                row.RelativeItem().AlignLeft().Text(data.Period)
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                        });
                    }
                }).GeneratePdf(outputPath);

                return null;
            }
            catch (Exception ex) { return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}"; }
        }

        // ── Data Quality report ───────────────────────────────────────────────

        public DataQualityReportData LoadDataQualityReport()
        {
            using var conn = _db.CreateConnection();

            int totalDossiers = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Dossiers WHERE DeletedAt IS NULL");
            int totalRecords = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");

            var warningGroups = conn.Query<DataQualityWarningGroup>(@"
                SELECT iw.WarningType, COUNT(*) AS Count,
                       COALESCE(ib.FileName,'') AS BatchFileName
                FROM ImportWarnings iw
                LEFT JOIN ImportBatches ib ON ib.ImportBatchId = iw.ImportBatchId
                WHERE iw.IsResolved = 0
                GROUP BY iw.WarningType
                ORDER BY Count DESC").AsList();

            int unresolvedTotal = warningGroups.Sum(g => g.Count);

            var mismatchDossiers = conn.Query<DataQualityMismatchRow>(@"
                SELECT d.DossierNumber, d.HijriMonth, d.HijriYear,
                       d.ExpectedFileCount AS ExpectedCount,
                       COUNT(r.RecordId)   AS ActualCount
                FROM Dossiers d
                LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.DeletedAt IS NULL AND d.ExpectedFileCount IS NOT NULL
                GROUP BY d.DossierId
                HAVING COUNT(r.RecordId) != d.ExpectedFileCount
                ORDER BY ABS(COUNT(r.RecordId) - d.ExpectedFileCount) DESC
                LIMIT 100").AsList();

            var fields = conn.Query<(int Id, string Label, bool Required)>(@"
                SELECT CustomFieldId, ArabicLabel, IsRequired
                FROM CustomFields WHERE IsActive = 1
                ORDER BY SortOrder, ArabicLabel").AsList();

            var fieldRows = new List<DataQualityFieldRow>();
            foreach (var (id, label, required) in fields)
            {
                string? fieldCreatedAt = conn.ExecuteScalar<string?>(
                    "SELECT CreatedAt FROM CustomFields WHERE CustomFieldId = @Id", new { Id = id });

                int relevantTotal = string.IsNullOrEmpty(fieldCreatedAt)
                    ? totalRecords
                    : conn.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM Records
                        WHERE DeletedAt IS NULL AND CreatedAt >= @FieldCreatedAt",
                        new { FieldCreatedAt = fieldCreatedAt });

                int filled = conn.ExecuteScalar<int>(@"
                    SELECT COUNT(DISTINCT rcfv.RecordId)
                    FROM RecordCustomFieldValues rcfv
                    JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
                    WHERE rcfv.CustomFieldId = @Id
                      AND rcfv.ValueText IS NOT NULL AND rcfv.ValueText != ''",
                    new { Id = id });

                fieldRows.Add(new DataQualityFieldRow
                {
                    FieldLabel = label,
                    IsRequired = required,
                    TotalRecords = relevantTotal,
                    FilledCount = filled
                });
            }

            return new DataQualityReportData
            {
                GeneratedAt = DateTime.Now.ToString("yyyy/MM/dd  HH:mm:ss"),
                TotalDossiers = totalDossiers,
                TotalRecords = totalRecords,
                DossiersWithMismatch = mismatchDossiers.Count,
                UnresolvedWarningsTotal = unresolvedTotal,
                WarningGroups = warningGroups,
                MismatchDossiers = mismatchDossiers,
                MissingFieldRows = fieldRows
            };
        }

        public string? GenerateDataQualityReportPdf(DataQualityReportData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                string printDate = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily(FontName));
                        page.ContentFromRightToLeft();

                        // ── Header ────────────────────────────────────────────
                        page.Header().Column(col =>
                        {
                            col.Item().Height(6).Background(Colors.Teal.Medium);
                            col.Item().PaddingVertical(6).Column(inner =>
                            {
                                inner.Item().AlignCenter()
                                    .Text("تقرير جودة البيانات والأخطاء")
                                    .FontSize(18).Bold().FontColor(Colors.Teal.Darken3);
                                inner.Item().AlignCenter().PaddingTop(2)
                                    .Text($"تاريخ التقرير:  {data.GeneratedAt}")
                                    .FontSize(10).FontColor(Colors.Grey.Darken2);
                                inner.Item().AlignCenter().PaddingTop(2)
                                    .Text($"إجمالي الدوسيات: {data.TotalDossiers}    |    إجمالي السجلات: {data.TotalRecords:N0}")
                                    .FontSize(10);
                            });

                            // Summary badge row
                            col.Item().Padding(6).Row(row =>
                            {
                                SummaryBadge(row,
                                    $"تحذيرات غير محلولة:  {data.UnresolvedWarningsTotal}",
                                    data.UnresolvedWarningsTotal > 0 ? Colors.Red.Lighten4 : Colors.Green.Lighten4,
                                    data.UnresolvedWarningsTotal > 0 ? Colors.Red.Darken3 : Colors.Green.Darken3);

                                row.ConstantItem(12);

                                SummaryBadge(row,
                                    $"دوسيات بتعداد غير مطابق:  {data.DossiersWithMismatch}",
                                    data.DossiersWithMismatch > 0 ? Colors.Orange.Lighten4 : Colors.Green.Lighten4,
                                    data.DossiersWithMismatch > 0 ? Colors.Orange.Darken3 : Colors.Green.Darken3);
                            });

                            col.Item().PaddingBottom(4).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                        });

                        // ── Content ───────────────────────────────────────────
                        page.Content().PaddingTop(8).Column(col =>
                        {
                            // Section 1 — Unresolved warnings
                            if (data.WarningGroups.Count > 0)
                            {
                                SectionTitle(col, "أولاً: التحذيرات غير المحلولة من الاستيراد", AccentRed);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3);
                                        c.ConstantColumn(65);
                                        c.RelativeColumn(2);
                                    });
                                    t.Header(h =>
                                    {
                                        HeaderCell(h, "نوع التحذير");
                                        HeaderCell(h, "العدد");
                                        HeaderCell(h, "الدفعة");
                                    });

                                    int wi = 0;
                                    foreach (var g in data.WarningGroups)
                                    {
                                        string bg = wi++ % 2 == 0 ? Colors.White : AltRowBg;
                                        // FIX: translate warning type to Arabic
                                        DataCell(t, TranslateWarningType(g.WarningType), bg);
                                        DataCell(t, g.Count.ToString(), bg, center: true);
                                        DataCell(t, g.BatchFileName, bg);
                                    }

                                    // Totals
                                    int total = data.WarningGroups.Sum(g => g.Count);
                                    TotalCell(t, "الإجمالي", TotalRowBg);
                                    TotalCell(t, total.ToString(), TotalRowBg, center: true);
                                    TotalCell(t, "", TotalRowBg);
                                });

                                col.Item().PaddingVertical(10);
                            }

                            // Section 2 — Count mismatches
                            if (data.MismatchDossiers.Count > 0)
                            {
                                SectionTitle(col, "ثانياً: الدوسيات ذات التعداد غير المطابق", AccentOrange);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(85);
                                        c.ConstantColumn(95);
                                        c.ConstantColumn(75);
                                        c.ConstantColumn(75);
                                        c.ConstantColumn(65);
                                    });
                                    t.Header(h =>
                                    {
                                        HeaderCell(h, "رقم الدوسية");
                                        HeaderCell(h, "التاريخ الهجري");
                                        HeaderCell(h, "المتوقع");
                                        HeaderCell(h, "الفعلي");
                                        HeaderCell(h, "الفارق");
                                    });

                                    int mi = 0;
                                    foreach (var r in data.MismatchDossiers)
                                    {
                                        string bg = mi++ % 2 == 0 ? Colors.White : AltRowBg;
                                        // Highlight rows where difference is large
                                        if (Math.Abs(r.Difference) > 5) bg = Colors.Orange.Lighten5;
                                        DataCell(t, r.DossierNumber.ToString(), bg, center: true);
                                        DataCell(t, r.HijriDisplay, bg, center: true);
                                        DataCell(t, r.ExpectedCount.ToString(), bg, center: true);
                                        DataCell(t, r.ActualCount.ToString(), bg, center: true);
                                        DataCell(t, r.DifferenceDisplay, bg, center: true);
                                    }
                                });

                                col.Item().PaddingVertical(10);
                            }

                            // Section 3 — Field fill rates
                            if (data.MissingFieldRows.Count > 0)
                            {
                                SectionTitle(col, "ثالثاً: نسبة تعبئة الحقول المخصصة", AccentGreen);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2);
                                        c.ConstantColumn(55);
                                        c.ConstantColumn(75);
                                        c.ConstantColumn(75);
                                        c.ConstantColumn(80);
                                    });
                                    t.Header(h =>
                                    {
                                        HeaderCell(h, "الحقل");
                                        HeaderCell(h, "مطلوب");
                                        HeaderCell(h, "مُعبأ");
                                        HeaderCell(h, "فارغ");
                                        HeaderCell(h, "نسبة التعبئة");
                                    });

                                    int fi = 0;
                                    foreach (var f in data.MissingFieldRows)
                                    {
                                        string bg = f.IsRequired && f.MissingCount > 0
                                            ? Colors.Red.Lighten5
                                            : (fi % 2 == 0 ? Colors.White : AltRowBg);
                                        fi++;
                                        DataCell(t, f.FieldLabel, bg);
                                        DataCell(t, f.IsRequired ? "✓" : "—", bg, center: true);
                                        DataCell(t, f.FilledCount.ToString(), bg, center: true);
                                        DataCell(t, f.MissingCount.ToString(), bg, center: true);
                                        DataCell(t, f.FillRate, bg, center: true);
                                    }
                                });
                            }

                            // All-clear message
                            if (data.WarningGroups.Count == 0
                                && data.MismatchDossiers.Count == 0
                                && data.MissingFieldRows.All(f => f.MissingCount == 0))
                            {
                                col.Item().PaddingTop(24).AlignCenter()
                                    .Text("✅ لا توجد مشكلات في جودة البيانات")
                                    .FontSize(14).FontColor(Colors.Green.Darken2).Bold();
                            }
                        });

                        // ── Footer ────────────────────────────────────────────
                        page.Footer().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem()
                                .Text($"نظام أرشفة الملفات — {printDate}")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                            row.ConstantItem(120).AlignCenter().Text(x =>
                            {
                                x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.CurrentPageNumber().FontSize(8);
                                x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.TotalPages().FontSize(8);
                            });
                            row.RelativeItem().AlignLeft()
                                .Text("تقرير جودة البيانات")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                }).GeneratePdf(outputPath);

                return null;
            }
            catch (Exception ex) { return $"خطأ أثناء إنشاء تقرير الجودة: {ex.Message}"; }
        }

        // ── Audit log PDF ─────────────────────────────────────────────────────

        public string? GenerateAuditLogPdf(
            List<AuditLogRow> rows, string outputPath, string periodLabel = "")
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                string printDate = DateTime.Now.ToString("yyyy/MM/dd  HH:mm");

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(1.2f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(9).FontFamily(FontName));
                        page.ContentFromRightToLeft();

                        page.Header().Column(col =>
                        {
                            col.Item().Height(6).Background(Colors.Teal.Medium);
                            col.Item().PaddingVertical(6).Column(inner =>
                            {
                                inner.Item().AlignCenter()
                                    .Text("سجل الأحداث — وثاق")
                                    .FontSize(16).Bold().FontColor(Colors.Teal.Darken3);
                                if (!string.IsNullOrWhiteSpace(periodLabel))
                                    inner.Item().AlignCenter().PaddingTop(2).Text(periodLabel)
                                        .FontSize(11).FontColor(Colors.Grey.Darken2);
                                inner.Item().AlignCenter().PaddingTop(2)
                                    .Text($"تاريخ الطباعة: {printDate}    |    إجمالي السجلات: {rows.Count:N0}")
                                    .FontSize(10).FontColor(Colors.Grey.Darken1);
                            });
                            col.Item().PaddingBottom(4).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                        });

                        page.Content().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(115);
                                cols.ConstantColumn(95);
                                cols.ConstantColumn(115);
                                cols.ConstantColumn(75);
                                cols.RelativeColumn();
                            });
                            table.Header(h =>
                            {
                                HeaderCell(h, "التاريخ والوقت");
                                HeaderCell(h, "المستخدم");
                                HeaderCell(h, "نوع الحدث");
                                HeaderCell(h, "الكيان");
                                HeaderCell(h, "الوصف");
                            });

                            int idx = 0;
                            foreach (var row in rows)
                            {
                                string bg = row.ActionType switch
                                {
                                    "LoginFailure" => Colors.Red.Lighten5,
                                    "RecordDeleted" or "DossierDeleted" => Colors.Orange.Lighten5,
                                    "RestoreCompleted" => Colors.Blue.Lighten5,
                                    _ => idx % 2 == 0 ? Colors.White : AltRowBg
                                };
                                idx++;
                                DataCell(table, row.CreatedAt ?? "", bg);
                                DataCell(table, row.UserFullName ?? "(نظام)", bg);
                                DataCell(table, row.ActionTypeArabic, bg);
                                DataCell(table, row.EntityDisplay, bg);
                                DataCell(table, row.Description, bg);
                            }
                        });

                        page.Footer().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text($"نظام أرشفة الملفات — {printDate}")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                            row.ConstantItem(120).AlignCenter().Text(x =>
                            {
                                x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.CurrentPageNumber().FontSize(8);
                                x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                                x.TotalPages().FontSize(8);
                            });
                            row.RelativeItem().AlignLeft()
                                .Text("سجل الأحداث")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                }).GeneratePdf(outputPath);

                using var conn = _db.CreateConnection();
                conn.Execute(@"
                    INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                    VALUES (@UserId, 'ReportPrinted', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"تصدير سجل الأحداث إلى PDF — {rows.Count} سجل",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return null;
            }
            catch (Exception ex) { return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}"; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // DIRECT PRINT
        // ─────────────────────────────────────────────────────────────────────

        public string? PrintDossierFaceDirect(DossierFaceData data)
        {
            string tmp = TempPath($"dossier_face_{data.DossierNumber}");
            var err = GenerateDossierFacePdf(data, tmp);
            return err ?? OpenPdfForDirectPrint(tmp, $"طباعة دوسية رقم {data.DossierNumber}");
        }

        public string? PrintPeriodReportDirect(PeriodReportData data)
        {
            string tmp = TempPath("period_report");
            var err = GeneratePeriodReportPdf(data, tmp);
            return err ?? OpenPdfForDirectPrint(tmp, data.Title);
        }

        public string? PrintDataQualityReportDirect(DataQualityReportData data)
        {
            string tmp = TempPath("data_quality");
            var err = GenerateDataQualityReportPdf(data, tmp);
            return err ?? OpenPdfForDirectPrint(tmp, "تقرير جودة البيانات");
        }

        public string? PrintAuditLogDirect(List<AuditLogRow> rows, string periodLabel = "")
        {
            string tmp = TempPath("audit_log");
            var err = GenerateAuditLogPdf(rows, tmp, periodLabel);
            return err ?? OpenPdfForDirectPrint(tmp, "سجل الأحداث");
        }

        private static string TempPath(string name) =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"{name}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        private static string? OpenPdfForDirectPrint(string pdfPath, string _)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(pdfPath)
                { Verb = "print", UseShellExecute = true, CreateNoWindow = true };
                try { System.Diagnostics.Process.Start(psi); return null; } catch { }
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) { return $"خطأ أثناء إرسال الطباعة: {ex.Message}"; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SHARED CELL / LAYOUT HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Teal header cell with bold white text, centered.</summary>
        private static void HeaderCell(TableCellDescriptor h, string text)
        {
            h.Cell()
                .Background(HeaderBg)
                .BorderBottom(1).BorderColor(Colors.Teal.Darken2)
                .Padding(5)
                .AlignCenter()
                .Text(text).Bold().FontColor(HeaderFg).FontSize(10);
        }

        /// <summary>Standard data cell.</summary>
        private static void DataCell(TableDescriptor table, string text,
            string bg, bool center = false)
        {
            var cell = table.Cell()
                .Background(bg)
                .BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                .Padding(4);

            if (center)
                cell.AlignCenter().Text(text).FontSize(10).FontColor(Colors.Black);
            else
                cell.AlignRight().Text(text).FontSize(10).FontColor(Colors.Black);
        }

        /// <summary>Bold totals cell (single column).</summary>
        private static void TotalCell(TableDescriptor table, string text,
            string bg, bool center = false)
        {
            var cell = table.Cell()
                .Background(bg)
                .BorderTop(1).BorderColor(Colors.Teal.Medium)
                .Padding(4);

            if (center)
                cell.AlignCenter().Text(text).Bold().FontSize(10);
            else
                cell.AlignRight().Text(text).Bold().FontSize(10); // explicit
        }

        /// <summary>
        /// Totals cell that spans multiple columns (QuestPDF ColumnSpan).
        /// </summary>
        private static void TotalSpanCell(TableDescriptor table, string text,
            string bg, uint colSpan = 1)
        {
            table.Cell().ColumnSpan(colSpan)
                .Background(bg)
                .BorderTop(1).BorderColor(Colors.Teal.Medium)
                .Padding(4)
                .AlignRight()          // ← add this
                .Text(text).Bold().FontSize(10);
        }

        /// <summary>Coloured section heading inside a Column.</summary>
        private static void SectionTitle(ColumnDescriptor col, string title, string color)
        {
            col.Item()
                .Background(Colors.Grey.Lighten3)
                .PaddingHorizontal(6).PaddingVertical(5)
                .Text(title)
                .Bold().FontSize(12).FontColor(color);
            col.Item().Height(2).Background(color);
            col.Item().PaddingBottom(6);
        }

        /// <summary>Coloured summary badge (used in Data Quality header).</summary>
        private static void SummaryBadge(RowDescriptor row,
            string text, string bgColor, string textColor)
        {
            row.RelativeItem()
                .Background(bgColor)
                .CornerRadius(4)
                .Padding(8)
                .AlignCenter()
                .Text(text).Bold().FontSize(11).FontColor(textColor);
        }

        // ─────────────────────────────────────────────────────────────────────
        // TRANSLATION HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static string TranslateStatus(string s) => s switch
        {
            "Open" => "مفتوح",
            "Complete" => "مكتمل",
            "NeedsReview" => "مراجعة",
            "Archived" => "مؤرشف",
            _ => s
        };

        /// <summary>
        /// FIX: Data Quality report was showing raw English warning type strings.
        /// Now fully translated to Arabic.
        /// </summary>
        private static string TranslateWarningType(string t) => t switch
        {
            ImportWarningTypes.MissingDossierMetadata => "بيانات الدوسية مفقودة",
            ImportWarningTypes.MissingHeaderRow => "صف الترويسة مفقود",
            ImportWarningTypes.CountMismatch => "عدد الملفات غير متطابق",
            ImportWarningTypes.MissingSequence => "رقم التسلسل مفقود",
            ImportWarningTypes.DuplicateSequence => "تسلسل مكرر",
            ImportWarningTypes.SequenceGap => "فجوة في التسلسل",
            ImportWarningTypes.InvalidSequence => "تسلسل غير صحيح",
            ImportWarningTypes.MissingName => "الاسم مفقود",
            ImportWarningTypes.SuspiciousName => "اسم مشكوك فيه",
            ImportWarningTypes.MissingPrisonerNumber => "رقم السجين مفقود",
            ImportWarningTypes.InvalidPrisonerNumber => "رقم السجين غير صحيح",
            ImportWarningTypes.DuplicateInSheet => "رقم سجين مكرر في الشيت",
            ImportWarningTypes.DuplicateInImport => "رقم سجين مكرر في الاستيراد",
            ImportWarningTypes.DuplicateInDatabase => "رقم سجين موجود في قاعدة البيانات",
            ImportWarningTypes.MixedLocationInDossier => "مواقع متعددة في الدوسية",
            ImportWarningTypes.InvalidLocation => "موقع غير موجود في النظام",
            ImportWarningTypes.SheetNameTitleMismatch => "اسم الشيت لا يتطابق مع رقم الدوسية",
            _ => t
        };
    }
}