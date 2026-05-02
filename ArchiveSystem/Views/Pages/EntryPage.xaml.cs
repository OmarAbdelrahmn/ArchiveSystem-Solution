using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Pages
{
    public partial class EntryPage : Page
    {
        private readonly DossierService _dossierService;
        private readonly RecordService _recordService;

        // track the dossier we are currently adding records into
        private int _currentDossierId = 0;

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
        }

        private void AutoDossierNumber_Click(object sender, RoutedEventArgs e)
        {
            SuggestDossierNumber();
        }

        private void AutoSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDossierId <= 0)
            {
                ShowError("احفظ بيانات الدوسية أولاً للحصول على رقم التسلسل التلقائي.");
                return;
            }
            int next = _recordService.GetNextSequenceNumber(_currentDossierId);
            SequenceBox.Text = next.ToString();
        }

        // ── SAVE ─────────────────────────────────────

        private void SaveRecord_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            // ── Parse dossier fields ──
            if (!int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            { ShowError("رقم الدوسية غير صحيح."); return; }

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

            // ── Parse record fields ──
            if (!int.TryParse(SequenceBox.Text, out int sequence))
            { ShowError("رقم التسلسل غير صحيح."); return; }

            string personName = PersonNameBox.Text.Trim();
            string prisonerNumber = PrisonerNumberBox.Text.Trim();
            string? notes = string.IsNullOrWhiteSpace(NotesBox.Text)
                                        ? null : NotesBox.Text.Trim();

            // ── Get or create dossier ──
            var existingDossier = _dossierService.GetDossierByNumber(dossierNumber);

            if (existingDossier != null)
            {
                _currentDossierId = existingDossier.DossierId;
            }
            else
            {
                var (dossierError, dossierId) = _dossierService.CreateDossier(
                    dossierNumber, hijriMonth, hijriYear,
                    expectedCount, hallway, cabinet, shelf);

                if (dossierError != null) { ShowError(dossierError); return; }
                _currentDossierId = dossierId;
            }

            // ── Add record ──
            var (recordError, recordId) = _recordService.AddRecord(
                _currentDossierId, sequence,
                personName, prisonerNumber, notes);

            if (recordError != null) { ShowError(recordError); return; }

            // ── Success ──
            ShowSuccess(
                $"✅ تم حفظ السجل بنجاح.\n" +
                $"الدوسية: {dossierNumber}  |  التسلسل: {sequence}  |  " +
                $"السجين: {personName}  |  الرقم: {prisonerNumber}");

            // clear record fields only, keep dossier fields for next entry
            ClearRecordFields();

            // auto-suggest next sequence
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
            NotesBox.Text = string.Empty;
            PersonNameBox.Focus();
        }

        // ── VALIDATION: numbers only input ───────────

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