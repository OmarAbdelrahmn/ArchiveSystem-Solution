using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    public partial class ReportsPage : Page
    {
        private readonly ReportService _reportService;
        private readonly DossierService _dossierService;

        // Debounce timer for batch range preview
        private System.Windows.Threading.DispatcherTimer? _batchDebounce;

        public ReportsPage()
        {
            InitializeComponent();
            _reportService = new ReportService(App.Database);
            _dossierService = new DossierService(App.Database);
            Loaded += (s, e) => { ApplyPermissions(); LoadDataQualitySummary(); };
        }

        private void ApplyPermissions()
        {
            // Dossier face section — needs PrintDossierFace
            PermissionHelper.Apply(DossierFacePreviewBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(DossierFaceSaveBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(DossierFacePrintBtn, Permissions.PrintDossierFace, hideInstead: true);

            // Batch dossier face section — needs PrintDossierFace
            PermissionHelper.Apply(BatchPreviewBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(BatchSaveBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(BatchPrintBtn, Permissions.PrintDossierFace, hideInstead: true);

            // Monthly/Yearly reports — needs PrintReports
            PermissionHelper.Apply(MonthlyPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(MonthlySaveBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(MonthlyPrintBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(YearlyPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(YearlySaveBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(YearlyPrintBtn, Permissions.PrintReports, hideInstead: true);
            // Weekly reports — needs PrintReports
            PermissionHelper.Apply(WeeklyPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(WeeklySaveBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(WeeklyPrintBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(DataQualityPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(DataQualitySaveBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(DataQualityPrintBtn, Permissions.PrintReports, hideInstead: true);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DOSSIER FACE
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewDossierFace_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDossierFaceOrError();
            if (data == null) return;

            string path = TempPdfPath($"dossier_face_{data.DossierNumber}");
            var err = _reportService.GenerateDossierFacePdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveDossierFacePdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDossierFaceOrError();
            if (data == null) return;

            string? path = PickSavePath($"dossier_{data.DossierNumber}_{data.HijriMonth}_{data.HijriYear}");
            if (path == null) return;

            var err = _reportService.GenerateDossierFacePdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
        }

        private void PrintDossierFaceDirect_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDossierFaceOrError();
            if (data == null) return;

            var err = _reportService.PrintDossierFaceDirect(data);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال الملف إلى الطابعة.");
        }

        private DossierFaceData? LoadDossierFaceOrError()
        {
            HideMsg();
            if (!int.TryParse(DossierFaceNumberBox.Text, out int num))
            {
                ShowError("يرجى إدخال رقم دوسية صحيح.");
                return null;
            }

            var dossier = _dossierService.GetDossierByNumber(num);
            if (dossier == null)
            {
                ShowError($"الدوسية رقم {num} غير موجودة.");
                return null;
            }

            var data = _reportService.LoadDossierFaceData(dossier.DossierId);
            if (data == null)
            {
                ShowError("تعذر تحميل بيانات الدوسية.");
                return null;
            }

            if (data.Records.Count == 0)
            {
                ShowError($"الدوسية رقم {num} لا تحتوي على أي سجلات.");
                return null;
            }

            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BATCH DOSSIER FACE PRINT  (from number → to number)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fires whenever the user changes FromBox or ToBox.
        /// Debounces 400 ms then updates the info badge showing how many
        /// dossiers / records fall in the range.
        /// </summary>
        private void BatchRange_Changed(object sender, TextChangedEventArgs e)
        {
            BatchInfoBadge.Visibility = Visibility.Collapsed;
            BatchWarningBorder.Visibility = Visibility.Collapsed;

            _batchDebounce?.Stop();
            _batchDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _batchDebounce.Tick += (_, _) =>
            {
                _batchDebounce.Stop();
                RefreshBatchInfoBadge();
            };
            _batchDebounce.Start();
        }

        private void RefreshBatchInfoBadge()
        {
            if (!int.TryParse(BatchFromBox.Text, out int from) ||
                !int.TryParse(BatchToBox.Text, out int to) || from <= 0 || to <= 0)
                return;

            if (from > to)
            {
                BatchWarningText.Text = "رقم البداية يجب أن يكون أصغر من أو يساوي رقم النهاية.";
                BatchWarningBorder.Visibility = Visibility.Visible;
                return;
            }

            // Count how many dossiers exist in range
            var infos = _reportService.GetBatchDossierRangeInfo(from, to);

            if (infos.DossierCount == 0)
            {
                BatchWarningText.Text = $"لا توجد دوسيات في النطاق من {from} إلى {to}.";
                BatchWarningBorder.Visibility = Visibility.Visible;
                return;
            }

            BatchCountText.Text = $"{infos.DossierCount} دوسية";
            BatchRecordsText.Text = $"{infos.RecordCount} سجل إجمالاً";
            BatchInfoBadge.Visibility = Visibility.Visible;

            // Warn if range is very large (>50 dossiers) — PDF may be big
            if (infos.DossierCount > 50)
            {
                BatchWarningText.Text = $"النطاق يحتوي على {infos.DossierCount} دوسية — قد يستغرق إنشاء PDF بعض الوقت.";
                BatchWarningBorder.Visibility = Visibility.Visible;
            }
            else
            {
                BatchWarningBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void PreviewBatchDossierFace_Click(object sender, RoutedEventArgs e)
        {
            string? path = GenerateBatchPdfOrError(out string defaultName);
            if (path == null) return;
            OpenFile(path);
        }

        private void SaveBatchDossierFacePdf_Click(object sender, RoutedEventArgs e)
        {
            HideMsg();
            if (!ParseBatchRange(out int from, out int to)) return;

            string? savePath = PickSavePath($"batch_dossiers_{from}_to_{to}");
            if (savePath == null) return;

            SetBatchBusy(true, $"جاري إنشاء PDF للدوسيات من {from} إلى {to}...");
            try
            {
                var err = _reportService.GenerateBatchDossierFacePdf(from, to, savePath);
                if (err != null) { ShowError(err); return; }
                ShowSuccess($"✅ تم حفظ الملف: {savePath}");
                OpenFile(savePath);
            }
            finally
            {
                SetBatchBusy(false);
            }
        }

        private void PrintBatchDossierFaceDirect_Click(object sender, RoutedEventArgs e)
        {
            string? path = GenerateBatchPdfOrError(out _);
            if (path == null) return;

            var err = OpenPdfForPrint(path);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال ملف PDF المجمَّع إلى الطابعة.");
        }

        /// <summary>
        /// Shared helper: validates inputs, builds a temp PDF, returns its path.
        /// Returns null and shows an error message if anything fails.
        /// </summary>
        private string? GenerateBatchPdfOrError(out string defaultName)
        {
            defaultName = string.Empty;
            HideMsg();

            if (!ParseBatchRange(out int from, out int to)) return null;

            defaultName = $"batch_dossiers_{from}_to_{to}";
            string tempPath = TempPdfPath(defaultName);

            SetBatchBusy(true, $"جاري إنشاء PDF للدوسيات من {from} إلى {to}...");
            try
            {
                var err = _reportService.GenerateBatchDossierFacePdf(from, to, tempPath);
                if (err != null) { ShowError(err); return null; }
                return tempPath;
            }
            finally
            {
                SetBatchBusy(false);
            }
        }

        /// <summary>Parses BatchFromBox / BatchToBox and shows errors if invalid.</summary>
        private bool ParseBatchRange(out int from, out int to)
        {
            from = to = 0;

            if (!int.TryParse(BatchFromBox.Text, out from) || from <= 0)
            {
                ShowError("يرجى إدخال رقم دوسية البداية بشكل صحيح.");
                return false;
            }
            if (!int.TryParse(BatchToBox.Text, out to) || to <= 0)
            {
                ShowError("يرجى إدخال رقم دوسية النهاية بشكل صحيح.");
                return false;
            }
            if (from > to)
            {
                ShowError("رقم البداية يجب أن يكون أصغر من أو يساوي رقم النهاية.");
                return false;
            }
            if (to - from > 499)
            {
                ShowError("لا يمكن طباعة أكثر من 500 دوسية في مرة واحدة. يرجى تضييق النطاق.");
                return false;
            }

            return true;
        }

        private void SetBatchBusy(bool busy, string? message = null)
        {
            BatchBusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (message != null) BatchProgressText.Text = message;
            BatchPreviewBtn.IsEnabled = !busy;
            BatchSaveBtn.IsEnabled = !busy;
            BatchPrintBtn.IsEnabled = !busy;
        }

        private static string? OpenPdfForPrint(string pdfPath)
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
        // DATA QUALITY SUMMARY
        // ─────────────────────────────────────────────────────────────────────

        private void LoadDataQualitySummary()
        {
            try
            {
                var data = _reportService.LoadDataQualityReport();

                DQWarningsText.Text = $"تحذيرات غير محلولة: {data.UnresolvedWarningsTotal}";
                DQMismatchText.Text = $"دوسيات غير مطابقة: {data.DossiersWithMismatch}";
                DQRecordsText.Text = $"إجمالي السجلات: {data.TotalRecords:N0}";

                DQWarningsBadge.Background = new SolidColorBrush(
                    data.UnresolvedWarningsTotal > 0
                        ? (Color)ColorConverter.ConvertFromString("#FFEBEE")
                        : (Color)ColorConverter.ConvertFromString("#E8F5E9"));
                DQWarningsText.Foreground = new SolidColorBrush(
                    data.UnresolvedWarningsTotal > 0
                        ? (Color)ColorConverter.ConvertFromString("#C62828")
                        : (Color)ColorConverter.ConvertFromString("#2E7D32"));

                DQMismatchBadge.Background = new SolidColorBrush(
                    data.DossiersWithMismatch > 0
                        ? (Color)ColorConverter.ConvertFromString("#FFF8E1")
                        : (Color)ColorConverter.ConvertFromString("#E8F5E9"));
                DQMismatchText.Foreground = new SolidColorBrush(
                    data.DossiersWithMismatch > 0
                        ? (Color)ColorConverter.ConvertFromString("#E65100")
                        : (Color)ColorConverter.ConvertFromString("#2E7D32"));

                DataQualitySummaryBorder.Visibility = Visibility.Visible;
            }
            catch { /* non-critical – don't block page load */ }
        }

        private void PreviewDataQuality_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDataQualityOrError();
            if (data == null) return;

            string path = TempPdfPath("data_quality");
            var err = _reportService.GenerateDataQualityReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveDataQualityPdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDataQualityOrError();
            if (data == null) return;

            string? path = PickSavePath($"data_quality_{DateTime.Now:yyyyMMdd}");
            if (path == null) return;

            var err = _reportService.GenerateDataQualityReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
        }

        private void PrintDataQualityDirect_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDataQualityOrError();
            if (data == null) return;

            var err = _reportService.PrintDataQualityReportDirect(data);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال تقرير جودة البيانات إلى الطابعة.");
        }

        private DataQualityReportData? LoadDataQualityOrError()
        {
            HideMsg();
            try
            {
                return _reportService.LoadDataQualityReport();
            }
            catch (Exception ex)
            {
                ShowError($"خطأ أثناء تحميل بيانات الجودة: {ex.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // WEEKLY REPORT
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewWeekly_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadWeeklyOrError();
            if (data == null) return;

            string path = TempPdfPath($"weekly_{WeeklyFromBox.Text}_{WeeklyToBox.Text}");
            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveWeeklyPdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadWeeklyOrError();
            if (data == null) return;

            string? path = PickSavePath($"weekly_{WeeklyFromBox.Text}_{WeeklyToBox.Text}");
            if (path == null) return;

            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
        }

        private void PrintWeeklyDirect_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadWeeklyOrError();
            if (data == null) return;

            var err = _reportService.PrintPeriodReportDirect(data);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال التقرير الأسبوعي إلى الطابعة.");
        }

        private PeriodReportData? LoadWeeklyOrError()
        {
            HideMsg();

            string fromStr = WeeklyFromBox.Text.Trim();
            string toStr = WeeklyToBox.Text.Trim();

            if (!DateTime.TryParse(fromStr, out var fromDate))
            { ShowError("يرجى إدخال تاريخ البداية بصيغة صحيحة (yyyy-MM-dd)."); return null; }

            if (!DateTime.TryParse(toStr, out var toDate))
            { ShowError("يرجى إدخال تاريخ النهاية بصيغة صحيحة (yyyy-MM-dd)."); return null; }

            if (toDate < fromDate)
            { ShowError("تاريخ النهاية يجب أن يكون بعد تاريخ البداية."); return null; }

            if ((toDate - fromDate).TotalDays > 31)
            { ShowError("النطاق الزمني لا يجب أن يتجاوز 31 يوماً للتقرير الأسبوعي."); return null; }

            string fromIso = fromDate.ToString("yyyy-MM-dd");
            string toIso = toDate.AddDays(1).ToString("yyyy-MM-dd");

            var data = _reportService.LoadWeeklyReport(fromIso, toIso);

            if (data.TotalDossiers == 0)
            { ShowError($"لا توجد سجلات مُضافة بين {fromStr} و{toStr}."); return null; }

            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MONTHLY REPORT
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewMonthly_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadMonthlyOrError();
            if (data == null) return;

            string path = TempPdfPath($"monthly_{data.Period.Replace(" ", "_").Replace("/", "-")}");
            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveMonthlyPdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadMonthlyOrError();
            if (data == null) return;

            string? path = PickSavePath($"monthly_{MonthlyMonthBox.Text}_{MonthlyYearBox.Text}");
            if (path == null) return;

            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
        }

        private void PrintMonthlyDirect_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadMonthlyOrError();
            if (data == null) return;

            var err = _reportService.PrintPeriodReportDirect(data);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال التقرير الشهري إلى الطابعة.");
        }

        private PeriodReportData? LoadMonthlyOrError()
        {
            HideMsg();

            if (!int.TryParse(MonthlyMonthBox.Text, out int month) || month < 1 || month > 12)
            { ShowError("يرجى إدخال شهر صحيح (1-12)."); return null; }

            if (!int.TryParse(MonthlyYearBox.Text, out int year) || year < 1400)
            { ShowError("يرجى إدخال سنة هجرية صحيحة."); return null; }

            var data = _reportService.LoadMonthlyReport(year, month);
            if (data.TotalDossiers == 0)
            { ShowError($"لا توجد دوسيات لشهر {month}/{year}هـ."); return null; }

            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // YEARLY REPORT
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewYearly_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadYearlyOrError();
            if (data == null) return;

            string path = TempPdfPath($"yearly_{YearlyYearBox.Text}");
            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveYearlyPdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadYearlyOrError();
            if (data == null) return;

            string? path = PickSavePath($"yearly_{YearlyYearBox.Text}");
            if (path == null) return;

            var err = _reportService.GeneratePeriodReportPdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
        }

        private void PrintYearlyDirect_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadYearlyOrError();
            if (data == null) return;

            var err = _reportService.PrintPeriodReportDirect(data);
            if (err != null) ShowError(err);
            else ShowSuccess("تم إرسال التقرير السنوي إلى الطابعة.");
        }

        private PeriodReportData? LoadYearlyOrError()
        {
            HideMsg();

            if (!int.TryParse(YearlyYearBox.Text, out int year) || year < 1400)
            { ShowError("يرجى إدخال سنة هجرية صحيحة."); return null; }

            var data = _reportService.LoadYearlyReport(year);
            if (data.TotalDossiers == 0)
            { ShowError($"لا توجد دوسيات لسنة {year}هـ."); return null; }

            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static string TempPdfPath(string baseName)
            => Path.Combine(Path.GetTempPath(),
                $"{baseName}_{DateTime.Now:yyyyMMddHHmm}.pdf");

        private static string? PickSavePath(string defaultName)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"{defaultName}.pdf"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { /* ignore — file saved but can't open viewer */ }
        }

        private void ShowError(string msg)
        {
            MsgText.Text = msg;
            MsgBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
            MsgBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF9A9A"));
            MsgText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
            MsgBorder.BorderThickness = new Thickness(1);
            MsgBorder.Visibility = Visibility.Visible;
        }

        private void ShowSuccess(string msg)
        {
            MsgText.Text = msg;
            MsgBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
            MsgBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"));
            MsgText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            MsgBorder.BorderThickness = new Thickness(1);
            MsgBorder.Visibility = Visibility.Visible;
        }

        private void HideMsg() => MsgBorder.Visibility = Visibility.Collapsed;

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }
    }
}