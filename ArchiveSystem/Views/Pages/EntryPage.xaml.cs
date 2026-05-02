using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArchiveSystem.Core.Services;
using Dapper;

namespace ArchiveSystem.Views.Pages
{
    public partial class EntryPage : Page
    {
        private readonly DossierService _dossierService;
        private readonly RecordService _recordService;

        private int _currentDossierId = 0;
        private bool _isExistingDossier = false;

        private System.Windows.Threading.DispatcherTimer? _dossierCheckTimer;

        public EntryPage()
        {
            InitializeComponent();
            _dossierService = new DossierService(App.Database);
            _recordService = new RecordService(App.Database);
            Loaded += (s, e) => SuggestDossierNumber();
        }

        // ── AUTO SUGGEST ──────────────────────────────

        private void SuggestDossierNumber()
        {
            int next = _dossierService.GetNextDossierNumber();
            DossierNumberBox.Text = next.ToString();
            DossierStatusText.Text = string.Empty;
            UnlockDossierFields();
        }

        private void AutoDossierNumber_Click(object sender, RoutedEventArgs e)
        {
            SuggestDossierNumber();
        }

        private void AutoSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDossierId > 0)
            {
                int next = _recordService.GetNextSequenceNumber(_currentDossierId);
                SequenceBox.Text = next.ToString();
                return;
            }

            if (int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            {
                var existing = _dossierService.GetDossierByNumber(dossierNumber);
                if (existing != null)
                {
                    _currentDossierId = existing.DossierId;
                    int next = _recordService.GetNextSequenceNumber(_currentDossierId);
                    SequenceBox.Text = next.ToString();
                    return;
                }
            }

            SequenceBox.Text = "1";
        }

        // ── REAL-TIME DOSSIER CHECK ───────────────────

