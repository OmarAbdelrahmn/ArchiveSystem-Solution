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
        // Additional custom field values keyed by ArabicLabel
        public Dictionary<string, string?> ExtraFields { get; set; } = new();
    }

    // ─── DTO for monthly/weekly report ─────────────────────────────────────────
    public class PeriodReportData
    {
        public string Title { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public List<PeriodReportRow> Rows { get; set; } = new();
        // Custom report fields (ShowInReports = true)
        public List<CustomField> ReportCustomFields { get; set; } = new();
        // Values per record: RecordId → (CustomFieldId → ValueText)
        public Dictionary<int, Dictionary<int, string?>> RecordCustomValues { get; set; } = new();
        // ── أضف هذا ──────────────────────────────────────────────────────────
        /// <summary>
        /// Per-field aggregation for the summary section.
        /// Key = ArabicLabel, Value = list of (ValueText, Count) ordered by count desc.
        /// </summary>
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

        // ─────────────────────────────────────────────────────────────────────
        // DATA LOADING
        // ─────────────────────────────────────────────────────────────────────

        public PeriodReportData LoadWeeklyReport(string gregorianDateFrom, string gregorianDateTo)
        {
            using var conn = _db.CreateConnection();

            var rows = conn.Query<PeriodReportRow>(@"
    SELECT
        d.DossierNumber,
        d.HijriMonth,
        d.HijriYear,
        d.Status,
        COALESCE(
            l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber,
            'غير محدد'
        ) AS LocationDisplay,
        COUNT(r.RecordId) AS RecordCount
    FROM Dossiers d
    LEFT JOIN Locations l
        ON l.LocationId = d.CurrentLocationId
    LEFT JOIN Records r
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
    WHERE d.DeletedAt IS NULL
      AND r.CreatedAt >= @From
      AND r.CreatedAt < @To
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
                FieldAggregates = BuildFieldAggregates(customValues, reportFields) // ← أضف هذا
            };
        }

        /// <summary>
        /// Collapses RecordCustomValues into per-field value-frequency maps.
        /// </summary>
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

        private static Dictionary<int, Dictionary<int, string?>> LoadCustomValuesForWeek(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            string dateFrom, string dateTo,
            List<CustomField> fields)
        {
            if (fields.Count == 0) return new();

            var fieldIds = string.Join(",", fields.Select(f => f.CustomFieldId));
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
                if (!result.ContainsKey(v.RecordId))
                    result[v.RecordId] = new();
                result[v.RecordId][v.CustomFieldId] = v.ValueText;
            }
            return result;
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
                            new { Id = dossierId },
                            splitOn: "LocationId").FirstOrDefault();

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

            // ── nationality field (legacy hardcoded) ──
            var natFieldId = conn.ExecuteScalar<int?>(
                "SELECT CustomFieldId FROM CustomFields WHERE FieldKey = 'nationality' AND IsActive = 1");

            // ── all ShowInReports custom fields ──
            var reportFields = conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInReports = 1
                ORDER BY SortOrder, ArabicLabel").AsList();

            // Load all custom values for this page of records in one query
            Dictionary<int, Dictionary<int, string?>> allValues = new();
            if (reportFields.Count > 0 || natFieldId.HasValue)
            {
                var rawValues = conn.Query<RecordCustomFieldValue>($@"
                    SELECT RecordId, CustomFieldId, ValueText
                    FROM RecordCustomFieldValues
                    WHERE RecordId IN ({string.Join(",", recIds)})")
                    .AsList();

                foreach (var v in rawValues)
                {
                    if (!allValues.ContainsKey(v.RecordId))
                        allValues[v.RecordId] = new();
                    allValues[v.RecordId][v.CustomFieldId] = v.ValueText;
                }
            }

            var faceRecords = records.Select(r =>
            {
                allValues.TryGetValue(r.RecordId, out var vals);
                vals ??= new();

                string? nationality = natFieldId.HasValue && vals.TryGetValue(natFieldId.Value, out var n)
                    ? n : null;

                // Extra fields: only those in reportFields that aren't nationality
                var extras = new Dictionary<string, string?>();
                foreach (var cf in reportFields)
                {
                    // Skip if this IS the nationality field (it already has its own column)
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
        d.DossierNumber,
        d.HijriMonth,
        d.HijriYear,
        d.Status,
        COALESCE(
            l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber,
            'غير محدد'
        ) AS LocationDisplay,
        COUNT(r.RecordId) AS RecordCount
    FROM Dossiers d
    LEFT JOIN Locations l
        ON l.LocationId = d.CurrentLocationId
    LEFT JOIN Records r
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
    WHERE d.DeletedAt IS NULL
      AND d.HijriYear = @Year
      AND d.HijriMonth = @Month
    GROUP BY d.DossierId
    ORDER BY d.DossierNumber",
                new { Year = hijriYear, Month = hijriMonth }).AsList();

            var reportFields = LoadReportCustomFields(conn);
            var customValues = LoadCustomValuesForDossiers(conn, hijriMonth, null, hijriYear, reportFields);

            // في LoadMonthlyReport
            return new PeriodReportData
            {
                Title = "تقرير شهري",
                Period = $"شهر {hijriMonth} / {hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows,
                ReportCustomFields = reportFields,
                RecordCustomValues = customValues,
                FieldAggregates = BuildFieldAggregates(customValues, reportFields) // ← أضف هذا
            };
        }

        public PeriodReportData LoadYearlyReport(int hijriYear)
        {
            using var conn = _db.CreateConnection();

            var rows = conn.Query<PeriodReportRow>(@"
    SELECT
        d.DossierNumber,
        d.HijriMonth,
        d.HijriYear,
        d.Status,
        COALESCE(
            l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber,
            'غير محدد'
        ) AS LocationDisplay,
        COUNT(r.RecordId) AS RecordCount
    FROM Dossiers d
    LEFT JOIN Locations l
        ON l.LocationId = d.CurrentLocationId
    LEFT JOIN Records r
        ON r.DossierId = d.DossierId
       AND r.DeletedAt IS NULL
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
                FieldAggregates = BuildFieldAggregates(customValues, reportFields) // ← أضف هذا
            };
        }

        // ── Helpers for custom field loading ──────────────────────────────────

        private static List<CustomField> LoadReportCustomFields(
            Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            return conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInReports = 1
                ORDER BY SortOrder, ArabicLabel").AsList();
        }

        /// <summary>
        /// Loads custom field values aggregated at the dossier level for period reports.
        /// Returns RecordId → (CustomFieldId → ValueText).
        /// </summary>
        private static Dictionary<int, Dictionary<int, string?>> LoadCustomValuesForDossiers(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            int? hijriMonth, int? monthFilter, int? yearFilter,
            List<CustomField> fields)
        {
            if (fields.Count == 0) return new();

            // Build WHERE for dossiers
            var conditions = new List<string>();
            var p = new DynamicParameters();


            if (hijriMonth.HasValue && monthFilter.HasValue)
            {
                conditions.Add("d.HijriYear = @Year2");
                p.Add("Year2", monthFilter.Value);  // receives actual hijriMonth value
                conditions.Add("d.HijriMonth = @Month");
                p.Add("Month", hijriMonth.Value);   // receives actual hijriYear value
            }

            if (yearFilter.HasValue)
            {
                conditions.Add("d.HijriYear = @Year");
                p.Add("Year", yearFilter.Value);
            }
            if (hijriMonth.HasValue && monthFilter.HasValue)
            {
                conditions.Add("d.HijriYear = @Year2");
                conditions.Add("d.HijriMonth = @Month");
                p.Add("Year2", monthFilter.Value);
                p.Add("Month", hijriMonth.Value);
            }

            string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            var fieldIds = string.Join(",", fields.Select(f => f.CustomFieldId));
            var rawVals = conn.Query<RecordCustomFieldValue>($@"
                SELECT rcfv.RecordId, rcfv.CustomFieldId, rcfv.ValueText
                FROM RecordCustomFieldValues rcfv
                JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
                JOIN Dossiers d ON d.DossierId = r.DossierId
                {where}
                AND rcfv.CustomFieldId IN ({fieldIds})", p).AsList();

            var result = new Dictionary<int, Dictionary<int, string?>>();
            foreach (var v in rawVals)
            {
                if (!result.ContainsKey(v.RecordId))
                    result[v.RecordId] = new();
                result[v.RecordId][v.CustomFieldId] = v.ValueText;
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PDF GENERATION  (QuestPDF)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates the dossier face table PDF and saves it to <paramref name="outputPath"/>.
        /// Returns error string or null on success.
        /// </summary>
        public string? GenerateDossierFacePdf(DossierFaceData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                // Determine extra column labels from first record that has them
                var extraLabels = data.Records
                    .SelectMany(r => r.ExtraFields.Keys)
                    .Distinct()
                    .ToList();

                bool showNat = data.Records.Any(r => r.Nationality != null);

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Noto Kufi Arabic"));
                        page.ContentFromRightToLeft();

                        page.Header().Element(c => ComposeHeader(c, data));
                        page.Content().Element(c => ComposeTable(c, data, showNat, extraLabels));
                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9);
                            x.Span(" من ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9);
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
            catch (Exception ex)
            {
                return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}";
            }
        }

        private static void ComposeHeader(IContainer c, DossierFaceData data)
        {
            c.Column(col =>
            {
                col.Item().AlignCenter().Text($"دوسية رقم ({data.DossierNumber})")
                    .FontSize(16).Bold();
                col.Item().AlignCenter().Text(
                    $"شهر {data.HijriMonth} لعام {data.HijriYear}هـ  |  " +
                    $"عدد {data.ExpectedCount?.ToString() ?? "؟"} ملف سجين")
                    .FontSize(12).FontColor(Colors.Grey.Darken2);
                col.Item().AlignCenter().Text($"الموقع: {data.LocationDisplay}")
                    .FontSize(11).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingVertical(4).LineHorizontal(1)
                    .LineColor(Colors.Teal.Medium);
            });
        }

        private static void ComposeTable(IContainer c, DossierFaceData data,
            bool showNat, List<string> extraLabels)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(40);    // تسلسل
                    cols.RelativeColumn(3);     // اسم السجين
                    cols.ConstantColumn(110);   // رقم السجين
                    if (showNat)
                        cols.RelativeColumn(1.5f); // الجنسية
                    foreach (var _ in extraLabels)
                        cols.RelativeColumn(1.5f);  // extra custom fields
                });

                table.Header(h =>
                {
                    HeaderCell(h, "تسلسل");
                    HeaderCell(h, "اسم السجين");
                    HeaderCell(h, "رقم السجين");
                    if (showNat) HeaderCell(h, "الجنسية");
                    foreach (var lbl in extraLabels) HeaderCell(h, lbl);
                });

                foreach (var rec in data.Records)
                {
                    bool alt = rec.Sequence % 2 == 0;
                    var bg = alt ? Colors.Grey.Lighten4 : Colors.White;

                    DataCell(table, rec.Sequence.ToString(), bg, center: true);
                    DataCell(table, rec.PersonName, bg);
                    DataCell(table, rec.PrisonerNumber, bg, center: true);
                    if (showNat)
                        DataCell(table, rec.Nationality ?? "—", bg);
                    foreach (var lbl in extraLabels)
                    {
                        rec.ExtraFields.TryGetValue(lbl, out var val);
                        DataCell(table, val ?? "—", bg);
                    }
                }
            });
        }

        /// <summary>
        /// Generates a period (monthly/yearly) report PDF.
        /// </summary>
        public string? GeneratePeriodReportPdf(PeriodReportData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Noto Kufi Arabic"));
                        page.ContentFromRightToLeft();

                        page.Header().Column(col =>
                        {
                            col.Item().AlignCenter().Text(data.Title)
                                .FontSize(16).Bold();
                            col.Item().AlignCenter().Text(data.Period)
                                .FontSize(13).FontColor(Colors.Grey.Darken2);
                            col.Item().AlignCenter().Text(
                                $"إجمالي الدوسيات: {data.TotalDossiers}  |  إجمالي الملفات: {data.TotalRecords}")
                                .FontSize(11);
                            col.Item().PaddingVertical(4).LineHorizontal(1)
                                .LineColor(Colors.Teal.Medium);
                        });

                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(70);   // رقم الدوسية
                                cols.ConstantColumn(90);   // التاريخ الهجري
                                cols.ConstantColumn(60);   // عدد الملفات
                                cols.RelativeColumn(2);    // الموقع
                                cols.ConstantColumn(70);   // الحالة
                                // One column per custom report fiel
                            });

                            table.Header(h =>
                            {
                                HeaderCell(h, "رقم الدوسية");
                                HeaderCell(h, "الشهر / السنة");
                                HeaderCell(h, "عدد الملفات");
                                HeaderCell(h, "الموقع");
                                HeaderCell(h, "الحالة");

                            });

                            foreach (var row in data.Rows)
                            {
                                bool alt = data.Rows.IndexOf(row) % 2 == 0;
                                var bg = alt ? Colors.Grey.Lighten4 : Colors.White;

                                DataCell(table, row.DossierNumber.ToString(), bg, center: true);
                                DataCell(table, $"{row.HijriMonth}/{row.HijriYear}هـ", bg, center: true);
                                DataCell(table, row.RecordCount.ToString(), bg, center: true);
                                DataCell(table, row.LocationDisplay, bg);
                                DataCell(table, TranslateStatus(row.Status), bg, center: true);

                            }
                        });

                        // ── القسم الثاني: ملخص الحقول المخصصة ───────────────────────────────
                        if (data.FieldAggregates.Count > 0)
                        {
                            // فراغ بين الجدولين
                            // نضيف صفحة جديدة للملخص إذا كانت البيانات كبيرة
                            container.Page(page2 =>
                            {
                                page2.Size(PageSizes.A4);
                                page2.Margin(1.5f, Unit.Centimetre);
                                page2.DefaultTextStyle(t => t.FontSize(10).FontFamily("Noto Kufi Arabic"));
                                page2.ContentFromRightToLeft();

                                page2.Header().Column(col =>
                                {
                                    col.Item().AlignCenter()
                                        .Text($"ملخص الحقول المخصصة — {data.Period}")
                                        .FontSize(14).Bold();
                                    col.Item().PaddingVertical(4)
                                        .LineHorizontal(1).LineColor(Colors.Teal.Medium);
                                });

                                page2.Content().Column(col =>
                                {
                                    foreach (var (fieldLabel, values) in data.FieldAggregates)
                                    {
                                        int fieldTotal = values.Sum(v => v.Count);

                                        col.Item().PaddingBottom(4)
                                            .Text(fieldLabel)
                                            .Bold().FontSize(12).FontColor(Colors.Teal.Darken3);

                                        col.Item().Table(t =>
                                        {
                                            t.ColumnsDefinition(c =>
                                            {
                                                c.RelativeColumn(3);   // القيمة
                                                c.ConstantColumn(70);  // العدد
                                                c.ConstantColumn(80);  // النسبة
                                            });

                                            t.Header(h =>
                                            {
                                                HeaderCell(h, "القيمة");
                                                HeaderCell(h, "العدد");
                                                HeaderCell(h, "النسبة");
                                            });

                                            foreach (var (value, count) in values)
                                            {
                                                bool alt = values.IndexOf((value, count)) % 2 == 0;
                                                var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                                                double pct = fieldTotal > 0 ? count * 100.0 / fieldTotal : 0;

                                                DataCell(t, value, bg);
                                                DataCell(t, count.ToString(), bg, center: true);
                                                DataCell(t, $"{pct:F1}%", bg, center: true);
                                            }

                                            // صف الإجمالي
                                            DataCell(t, "الإجمالي", Colors.Teal.Lighten4);
                                            DataCell(t, fieldTotal.ToString(), Colors.Teal.Lighten4, center: true);
                                            DataCell(t, "100%", Colors.Teal.Lighten4, center: true);
                                        });

                                        col.Item().PaddingVertical(12); // مسافة بين الحقول
                                    }
                                });

                                page2.Footer().AlignCenter().Text(x =>
                                {
                                    x.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Medium);
                                    x.CurrentPageNumber().FontSize(9);
                                    x.Span(" من ").FontSize(9).FontColor(Colors.Grey.Medium);
                                    x.TotalPages().FontSize(9);
                                });
                            });
                        }

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9);
                            x.Span(" من ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9);
                        });
                    });
                }).GeneratePdf(outputPath);

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}";
            }
        }

        public DataQualityReportData LoadDataQualityReport()
        {
            using var conn = _db.CreateConnection();

            int totalDossiers = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Dossiers WHERE DeletedAt IS NULL");
            int totalRecords = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records WHERE DeletedAt IS NULL");

            // ── 1. Unresolved import warnings grouped by type ─────────────────
            var warningGroups = conn.Query<DataQualityWarningGroup>(@"
                SELECT
                    iw.WarningType,
                    COUNT(*)           AS Count,
                    COALESCE(ib.FileName, '') AS BatchFileName
                FROM ImportWarnings iw
                LEFT JOIN ImportBatches ib
                    ON ib.ImportBatchId = iw.ImportBatchId
                WHERE iw.IsResolved = 0
                GROUP BY iw.WarningType
                ORDER BY Count DESC").AsList();

            int unresolvedTotal = warningGroups.Sum(g => g.Count);

            // ── 2. Dossiers where actual count ≠ expected (and expected is set) ─
            var mismatchDossiers = conn.Query<DataQualityMismatchRow>(@"
                SELECT
                    d.DossierNumber,
                    d.HijriMonth,
                    d.HijriYear,
                    d.ExpectedFileCount AS ExpectedCount,
                    COUNT(r.RecordId)   AS ActualCount
                FROM Dossiers d
                LEFT JOIN Records r
                    ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.DeletedAt IS NULL
                AND   d.ExpectedFileCount IS NOT NULL
                GROUP BY d.DossierId
                HAVING COUNT(r.RecordId) != d.ExpectedFileCount
                ORDER BY ABS(COUNT(r.RecordId) - d.ExpectedFileCount) DESC
                LIMIT 100").AsList();

            // ── 3. Active custom fields – fill rate per field ──────────────────
            var fields = conn.Query<(int Id, string Label, bool Required)>(@"
                SELECT CustomFieldId, ArabicLabel, IsRequired
                FROM CustomFields
                WHERE IsActive = 1
                ORDER BY SortOrder, ArabicLabel").AsList();

            var fieldRows = new List<DataQualityFieldRow>();
            foreach (var (id, label, required) in fields)
            {
                // Match StatisticsService: only count records created ON OR AFTER
                // the field was created — older records were never expected to have it.
                string? fieldCreatedAt = conn.ExecuteScalar<string?>(
                    "SELECT CreatedAt FROM CustomFields WHERE CustomFieldId = @Id",
                    new { Id = id });

                int relevantTotal;
                if (string.IsNullOrEmpty(fieldCreatedAt))
                {
                    relevantTotal = totalRecords;
                }
                else
                {
                    relevantTotal = conn.ExecuteScalar<int>(@"
                        SELECT COUNT(*)
                        FROM Records
                        WHERE DeletedAt IS NULL
                        AND   CreatedAt >= @FieldCreatedAt",
                                    new { FieldCreatedAt = fieldCreatedAt });
                            }

                            int filled = conn.ExecuteScalar<int>(@"
                    SELECT COUNT(DISTINCT rcfv.RecordId)
                    FROM RecordCustomFieldValues rcfv
                    JOIN Records r ON r.RecordId = rcfv.RecordId AND r.DeletedAt IS NULL
                    WHERE rcfv.CustomFieldId = @Id
                    AND   rcfv.ValueText IS NOT NULL
                    AND   rcfv.ValueText != ''",
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
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalDossiers = totalDossiers,
                TotalRecords = totalRecords,
                DossiersWithMismatch = mismatchDossiers.Count,
                UnresolvedWarningsTotal = unresolvedTotal,
                WarningGroups = warningGroups,
                MismatchDossiers = mismatchDossiers,
                MissingFieldRows = fieldRows
            };
        }

        /// <summary>Generates the data quality PDF and saves to outputPath.</summary>
        public string? GenerateDataQualityReportPdf(DataQualityReportData data, string outputPath)
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Noto Kufi Arabic"));
                        page.ContentFromRightToLeft();

                        page.Header().Column(col =>
                        {
                            col.Item().AlignCenter().Text("تقرير جودة البيانات والأخطاء")
                                .FontSize(16).Bold();
                            col.Item().AlignCenter()
                                .Text($"تاريخ التقرير: {data.GeneratedAt}")
                                .FontSize(11).FontColor(Colors.Grey.Darken2);
                            col.Item().AlignCenter()
                                .Text($"إجمالي الدوسيات: {data.TotalDossiers}  |  إجمالي السجلات: {data.TotalRecords}")
                                .FontSize(11);

                            // Summary badges row
                            col.Item().Padding(6).Row(row =>
                            {
                                row.RelativeItem().Background(
                                    data.UnresolvedWarningsTotal > 0
                                        ? Colors.Red.Lighten4 : Colors.Green.Lighten4)
                                    .Padding(8).AlignCenter()
                                    .Text($"تحذيرات غير محلولة: {data.UnresolvedWarningsTotal}")
                                    .Bold().FontSize(11);

                                row.ConstantItem(12);

                                row.RelativeItem().Background(
                                    data.DossiersWithMismatch > 0
                                        ? Colors.Orange.Lighten4 : Colors.Green.Lighten4)
                                    .Padding(8).AlignCenter()
                                    .Text($"دوسيات بتعداد غير مطابق: {data.DossiersWithMismatch}")
                                    .Bold().FontSize(11);
                            });

                            col.Item().PaddingVertical(4)
                                .LineHorizontal(1).LineColor(Colors.Teal.Medium);
                        });

                        page.Content().Column(col =>
                        {
                            // ── Section 1: Unresolved warnings ──────────────
                            if (data.WarningGroups.Count > 0)
                            {
                                col.Item().PaddingBottom(6)
                                    .Text("أولاً: التحذيرات غير المحلولة من الاستيراد")
                                    .Bold().FontSize(12).FontColor(Colors.Red.Darken3);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3);
                                        c.ConstantColumn(70);
                                        c.RelativeColumn(2);
                                    });

                                    t.Header(h =>
                                    {
                                        HeaderCell(h, "نوع التحذير");
                                        HeaderCell(h, "العدد");
                                        HeaderCell(h, "الدفعة");
                                    });

                                    foreach (var g in data.WarningGroups)
                                    {
                                        bool alt = data.WarningGroups.IndexOf(g) % 2 == 0;
                                        var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                                        DataCell(t, g.WarningType, bg);
                                        DataCell(t, g.Count.ToString(), bg, center: true);
                                        DataCell(t, g.BatchFileName, bg);
                                    }
                                });

                                col.Item().PaddingVertical(10);
                            }

                            // ── Section 2: Count mismatches ──────────────────
                            if (data.MismatchDossiers.Count > 0)
                            {
                                col.Item().PaddingBottom(6)
                                    .Text("ثانياً: الدوسيات ذات التعداد غير المطابق")
                                    .Bold().FontSize(12).FontColor(Colors.Orange.Darken3);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(90);
                                        c.ConstantColumn(100);
                                        c.ConstantColumn(80);
                                        c.ConstantColumn(80);
                                        c.ConstantColumn(70);
                                    });

                                    t.Header(h =>
                                    {
                                        HeaderCell(h, "رقم الدوسية");
                                        HeaderCell(h, "التاريخ الهجري");
                                        HeaderCell(h, "المتوقع");
                                        HeaderCell(h, "الفعلي");
                                        HeaderCell(h, "الفارق");
                                    });

                                    foreach (var r in data.MismatchDossiers)
                                    {
                                        bool alt = data.MismatchDossiers.IndexOf(r) % 2 == 0;
                                        var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                                        DataCell(t, r.DossierNumber.ToString(), bg, center: true);
                                        DataCell(t, r.HijriDisplay, bg, center: true);
                                        DataCell(t, r.ExpectedCount.ToString(), bg, center: true);
                                        DataCell(t, r.ActualCount.ToString(), bg, center: true);
                                        DataCell(t, r.DifferenceDisplay, bg, center: true);
                                    }
                                });

                                col.Item().PaddingVertical(10);
                            }

                            // ── Section 3: Field fill rates ──────────────────
                            if (data.MissingFieldRows.Count > 0)
                            {
                                col.Item().PaddingBottom(6)
                                    .Text("ثالثاً: نسبة تعبئة الحقول المخصصة")
                                    .Bold().FontSize(12).FontColor(Colors.Teal.Darken3);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2);
                                        c.ConstantColumn(60);
                                        c.ConstantColumn(80);
                                        c.ConstantColumn(80);
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

                                    foreach (var f in data.MissingFieldRows)
                                    {
                                        bool alt = data.MissingFieldRows.IndexOf(f) % 2 == 0;
                                        var bg = f.IsRequired && f.MissingCount > 0
                                            ? Colors.Red.Lighten5
                                            : (alt ? Colors.Grey.Lighten4 : Colors.White);

                                        DataCell(t, f.FieldLabel, bg);
                                        DataCell(t, f.IsRequired ? "✓" : "—", bg, center: true);
                                        DataCell(t, f.FilledCount.ToString(), bg, center: true);
                                        DataCell(t, f.MissingCount.ToString(), bg, center: true);
                                        DataCell(t, f.FillRate, bg, center: true);
                                    }
                                });
                            }

                            if (data.WarningGroups.Count == 0
                                && data.MismatchDossiers.Count == 0
                                && data.MissingFieldRows.All(f => f.MissingCount == 0))
                            {
                                col.Item().PaddingTop(20).AlignCenter()
                                    .Text("✅ لا توجد مشكلات في جودة البيانات.")
                                    .FontSize(14).FontColor(Colors.Green.Darken2).Bold();
                            }
                        });

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9);
                            x.Span(" من ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9);
                        });
                    });
                }).GeneratePdf(outputPath);

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء إنشاء تقرير الجودة: {ex.Message}";
            }
        }



        public string? PrintDataQualityReportDirect(DataQualityReportData data)
        {
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"data_quality_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            var genErr = GenerateDataQualityReportPdf(data, tempPath);
            if (genErr != null) return genErr;
            return OpenPdfForDirectPrint(tempPath, "تقرير جودة البيانات");
        }



        /// <summary>
        /// Generates an audit-log PDF from a pre-loaded list of rows.
        /// Returns null on success, error string on failure.
        /// </summary>
        public string? GenerateAuditLogPdf(
            List<AuditLogRow> rows,
            string outputPath,
            string periodLabel = "")
        {
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());   // landscape — more columns
                        page.Margin(1.2f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(9).FontFamily("Noto Kufi Arabic"));
                        page.ContentFromRightToLeft();

                        // ── Header ───────────────────────────────────────────────────
                        page.Header().Column(col =>
                        {
                            col.Item().AlignCenter()
                                .Text("سجل الاحداث — أرشيف الملفات")
                                .FontSize(16).Bold();

                            if (!string.IsNullOrWhiteSpace(periodLabel))
                                col.Item().AlignCenter()
                                    .Text(periodLabel)
                                    .FontSize(11).FontColor(Colors.Grey.Darken2);

                            col.Item().AlignCenter()
                                .Text($"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}  |  " +
                                      $"إجمالي السجلات: {rows.Count:N0}")
                                .FontSize(10).FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingVertical(4)
                                .LineHorizontal(1).LineColor(Colors.Teal.Medium);
                        });

                        // ── Table ────────────────────────────────────────────────────
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(118);   // التاريخ والوقت
                                cols.ConstantColumn(100);   // المستخدم
                                cols.ConstantColumn(120);   // نوع الحدث
                                cols.ConstantColumn(80);    // الكيان
                                cols.RelativeColumn();      // الوصف
                            });

                            // Header row
                            table.Header(h =>
                            {
                                HeaderCell(h, "التاريخ والوقت");
                                HeaderCell(h, "المستخدم");
                                HeaderCell(h, "نوع الحدث");
                                HeaderCell(h, "الكيان");
                                HeaderCell(h, "الوصف");
                            });

                            // Data rows
                            foreach (var row in rows)
                            {
                                // Colour-code certain action types
                                string bg = row.ActionType switch
                                {
                                    "LoginFailure" => Colors.Red.Lighten5,
                                    "RecordDeleted" or "DossierDeleted" => Colors.Orange.Lighten5,
                                    "RestoreCompleted" => Colors.Blue.Lighten5,
                                    _ => rows.IndexOf(row) % 2 == 0
                                             ? Colors.Grey.Lighten4
                                             : Colors.White
                                };

                                DataCell(table, row.CreatedAt ?? "", bg);
                                DataCell(table, row.UserFullName ?? "(نظام)", bg);
                                DataCell(table, row.ActionTypeArabic, bg);
                                DataCell(table, row.EntityDisplay, bg);
                                DataCell(table, row.Description, bg);
                            }
                        });

                        // ── Footer ───────────────────────────────────────────────────
                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("صفحة ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(8);
                            x.Span(" من ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(8);
                        });
                    });
                }).GeneratePdf(outputPath);

                // Audit the print action itself
                using var conn = _db.CreateConnection();
                conn.Execute(@"
            INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
            VALUES (@UserId, 'ReportPrinted', @Desc, @Now)",
                    new
                    {
                        UserId = UserSession.CurrentUser?.UserId,
                        Desc = $"تصدير سجل الاحداث إلى PDF — {rows.Count} سجل",
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}";
            }
        }

        /// <summary>
        /// Generates the audit-log PDF to a temp file then opens it via the
        /// system default PDF handler so the user can print directly.
        /// </summary>
        public string? PrintAuditLogDirect(List<AuditLogRow> rows, string periodLabel = "")
        {
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"audit_log_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            var err = GenerateAuditLogPdf(rows, tempPath, periodLabel);
            if (err != null) return err;
            return OpenPdfForDirectPrint(tempPath, "سجل الاحداث");
        }
        // ─────────────────────────────────────────────────────────────────────
        // DIRECT WPF PRINT  (PrintDialog → XPS)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a WPF PrintDialog for direct printing without saving a file.
        /// Generates the PDF to a temp file then prints it via PrintVisual.
        /// Returns error string or null on success.
        /// </summary>
        public string? PrintDossierFaceDirect(DossierFaceData data)
        {
            // Generate to temp
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dossier_face_print_{data.DossierNumber}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            var genErr = GenerateDossierFacePdf(data, tempPath);
            if (genErr != null) return genErr;

            return OpenPdfForDirectPrint(tempPath,
                $"طباعة دوسية رقم {data.DossierNumber}");
        }

        /// <summary>
        /// Opens a WPF PrintDialog for a period report PDF.
        /// </summary>
        public string? PrintPeriodReportDirect(PeriodReportData data)
        {
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"period_report_print_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            var genErr = GeneratePeriodReportPdf(data, tempPath);
            if (genErr != null) return genErr;

            return OpenPdfForDirectPrint(tempPath, data.Title);
        }

        /// <summary>
        /// Opens the system's default PDF handler with the Shell verb "print"
        /// so the user gets a direct-print dialog without having to manually
        /// press Print in the viewer.  Falls back to "open" if "print" is not
        /// supported by the installed viewer.
        /// </summary>
        private static string? OpenPdfForDirectPrint(string pdfPath, string documentTitle)
        {
            try
            {
                // Try the "print" ShellExecute verb first — most PDF viewers honour it
                var psi = new System.Diagnostics.ProcessStartInfo(pdfPath)
                {
                    Verb = "print",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                try
                {
                    System.Diagnostics.Process.Start(psi);
                    return null;
                }
                catch
                {
                    // "print" verb not available → fall back to open
                }

                // Fallback: open the PDF (user can print from viewer)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(pdfPath)
                    { UseShellExecute = true });

                return null;
            }
            catch (Exception ex)
            {
                return $"خطأ أثناء إرسال الطباعة: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static void HeaderCell(TableCellDescriptor h, string text)
        {
            h.Cell().Background(Colors.Teal.Medium).Padding(5)
                .AlignCenter()
                .Text(text).Bold().FontColor(Colors.White).FontSize(10);
        }

        private static void DataCell(TableDescriptor table, string text,
            string bg, bool center = false)
        {
            var cell = table.Cell().Background(bg).Padding(4);
            if (center) cell.AlignCenter().Text(text).FontSize(10);
            else cell.Text(text).FontSize(10);
        }

        private static string TranslateStatus(string s) => s switch
        {
            "Open" => "مفتوح",
            "Complete" => "مكتمل",
            "NeedsReview" => "مراجعة",
            "Archived" => "مؤرشف",
            _ => s
        };
    }
}