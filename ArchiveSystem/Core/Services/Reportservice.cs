using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
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
            COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
            COUNT(r.RecordId) AS RecordCount
        FROM Dossiers d
        LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
        LEFT JOIN Records   r ON r.DossierId  = d.DossierId AND r.DeletedAt IS NULL
        WHERE r.CreatedAt >= @From AND r.CreatedAt < @To
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
                RecordCustomValues = customValues
            };
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
                    COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
                    COUNT(r.RecordId) AS RecordCount
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                LEFT JOIN Records   r ON r.DossierId  = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.HijriYear = @Year AND d.HijriMonth = @Month
                GROUP BY d.DossierId
                ORDER BY d.DossierNumber",
                new { Year = hijriYear, Month = hijriMonth }).AsList();

            var reportFields = LoadReportCustomFields(conn);
            var customValues = LoadCustomValuesForDossiers(conn, hijriYear, hijriMonth, null, reportFields);

            return new PeriodReportData
            {
                Title = "تقرير شهري",
                Period = $"شهر {hijriMonth} / {hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows,
                ReportCustomFields = reportFields,
                RecordCustomValues = customValues
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
                    COALESCE(l.HallwayNumber || '-' || l.CabinetNumber || '-' || l.ShelfNumber, 'غير محدد') AS LocationDisplay,
                    COUNT(r.RecordId) AS RecordCount
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                LEFT JOIN Records   r ON r.DossierId  = d.DossierId AND r.DeletedAt IS NULL
                WHERE d.HijriYear = @Year
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
                RecordCustomValues = customValues
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
                        page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Arial"));
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
                        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));
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
                                // One column per custom report field
                                foreach (var _ in data.ReportCustomFields)
                                    cols.RelativeColumn(1.5f);
                            });

                            table.Header(h =>
                            {
                                HeaderCell(h, "رقم الدوسية");
                                HeaderCell(h, "الشهر / السنة");
                                HeaderCell(h, "عدد الملفات");
                                HeaderCell(h, "الموقع");
                                HeaderCell(h, "الحالة");
                                foreach (var cf in data.ReportCustomFields)
                                    HeaderCell(h, cf.ArabicLabel);
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

                                // For period reports, custom field values are per-record, not per-dossier.
                                // We show a summary count: e.g. how many non-empty values in this dossier.
                                foreach (var cf in data.ReportCustomFields)
                                {
                                    // Count distinct non-null values for this field within this dossier's records
                                    var distinctVals = data.RecordCustomValues.Values
                                        .Where(rv => rv.ContainsKey(cf.CustomFieldId)
                                                     && !string.IsNullOrWhiteSpace(rv[cf.CustomFieldId]))
                                        .Select(rv => rv[cf.CustomFieldId])
                                        .Distinct()
                                        .Take(3)
                                        .ToList();

                                    string display = distinctVals.Count > 0
                                        ? string.Join(" / ", distinctVals)
                                        : "—";

                                    DataCell(table, display, bg);
                                }
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
                return $"خطأ أثناء إنشاء ملف PDF: {ex.Message}";
            }
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