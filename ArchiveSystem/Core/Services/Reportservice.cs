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
    }

    // ─── DTO for monthly/weekly report ─────────────────────────────────────────
    public class PeriodReportData
    {
        public string Title { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public List<PeriodReportRow> Rows { get; set; } = new();
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

        public DossierFaceData? LoadDossierFaceData(int dossierId)
        {
            using var conn = _db.CreateConnection();

            var dossier = conn.QuerySingleOrDefault<Dossier, Location, Dossier>(@"
                SELECT d.*, l.*
                FROM Dossiers d
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE d.DossierId = @Id",
                (d, l) => { d.CurrentLocation = l; return d; },
                new { Id = dossierId },
                splitOn: "LocationId");

            if (dossier == null) return null;

            // nationality custom field id
            var natFieldId = conn.ExecuteScalar<int?>(
                "SELECT CustomFieldId FROM CustomFields WHERE FieldKey = 'nationality' AND IsActive = 1");

            var records = conn.Query<Record>(@"
                SELECT * FROM Records
                WHERE DossierId = @DossierId AND DeletedAt IS NULL
                ORDER BY SequenceNumber",
                new { DossierId = dossierId }).AsList();

            List<DossierFaceRecord>? faceRecords = null;

            if (natFieldId.HasValue && records.Count > 0)
            {
                var recIds = records.Select(r => r.RecordId).ToList();
                var natValues = conn.Query<(int RecordId, string? Val)>($@"
                    SELECT RecordId, ValueText FROM RecordCustomFieldValues
                    WHERE CustomFieldId = @FieldId
                    AND RecordId IN ({string.Join(",", recIds)})",
                    new { FieldId = natFieldId.Value })
                    .ToDictionary(x => x.RecordId, x => x.Val);

                faceRecords = records.Select(r => new DossierFaceRecord
                {
                    Sequence = r.SequenceNumber,
                    PersonName = r.PersonName,
                    PrisonerNumber = r.PrisonerNumber,
                    Nationality = natValues.TryGetValue(r.RecordId, out var n) ? n : null
                }).ToList();
            }
            else
            {
                faceRecords = records.Select(r => new DossierFaceRecord
                {
                    Sequence = r.SequenceNumber,
                    PersonName = r.PersonName,
                    PrisonerNumber = r.PrisonerNumber
                }).ToList();
            }

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

            return new PeriodReportData
            {
                Title = "تقرير شهري",
                Period = $"شهر {hijriMonth} / {hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows
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

            return new PeriodReportData
            {
                Title = "تقرير سنوي",
                Period = $"{hijriYear}هـ",
                TotalDossiers = rows.Count,
                TotalRecords = rows.Sum(r => r.RecordCount),
                Rows = rows
            };
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

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Arial"));
                        page.ContentFromRightToLeft();

                        page.Header().Element(ComposeHeader);
                        page.Content().Element(ComposeTable);
                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(9);
                            x.Span(" من ").FontSize(9).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(9);
                        });

                        void ComposeHeader(IContainer c)
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

                        void ComposeTable(IContainer c)
                        {
                            bool showNat = data.Records.Any(r => r.Nationality != null);
                            int colCount = showNat ? 4 : 3;

                            c.Table(table =>
                            {
                                // columns
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(40);   // تسلسل
                                    cols.RelativeColumn(3);    // اسم السجين
                                    cols.ConstantColumn(110);  // رقم السجين
                                    if (showNat)
                                        cols.RelativeColumn(1.5f); // الجنسية
                                });

                                // header row
                                table.Header(h =>
                                {
                                    HeaderCell(h, "تسلسل");
                                    HeaderCell(h, "اسم السجين");
                                    HeaderCell(h, "رقم السجين");
                                    if (showNat) HeaderCell(h, "الجنسية");
                                });

                                // data rows
                                foreach (var rec in data.Records)
                                {
                                    bool alt = rec.Sequence % 2 == 0;
                                    var bg = alt ? Colors.Grey.Lighten4 : Colors.White;

                                    DataCell(table, rec.Sequence.ToString(), bg, center: true);
                                    DataCell(table, rec.PersonName, bg);
                                    DataCell(table, rec.PrisonerNumber, bg, center: true);
                                    if (showNat)
                                        DataCell(table, rec.Nationality ?? "—", bg);
                                }
                            });
                        }
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