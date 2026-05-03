using System.Windows;
using System.Windows.Controls;
using ArchiveSystem.Core.Models;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class EditRecordDialog : Window
    {
        // Output properties read by caller
        public string PersonName { get; private set; } = string.Empty;
        public string PrisonerNumber { get; private set; } = string.Empty;
        public string? Notes { get; private set; }
        public Dictionary<int, string?> CustomFieldValues { get; private set; } = new();

        private readonly List<CustomField> _customFields;
        // Map: CustomFieldId → TextBox control (for reading back)
        private readonly Dictionary<int, TextBox> _customInputs = new();

        public EditRecordDialog(
            Record record,
            List<CustomField> customFields,
            Dictionary<int, string?> existingValues)
        {
            InitializeComponent();
            _customFields = customFields;

            // Pre-fill fields
            SequenceBox.Text = record.SequenceNumber.ToString();
            PersonNameBox.Text = record.PersonName;
            PrisonerNumberBox.Text = record.PrisonerNumber;
            NotesBox.Text = record.Notes ?? string.Empty;

            // Build custom field inputs
            BuildCustomFields(existingValues);
        }

        private void BuildCustomFields(Dictionary<int, string?> existingValues)
        {
            CustomFieldsPanel.Children.Clear();
            _customInputs.Clear();

            foreach (var cf in _customFields)
            {
                var tb = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, 14)
                };
                MaterialDesignThemes.Wpf.HintAssist.SetHint(
                    tb, cf.ArabicLabel + (cf.IsRequired ? " *" : " (اختياري)"));
                tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");

                if (existingValues.TryGetValue(cf.CustomFieldId, out var val))
                    tb.Text = val ?? string.Empty;

                _customInputs[cf.CustomFieldId] = tb;
                CustomFieldsPanel.Children.Add(tb);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(PersonNameBox.Text))
            {
                ShowError("اسم السجين مطلوب."); return;
            }

            string pNum = PrisonerNumberBox.Text.Trim();
            if (pNum.Length != 10 || !pNum.All(char.IsDigit))
            {
                ShowError("رقم السجين يجب أن يكون 10 أرقام فقط."); return;
            }

            // Validate required custom fields
            foreach (var cf in _customFields.Where(f => f.IsRequired))
            {
                if (_customInputs.TryGetValue(cf.CustomFieldId, out var tb) &&
                    string.IsNullOrWhiteSpace(tb.Text))
                {
                    ShowError($"حقل '{cf.ArabicLabel}' مطلوب."); return;
                }
            }

            PersonName = PersonNameBox.Text.Trim();
            PrisonerNumber = pNum;
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

            CustomFieldValues.Clear();
            foreach (var (cfId, tb) in _customInputs)
                CustomFieldValues[cfId] = string.IsNullOrWhiteSpace(tb.Text)
                    ? null : tb.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}