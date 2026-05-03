using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArchiveSystem.Views.Pages
{
    public partial class ExcelImportPage : Page
    {
        private readonly ExcelImportService _importService;
        private readonly BackupService _backupService;

        private int _currentBatchId;
        private string? _filePath;

        public ExcelImportPage()
        {
            InitializeComponent();
            _importService = new ExcelImportService(App.Database);
            _backupService = new BackupService(App.Database, App.DbPath);
            Loaded += (s, e) =>
            {
                if (PermissionHelper.DenyPage(this, Permissions.ImportExcel)) return;
                LoadRecentBatches();
                // Hide approve button if no ApproveExcelImport permission
                PermissionHelper.Apply(ApproveButton, Permissions.ApproveExcelImport, hideInstead: true);
                // Hide rollback button too — only approvers can rollback
                PermissionHelper.Apply(RollbackBtn, Permissions.ApproveExcelImport, hideInstead: true);
            };
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

            CollapseReviewPanels();
            _currentBatchId = 0;
        }

        // ── STAGE ─────────────────────────────────────────────────────────────

        private async void StageFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            {
                MessageBox.Show("يرجى اختيار ملف Excel صالح أولاً.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StageButton.IsEnabled = false;
            BusyPanel.Visibility = Visibility.Visible;
            CollapseReviewPanels();

            StagingResult result;
            try
            {
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

        // ── SUMMARY CARD ──────────────────────────────────────────────────────

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

        // ── APPROVE (FIX: backup first) ───────────────────────────────────────

        private void Approve_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatchId <= 0) return;

            var blocker = _importService.CanApprove(_currentBatchId);
            if (blocker != null)
            {
                MessageBox.Show(blocker, "لا يمكن الاعتماد بعد",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "سيتم إنشاء نسخة احتياطية أولاً، ثم إضافة جميع السجلات والدوسيات الصحيحة.\n\nهل تريد المتابعة؟",
                "تأكيد اعتماد الاستيراد",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // FIX: Step 1 — backup before any data change
            var (backupError, backupPath) = _backupService.CreateBackup(null, "BeforeImport");
            if (backupError != null)
            {
                var continueAnyway = MessageBox.Show(
                    $"فشل إنشاء النسخة الاحتياطية:\n{backupError}\n\nهل تريد المتابعة بدون نسخة احتياطية؟",
                    "تحذير", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (continueAnyway != MessageBoxResult.Yes) return;
            }

            ApproveButton.IsEnabled = false;

            var error = _importService.ApproveBatch(_currentBatchId);

            if (error != null)
            {
                ApproveButton.IsEnabled = true;
                MessageBox.Show(error, "خطأ في الاعتماد",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string backupMsg = backupPath != null
                ? $"\n✅ نسخة احتياطية: {System.IO.Path.GetFileName(backupPath)}"
                : "";
            MessageBox.Show($"✅ تم اعتماد الاستيراد وإضافة البيانات بنجاح.{backupMsg}",
                "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);

            ResetToIdle();
            LoadRecentBatches();
        }

        // ── ROLLBACK (FIX: backup first + warn if records were edited) ─────────

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatchId <= 0) return;

            // FIX: Warn if edited records exist after import
            int editedCount = _importService.CountEditedRecords(_currentBatchId);
            string editWarning = editedCount > 0
                ? $"\n\n⚠️ تحذير: {editedCount} سجل تم تعديله بعد الاستيراد — سيتم حذفه أيضاً!"
                : "";

            var confirm = MessageBox.Show(
                $"سيتم إنشاء نسخة احتياطية أولاً، ثم حذف جميع البيانات المستوردة من هذه الدفعة.{editWarning}\n\nهل تريد المتابعة؟",
                "تأكيد الإلغاء",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // FIX: Step 1 — backup before rollback
            var (backupError, _) = _backupService.CreateBackup(null, "BeforeImport");
            if (backupError != null)
            {
                var continueAnyway = MessageBox.Show(
                    $"فشل إنشاء النسخة الاحتياطية:\n{backupError}\n\nهل تريد المتابعة بدون نسخة احتياطية؟",
                    "تحذير", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (continueAnyway != MessageBoxResult.Yes) return;
            }

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

        // ── RESOLVE WARNING ───────────────────────────────────────────────────

        private void ResolveWarning_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not int warningId) return;

            _importService.ResolveWarning(warningId);
            RefreshReviewData();
        }

        // ── EDIT STAGING DOSSIER METADATA (FIX: was missing) ─────────────────
        // Double-click on a dossier row in the Dossiers tab to edit its metadata

        private void DossiersGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DossiersGrid.SelectedItem is not StagedDossierView staged) return;

            // Only allow editing dossiers that have missing metadata or need review
            var dlg = new Dialogs.EditStagingDossierDialog(staged)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() != true) return;

            // Apply the changes
            var error = _importService.UpdateStagingDossier(
                staged.StagingDossierId,
                dlg.DossierNumber,
                dlg.HijriMonth,
                dlg.HijriYear,
                dlg.ExpectedCount);

            if (error != null)
            {
                MessageBox.Show(error, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshReviewData();
        }

        // ── RECENT BATCHES ────────────────────────────────────────────────────

        private void LoadRecentBatches()
        {
            RecentGrid.ItemsSource = _importService.GetRecentBatches(10);
        }

        private void RecentBatch_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentGrid.SelectedItem is not ImportBatch batch) return;

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

        // ── REFRESH ───────────────────────────────────────────────────────────

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