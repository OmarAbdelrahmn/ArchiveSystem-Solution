using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArchiveSystem.Views.Pages
{
    public partial class DossierDetailsPage : Page
    {
        private readonly DossierService _dossierService;
        private readonly RecordService _recordService;
        private readonly CustomFieldService _customFieldService;
        private readonly ReportService _reportService;

        private readonly int _dossierId;
        private Dossier? _dossier;
        private List<CustomField> _customFields = new();

        // Suppress SelectionChanged feedback loop while we set status programmatically
        private bool _suppressStatusChange = false;

        private void AddRecord_Click(object sender, RoutedEventArgs e)
        {
            int nextSeq = _recordService.GetNextSequenceNumber(_dossierId);

            // Reuse EditRecordDialog with a blank record
            var blankRecord = new ArchiveSystem.Core.Models.Record
            {
                RecordId = 0,
                DossierId = _dossierId,
                SequenceNumber = nextSeq,
                PersonName = string.Empty,
                PrisonerNumber = string.Empty
            };

            var dlg = new ArchiveSystem.Views.Dialogs.EditRecordDialog(
                blankRecord, _customFields, new Dictionary<int, string?>())
            {
                Owner = Window.GetWindow(this),
                Title = "إضافة سجل جديد"
            };

            if (dlg.ShowDialog() != true) return;

            var (err, newRecordId) = _recordService.AddRecord(
                _dossierId,
                nextSeq,
                dlg.PersonName,
                dlg.PrisonerNumber,
                dlg.Notes);

            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            foreach (var (fieldId, value) in dlg.CustomFieldValues)
                _customFieldService.SaveFieldValue(newRecordId, fieldId, value);

            LoadRecords();
        }

        public DossierDetailsPage(int dossierId)
        {
            InitializeComponent();
            _dossierService = new DossierService(App.Database);
            _recordService = new RecordService(App.Database);
            _customFieldService = new CustomFieldService(App.Database);
            _reportService = new ReportService(App.Database);
            _dossierId = dossierId;
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ──────────────────────────────────────────────────────────────

        private void Initialize()
        {
            // Load custom field columns first (before data)
            _customFields = _customFieldService.GetActiveEntryFields();
            AddCustomFieldColumns();
            ApplyPermissions();
            LoadAll();
        }

        private void ApplyPermissions()
        {
            PermissionHelper.Apply(PrintFaceBtn, Permissions.PrintDossierFace, hideInstead: true);
            PermissionHelper.Apply(EditDossierBtn, Permissions.EditDossier, hideInstead: true);
            PermissionHelper.Apply(MoveDossierBtn, Permissions.MoveDossier, hideInstead: true);
            PermissionHelper.Apply(StatusCombo, Permissions.EditDossier, hideInstead: true);
            PermissionHelper.Apply(EditRecordBtn, Permissions.EditRecord, hideInstead: true);
            PermissionHelper.Apply(DeleteRecordBtn, Permissions.DeleteRecord, hideInstead: true);
            PermissionHelper.Apply(RegisterMoveBtn, Permissions.MoveDossier, hideInstead: true);
            PermissionHelper.Apply(DeleteDossierBtn, Permissions.DeleteDossier, hideInstead: true);
            PermissionHelper.Apply(AddRecordBtn, Permissions.AddRecord, hideInstead: true);
        }


        private void DeleteDossier_Click(object sender, RoutedEventArgs e)
        {
            if (_dossier == null) return;

            var activeRecords = _recordService.GetRecordsByDossier(_dossierId);

            string reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"أدخل سبب حذف الدوسية رقم {_dossier.DossierNumber}:\n\n" +
                $"⚠️ سيتم حذف {activeRecords.Count} سجل داخلها أيضاً.",
                "حذف الدوسية", "");

            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("سبب الحذف مطلوب. تم إلغاء العملية.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"⚠️ سيتم حذف الدوسية رقم {_dossier.DossierNumber} " +
                $"و{activeRecords.Count} سجل بداخلها.\n\nالسبب: {reason}\n\nهل تريد المتابعة؟",
                "تأكيد الحذف",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var (error, deleted) = _dossierService.DeleteDossier(_dossierId, reason);

            if (error != null)
            {
                MessageBox.Show(error, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                $"✅ تم حذف الدوسية و{deleted} سجل بنجاح.",
                "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information);

            NavigationService?.GoBack();
        }

        private void AddCustomFieldColumns()
        {
            // Remove any previously added custom columns (keep first 4 built-in)
            while (RecordsGrid.Columns.Count > 4)
                RecordsGrid.Columns.RemoveAt(RecordsGrid.Columns.Count - 1);

            foreach (var cf in _customFields)
            {
                RecordsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = cf.ArabicLabel,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new System.Windows.Data.Binding($"CustomValues[{cf.CustomFieldId}]")
                    {
                        TargetNullValue = string.Empty,
                        FallbackValue = string.Empty
                    }
                });
            }
        }

        // ── LOAD ALL ─────────────────────────────────────────────────────────

        private void LoadAll()
        {
            LoadDossier();
            LoadRecords();
            LoadMovements();
        }

        private void LoadDossier()
        {
            _dossier = _dossierService.GetDossierById(_dossierId);
            if (_dossier == null) return;

            // Header
            HeaderTitle.Text = $"📁  دوسية رقم {_dossier.DossierNumber}";
            DossierNumberText.Text = _dossier.DossierNumber.ToString();
            HijriDateText.Text = $"{_dossier.HijriMonth} / {_dossier.HijriYear} هـ";
            LocationText.Text = _dossier.CurrentLocation?.DisplayName ?? "غير محدد";
            CreatedAtText.Text = _dossier.CreatedAt.Length >= 10
                ? _dossier.CreatedAt[..10] : _dossier.CreatedAt;

            // Status badge
            (string badgeText, string bg, string fg) = _dossier.Status switch
            {
                "Complete" => ("مكتملة ✓", "#E8F5E9", "#2E7D32"),
                "Archived" => ("مؤرشفة 🗄", "#EDE7F6", "#4527A0"),
                _ => ("مفتوحة", "#E3F2FD", "#1565C0")
            };

            StatusBadgeText.Text = badgeText;
            StatusBadge.Background = new System.Windows.Media.BrushConverter()
                .ConvertFromString(bg) as System.Windows.Media.Brush;
            StatusBadgeText.Foreground = new System.Windows.Media.BrushConverter()
                .ConvertFromString(fg) as System.Windows.Media.Brush;

            // Sync status combo without triggering change handler
            _suppressStatusChange = true;
            foreach (ComboBoxItem item in StatusCombo.Items)
            {
                if (item.Tag?.ToString() == _dossier.Status)
                {
                    StatusCombo.SelectedItem = item;
                    break;
                }
            }
            _suppressStatusChange = false;
        }

        private void LoadRecords()
        {
            var records = _recordService.GetRecordsByDossier(_dossierId);

            // Attach custom field values
            var rows = records.Select(r =>
            {
                var values = _customFieldService.GetRecordValues(r.RecordId);
                return new RecordRow
                {
                    RecordId = r.RecordId,
                    SequenceNumber = r.SequenceNumber,
                    PersonName = r.PersonName,
                    PrisonerNumber = r.PrisonerNumber,
                    Notes = r.Notes,
                    Status = r.Status,
                    CustomValues = values
                };
            }).ToList();

            RecordsGrid.ItemsSource = rows;

            int actual = rows.Count;
            int? expected = _dossier?.ExpectedFileCount;
            FileCountText.Text = expected.HasValue
                ? $"{actual} / {expected}"
                : $"{actual} ملف";

            RecordCountText.Text = $"إجمالي السجلات: {actual}" +
                (expected.HasValue ? $"  |  المتوقع: {expected}" : "");
        }

        private void LoadMovements()
        {
            MovementsGrid.ItemsSource = _dossierService.GetMovementHistory(_dossierId);
        }

        // ── PRINT FACE ────────────────────────────────────────────────────────

        private void PrintFace_Click(object sender, RoutedEventArgs e)
        {
            var data = _reportService.LoadDossierFaceData(_dossierId);
            if (data == null || data.Records.Count == 0)
            {
                MessageBox.Show("لا توجد سجلات للطباعة.", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dossier_face_{data.DossierNumber}_{DateTime.Now:yyyyMMddHHmm}.pdf");

            var err = _reportService.GenerateDossierFacePdf(data, path);
            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { /* viewer not available */ }
        }

        // ── EDIT DOSSIER METADATA ─────────────────────────────────────────────

        private void EditDossier_Click(object sender, RoutedEventArgs e)
        {
            if (_dossier == null) return;

            var dlg = new EditDossierDialog(_dossier) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var err = _dossierService.UpdateDossier(
                _dossierId,
                dlg.HijriMonth,
                dlg.HijriYear,
                dlg.ExpectedCount,
                dlg.Hallway,
                dlg.Cabinet,
                dlg.Shelf);

            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadAll();
        }

        // ── MOVE DOSSIER ──────────────────────────────────────────────────────

        private void MoveDossier_Click(object sender, RoutedEventArgs e)
        {
            if (_dossier == null) return;

            // Pre-fill move form with current location
            if (_dossier.CurrentLocation != null)
            {
                MoveHallwayBox.Text = _dossier.CurrentLocation.HallwayNumber.ToString();
                MoveCabinetBox.Text = _dossier.CurrentLocation.CabinetNumber.ToString();
                MoveShelfBox.Text = _dossier.CurrentLocation.ShelfNumber.ToString();
            }

            // Switch to movement tab
            if (FindName("MovementTab") is TabItem tab)
                tab.IsSelected = true;

            MoveHallwayBox.Focus();
        }

        private void RegisterMove_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(MoveHallwayBox.Text, out int hallway) || hallway <= 0 ||
                !int.TryParse(MoveCabinetBox.Text, out int cabinet) || cabinet <= 0 ||
                !int.TryParse(MoveShelfBox.Text, out int shelf) || shelf <= 0)
            {
                MessageBox.Show("يرجى إدخال أرقام الممر والكبينة والرف بشكل صحيح.",
                    "خطأ في الإدخال", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? reason = string.IsNullOrWhiteSpace(MoveReasonBox.Text)
                ? null : MoveReasonBox.Text.Trim();

            var err = _dossierService.MoveDossier(_dossierId, hallway, cabinet, shelf, reason);
            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MoveHallwayBox.Text = string.Empty;
            MoveCabinetBox.Text = string.Empty;
            MoveShelfBox.Text = string.Empty;
            MoveReasonBox.Text = string.Empty;

            LoadAll();

            MessageBox.Show("✅ تم تسجيل حركة الدوسية بنجاح.",
                "نقل الدوسية", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── STATUS CHANGE ─────────────────────────────────────────────────────

        private void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressStatusChange) return;
            if (_dossier == null) return;
            if (StatusCombo.SelectedItem is not ComboBoxItem item) return;

            string newStatus = item.Tag?.ToString() ?? "Open";
            if (newStatus == _dossier.Status) return;

            string statusArabic = item.Content?.ToString() ?? newStatus;
            var confirm = MessageBox.Show(
                $"هل تريد تغيير حالة الدوسية إلى '{statusArabic}'؟",
                "تغيير الحالة",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                // Revert combo
                _suppressStatusChange = true;
                foreach (ComboBoxItem ci in StatusCombo.Items)
                    if (ci.Tag?.ToString() == _dossier.Status)
                    { StatusCombo.SelectedItem = ci; break; }
                _suppressStatusChange = false;
                return;
            }

            var err = _dossierService.SetDossierStatus(_dossierId, newStatus);
            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadDossier();
        }

        // ── EDIT RECORD ───────────────────────────────────────────────────────

        private void EditRecord_Click(object sender, RoutedEventArgs e)
        {
            if (RecordsGrid.SelectedItem is not RecordRow row) { PromptSelectRecord(); return; }
            OpenEditRecordDialog(row.RecordId);
        }

        private void RecordsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecordsGrid.SelectedItem is not RecordRow row) return;
            OpenEditRecordDialog(row.RecordId);
        }

        private void OpenEditRecordDialog(int recordId)
        {
            var record = _recordService.GetRecordById(recordId);
            if (record == null) return;

            // Load existing custom field values for this record
            var existingValues = _customFieldService.GetRecordValues(recordId);

            var dlg = new EditRecordDialog(record, _customFields, existingValues)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() != true) return;

            var err = _recordService.UpdateRecord(
                recordId,
                dlg.PersonName,
                dlg.PrisonerNumber,
                dlg.Notes);

            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save custom field values
            foreach (var (fieldId, value) in dlg.CustomFieldValues)
                _customFieldService.SaveFieldValue(recordId, fieldId, value);

            LoadRecords();
        }

        // ── DELETE RECORD ─────────────────────────────────────────────────────

        private void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (RecordsGrid.SelectedItem is not RecordRow row) { PromptSelectRecord(); return; }

            string reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"أدخل سبب حذف سجل '{row.PersonName}' ({row.PrisonerNumber}):",
                "حذف السجل", "");

            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("سبب الحذف مطلوب.", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"هل تريد حذف سجل '{row.PersonName}'؟\nالسبب: {reason}",
                "تأكيد الحذف",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var err = _recordService.DeleteRecord(row.RecordId, reason);
            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadRecords();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void PromptSelectRecord()
        {
            MessageBox.Show("يرجى اختيار سجل من الجدول أولاً.",
                "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
            => NavigationService?.GoBack();

        private void NumberOnly(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    // ── View-model row for the records DataGrid ───────────────────────────────
    public class RecordRow
    {
        public int RecordId { get; set; }
        public int SequenceNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string PrisonerNumber { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string Status { get; set; } = "Active";
        public Dictionary<int, string?> CustomValues { get; set; } = new();
    }
}