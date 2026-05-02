using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Microsoft.Win32;

namespace ArchiveSystem.Views.Pages
{
    public partial class ExcelImportPage : Page
    {
        private readonly ExcelImportService _importService;

        // Tracks whichever batch is currently being reviewed
        private int _currentBatchId;
        private string? _filePath;

        public ExcelImportPage()
        {
            InitializeComponent();
            _importService = new ExcelImportService(App.Database);
            Loaded += (s, e) => LoadRecentBatches();
        }

        // ── FILE SELECTION ────────────────────────────────────────────────────

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Title = "اختر ملف Excel للاستيراد"
            };

            if (dlg.ShowDialog() != true) return;

            _filePath = dlg.FileName;
            FilePathBox.Text = _filePath;

            // Reset any previous review session
            CollapseReviewPanels();
            _currentBatchId = 0;
        }

        // ── STAGE (process the workbook into staging tables) ──────────────────

        private async void StageFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            {
                MessageBox.Show("يرجى اختيار ملف Excel صالح أولاً.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --- UI: show busy state ---
            StageButton.IsEnabled = false;
            BusyPanel.Visibility = Visibility.Visible;
            CollapseReviewPanels();

            StagingResult result;
            try
            {
                // Heavy file-read runs off the UI thread
                result = await Task.Run(() => _importService.StageBatch(_filePath));
            }
            finally
            {
                BusyPanel.Visibility = Visibility.Collapsed;
                StageButton.IsEnabled = true;
            }

            if (result.Error != null)
            {
                MessageBox.Show(result.Error, "خطأ في معالجة الملف",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentBatchId = result.BatchId;

            ShowStagingResult(result);
            RefreshReviewData();
            LoadRecentBatches();
        }

        // ── RENDER SUMMARY CARD ───────────────────────────────────────────────

        private void ShowStagingResult(StagingResult result)
        {
            StagingTitleText.Text = $"نتيجة معالجة:  {result.FileName}";
            StagingSubText.Text =
                $"جاهز: {result.ReadySheets}  |  يحتاج مراجعة: {result.NeedsReviewSheets}  |  إجمالي الشيتات: {result.TotalSheets}";

            SumSheets.Text = result.TotalSheets.ToString("N0");
            SumDossiers.Text = result.TotalDossiers.ToString("N0");
            SumRecords.Text = result.TotalRecords.ToString("N0");
            SumWarnings.Text = result.WarningCount.ToString("N0");
            SumDupes.Text = result.DuplicateCount.ToString("N0");

            StagingResultBorder.Visibility = Visibility.Visible;
            ReviewBorder.Visibility = Visibility.Visible;

            RefreshBlockerBanner();
        }

        // Repopulate dossiers grid, warnings grid, and the blocker banner
        private void RefreshReviewData()
        {
            if (_currentBatchId <= 0) return;

            DossiersGrid.ItemsSource = _importService.GetStagedDossiers(_currentBatchId);

            var warnings = _importService.GetWarnings(_currentBatchId);
            WarningsGrid.ItemsSource = warnings;

            int unresolved = warnings.Count(w => !w.IsResolved);
            int total = warnings.Count;

            WarningsTab.Header = unresolved > 0 ? $"التحذيرات  ⚠️ ({unresolved})" : "التحذيرات  ✅";
            WarningsSummaryText.Text = $"إجمالي التحذيرات: {total}  |  محلول: {total - unresolved}  |  متبقٍ: {unresolved}";

            RefreshBlockerBanner();
        }

        private void RefreshBlockerBanner()
        {
            var blocker = _importService.CanApprove(_currentBatchId);

            if (blocker != null)
            {
                BlockerText.Text = blocker;
                BlockerBorder.Visibility = Visibility.Visible;
                ReadyBorder.Visibility = Visibility.Collapsed;
                ApproveButton.IsEnabled = false;
            }
            else
            {
                BlockerBorder.Visibility = Visibility.Collapsed;
                ReadyBorder.Visibility = Visibility.Visible;
                ApproveButton.IsEnabled = true;
            }
        }

        // ── APPROVE ───────────────────────────────────────────────────────────

        private void Approve_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatchId <= 0) return;

            // Final pre-check (user might have resolved warnings since the banner last updated)
            var blocker = _importService.CanApprove(_currentBatchId);
            if (blocker != null)
            {
                MessageBox.Show(blocker, "لا يمكن الاعتماد بعد",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "سيتم إضافة جميع السجلات والدوسيات الصحيحة إلى قاعدة البيانات.\n\nهل تريد المتابعة؟",
                "تأكيد اعتماد الاستيراد",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            ApproveButton.IsEnabled = false;

            var error = _importService.ApproveBatch(_currentBatchId);

            if (error != null)
            {
                ApproveButton.IsEnabled = true;
                MessageBox.Show(error, "خطأ في الاعتماد",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("✅ تم اعتماد الاستيراد وإضافة البيانات بنجاح.",
                "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);

            ResetToIdle();
            LoadRecentBatches();
        }

        // ── ROLLBACK / CANCEL BATCH ───────────────────────────────────────────

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatchId <= 0) return;

            var confirm = MessageBox.Show(
                "سيتم إلغاء هذه الدفعة وحذف أي بيانات تم استيرادها منها.\n\nهل تريد المتابعة؟",
                "تأكيد الإلغاء",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var error = _importService.RollbackBatch(_currentBatchId);

            if (error != null)
            {
                MessageBox.Show(error, "خطأ أثناء الإلغاء",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("تم إلغاء الدفعة بنجاح.",
                "إلغاء الدفعة", MessageBoxButton.OK, MessageBoxImage.Information);

            ResetToIdle();
            LoadRecentBatches();
        }

        // ── RESOLVE A SINGLE WARNING ──────────────────────────────────────────

        private void ResolveWarning_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not int warningId) return;

            _importService.ResolveWarning(warningId);

            // Refresh the warnings grid and re-evaluate the approve button
            RefreshReviewData();
        }

        // ── RECENT BATCHES ────────────────────────────────────────────────────

        private void LoadRecentBatches()
        {
            RecentGrid.ItemsSource = _importService.GetRecentBatches(10);
        }

        // Double-clicking a "ReadyForReview" row in the recent list reopens it
        private void RecentBatch_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentGrid.SelectedItem is not ImportBatch batch) return;

            // Only allow re-opening batches that are still staged / awaiting approval
            if (batch.Status is "Imported" or "RolledBack" or "Failed")
            {
                MessageBox.Show(
                    $"حالة هذه الدفعة '{batch.Status}' — لا يمكن إعادة فتحها للمراجعة.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _currentBatchId = batch.ImportBatchId;
            _filePath = null;
            FilePathBox.Text = batch.FileName;

            // Populate summary numbers from the stored batch record
            StagingTitleText.Text = $"مراجعة دفعة:  {batch.FileName}";
            StagingSubText.Text = $"الحالة الحالية: {batch.Status}";
            SumSheets.Text = batch.TotalSheets.ToString("N0");
            SumDossiers.Text = batch.TotalDossiers.ToString("N0");
            SumRecords.Text = batch.TotalRecords.ToString("N0");
            SumWarnings.Text = batch.WarningCount.ToString("N0");
            SumDupes.Text = "—";

            StagingResultBorder.Visibility = Visibility.Visible;
            ReviewBorder.Visibility = Visibility.Visible;

            RefreshReviewData();
        }

        // ── REFRESH BUTTON ────────────────────────────────────────────────────

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadRecentBatches();
            if (_currentBatchId > 0)
                RefreshReviewData();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void CollapseReviewPanels()
        {
            StagingResultBorder.Visibility = Visibility.Collapsed;
            ReviewBorder.Visibility = Visibility.Collapsed;
            BlockerBorder.Visibility = Visibility.Collapsed;
            ReadyBorder.Visibility = Visibility.Collapsed;
        }

        private void ResetToIdle()
        {
            _currentBatchId = 0;
            _filePath = null;
            FilePathBox.Text = string.Empty;
            CollapseReviewPanels();
        }
    }
}