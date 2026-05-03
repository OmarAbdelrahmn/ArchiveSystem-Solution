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

        public ReportsPage()
        {
            InitializeComponent();
            _reportService = new ReportService(App.Database);
            _dossierService = new DossierService(App.Database);
            Loaded += (s, e) => ApplyPermissions();
        }


        private void ApplyPermissions()
        {
            // Dossier face section — needs PrintDossierFace
            PermissionHelper.Apply(DossierFacePreviewBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(DossierFaceSaveBtn, Permissions.PrintDossierFace, hideInstead: true);

            // Monthly/Yearly reports — needs PrintReports
            PermissionHelper.Apply(MonthlyPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(MonthlySaveBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(YearlyPreviewBtn, Permissions.PrintReports, hideInstead: true);
            PermissionHelper.Apply(YearlySaveBtn, Permissions.PrintReports, hideInstead: true);
        }


        // ─────────────────────────────────────────────────────────────────────
        // DOSSIER FACE
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewDossierFace_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDossierFaceOrError();
            if (data == null) return;

            // Generate to temp file then open
            string path = Path.Combine(Path.GetTempPath(),
                $"dossier_face_{data.DossierNumber}_{DateTime.Now:yyyyMMddHHmm}.pdf");

            var err = _reportService.GenerateDossierFacePdf(data, path);
            if (err != null) { ShowError(err); return; }

            OpenFile(path);
        }

        private void SaveDossierFacePdf_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadDossierFaceOrError();
            if (data == null) return;

            string? path = PickSavePath(
                $"dossier_{data.DossierNumber}_{data.HijriMonth}_{data.HijriYear}");
            if (path == null) return;

            var err = _reportService.GenerateDossierFacePdf(data, path);
            if (err != null) { ShowError(err); return; }

            ShowSuccess($"تم حفظ الملف: {path}");
            OpenFile(path);
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
        // MONTHLY REPORT
        // ─────────────────────────────────────────────────────────────────────

        private void PreviewMonthly_Click(object sender, RoutedEventArgs e)
        {
            var data = LoadMonthlyOrError();
            if (data == null) return;

            string path = Path.Combine(Path.GetTempPath(),
                $"monthly_{data.Period.Replace(" ", "_").Replace("/", "-")}_{DateTime.Now:yyyyMMddHHmm}.pdf");

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

            string path = Path.Combine(Path.GetTempPath(),
                $"yearly_{YearlyYearBox.Text}_{DateTime.Now:yyyyMMddHHmm}.pdf");

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
            MsgBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FFEBEE"));
            MsgBorder.BorderBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#EF9A9A"));
            MsgText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#C62828"));
            MsgBorder.BorderThickness = new Thickness(1);
            MsgBorder.Visibility = Visibility.Visible;
        }

        private void ShowSuccess(string msg)
        {
            MsgText.Text = msg;
            MsgBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#E8F5E9"));
            MsgBorder.BorderBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#A5D6A7"));
            MsgText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#2E7D32"));
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