        private void DossierNumberBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            _dossierCheckTimer?.Stop();
            _dossierCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _dossierCheckTimer.Tick += (s, _) =>
            {
                _dossierCheckTimer.Stop();
                CheckDossierNumber();
            };
            _dossierCheckTimer.Start();
        }

        private void CheckDossierNumber()
        {
            if (!int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            {
                DossierStatusText.Text = string.Empty;
                _currentDossierId = 0;
                _isExistingDossier = false;
                UnlockDossierFields();
                return;
            }

            var existing = _dossierService.GetDossierByNumber(dossierNumber);

            if (existing != null)
            {
                _currentDossierId = existing.DossierId;
                _isExistingDossier = true;

                HijriMonthBox.Text = existing.HijriMonth.ToString();
                HijriYearBox.Text = existing.HijriYear.ToString();
                ExpectedCountBox.Text = existing.ExpectedFileCount?.ToString() ?? "";

                if (existing.CurrentLocation != null)
                {
                    HallwayBox.Text = existing.CurrentLocation.HallwayNumber.ToString();
                    CabinetBox.Text = existing.CurrentLocation.CabinetNumber.ToString();
                    ShelfBox.Text = existing.CurrentLocation.ShelfNumber.ToString();
                }

                LockDossierFields();

                int currentCount = _recordService
                    .GetRecordsByDossier(_currentDossierId).Count;
                int? expected = existing.ExpectedFileCount;
                string countInfo = expected.HasValue
                    ? $"{currentCount} من {expected} ملف"
                    : $"{currentCount} ملف مسجل";

                DossierStatusText.Text = $"✅ دوسية موجودة — {countInfo}";
                DossierStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(30, 120, 80));

                int nextSeq = _recordService.GetNextSequenceNumber(_currentDossierId);
                SequenceBox.Text = nextSeq.ToString();
            }
            else
            {
                _currentDossierId = 0;
                _isExistingDossier = false;
                DossierStatusText.Text = "🆕 دوسية جديدة";
                DossierStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(100, 100, 100));
                UnlockDossierFields();
            }
        }

        // ── LOCK / UNLOCK DOSSIER FIELDS ─────────────

        private void LockDossierFields()
        {
            HijriMonthBox.IsReadOnly = true;
            HijriYearBox.IsReadOnly = true;
            ExpectedCountBox.IsReadOnly = true;
            HallwayBox.IsReadOnly = true;
            CabinetBox.IsReadOnly = true;
            ShelfBox.IsReadOnly = true;

            HijriMonthBox.Opacity = 0.6;
            HijriYearBox.Opacity = 0.6;
            ExpectedCountBox.Opacity = 0.6;
            HallwayBox.Opacity = 0.6;
            CabinetBox.Opacity = 0.6;
            ShelfBox.Opacity = 0.6;
        }

        private void UnlockDossierFields()
        {
            HijriMonthBox.IsReadOnly = false;
            HijriYearBox.IsReadOnly = false;
            ExpectedCountBox.IsReadOnly = false;
            HallwayBox.IsReadOnly = false;
            CabinetBox.IsReadOnly = false;
            ShelfBox.IsReadOnly = false;

            HijriMonthBox.Opacity = 1;
            HijriYearBox.Opacity = 1;
            ExpectedCountBox.Opacity = 1;
            HallwayBox.Opacity = 1;
            CabinetBox.Opacity = 1;
            ShelfBox.Opacity = 1;
        }

        // ── SAVE ─────────────────────────────────────

        private void SaveRecord_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            if (!int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            { ShowError("رقم الدوسية غير صحيح."); return; }

            if (!int.TryParse(SequenceBox.Text, out int sequence))
            { ShowError("رقم التسلسل غير صحيح."); return; }

            string personName = PersonNameBox.Text.Trim();
            string prisonerNumber = PrisonerNumberBox.Text.Trim();
            string? notes = string.IsNullOrWhiteSpace(NotesBox.Text)
                                      ? null : NotesBox.Text.Trim();

            // ── Get or create dossier ──────────────────
            if (_currentDossierId <= 0)
            {
                if (!int.TryParse(HijriMonthBox.Text, out int hijriMonth))
                { ShowError("الشهر الهجري غير صحيح."); return; }

                if (!int.TryParse(HijriYearBox.Text, out int hijriYear))
                { ShowError("السنة الهجرية غير صحيحة."); return; }

                if (!int.TryParse(HallwayBox.Text, out int hallway))
                { ShowError("رقم الممر غير صحيح."); return; }

                if (!int.TryParse(CabinetBox.Text, out int cabinet))
                { ShowError("رقم الكبينة غير صحيح."); return; }

                if (!int.TryParse(ShelfBox.Text, out int shelf))
                { ShowError("رقم الرف غير صحيح."); return; }

                int? expectedCount = null;
                if (!string.IsNullOrWhiteSpace(ExpectedCountBox.Text))
                {
                    if (!int.TryParse(ExpectedCountBox.Text, out int ec))
                    { ShowError("عدد الملفات المتوقع غير صحيح."); return; }
                    expectedCount = ec;
                }

                var (dossierError, dossierId) = _dossierService.CreateDossier(
                    dossierNumber, hijriMonth, hijriYear,
                    expectedCount, hallway, cabinet, shelf);

                if (dossierError != null) { ShowError(dossierError); return; }

                _currentDossierId = dossierId;
                _isExistingDossier = false;
            }

            // ── Add record ────────────────────────────
            var (recordError, newRecordId) = _recordService.AddRecord(
                _currentDossierId, sequence,
                personName, prisonerNumber, notes);

            if (recordError != null) { ShowError(recordError); return; }

            // ── Save nationality custom field ─────────
            if (!string.IsNullOrWhiteSpace(NationalityBox.Text))
            {
                using var conn = App.Database.CreateConnection();
                var fieldId = conn.ExecuteScalar<int?>(
                    "SELECT CustomFieldId FROM CustomFields WHERE FieldKey = 'nationality'");
                if (fieldId.HasValue)
                {
                    conn.Execute(@"
                        INSERT INTO RecordCustomFieldValues
                            (RecordId, CustomFieldId, ValueText, UpdatedAt)
                        VALUES (@RecordId, @FieldId, @Value, @Now)
                        ON CONFLICT(RecordId, CustomFieldId)
                        DO UPDATE SET ValueText = @Value, UpdatedAt = @Now",
                        new
                        {
                            RecordId = newRecordId,
                            FieldId = fieldId.Value,
                            Value = NationalityBox.Text.Trim(),
                            Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                        });
                }
            }

            // ── Success ───────────────────────────────
            int newCount = _recordService
                .GetRecordsByDossier(_currentDossierId).Count;

            var dossier = _dossierService.GetDossierById(_currentDossierId);
            int? expectedFinal = dossier?.ExpectedFileCount;

            string countMsg = expectedFinal.HasValue
                ? $"{newCount} من {expectedFinal} ملف"
                : $"{newCount} ملف مسجل";

            ShowSuccess(
                $"✅ تم حفظ السجل بنجاح.\n" +
                $"الدوسية: {dossierNumber}  |  " +
                $"السجين: {personName}  |  " +
                $"الرقم: {prisonerNumber}\n" +
                $"إجمالي الدوسية: {countMsg}");

            DossierStatusText.Text = $"✅ دوسية موجودة — {countMsg}";

            ClearRecordFields();

            int nextSeq = _recordService.GetNextSequenceNumber(_currentDossierId);
            SequenceBox.Text = nextSeq.ToString();
        }

        // ── CLEAR ─────────────────────────────────────

        private void ClearRecord_Click(object sender, RoutedEventArgs e)
        {
            ClearRecordFields();
            HideMessages();
        }

        private void ClearRecordFields()
        {
            PersonNameBox.Text = string.Empty;
            PrisonerNumberBox.Text = string.Empty;
            NationalityBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            PersonNameBox.Focus();
        }

        // ── VALIDATION ────────────────────────────────

        private void NumberOnly_PreviewTextInput(object sender,
            TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        // ── UI HELPERS ────────────────────────────────

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
            SuccessBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowSuccess(string msg)
        {
            SuccessText.Text = msg;
            SuccessBorder.Visibility = Visibility.Visible;
            ErrorBorder.Visibility = Visibility.Collapsed;
        }

        private void HideMessages()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
            SuccessBorder.Visibility = Visibility.Collapsed;
        }
    }
}