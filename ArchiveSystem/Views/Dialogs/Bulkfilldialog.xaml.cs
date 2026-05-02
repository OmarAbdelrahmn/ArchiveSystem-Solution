using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using DocumentFormat.OpenXml.InkML;
using System.Windows;
using System.Windows.Controls;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class BulkFillDialog : Window
    {
        private readonly AllDataService _service;
        private readonly List<int> _recordIds;
        private List<CustomField> _fields = new();

        public BulkFillDialog(List<int> recordIds)
        {
            InitializeComponent();
            _service = new AllDataService(App.Database);
            _recordIds = recordIds;

            CountText.Text = $"سيتم تعبئة الحقل لعدد {recordIds.Count} سجل محدد.";
            Loaded += (s, e) => LoadFields();
        }

        private void LoadFields()
        {
            _fields = _service.GetAllDataCustomFields()
                .Where(f => f.AllowBulkUpdate).ToList();
            FieldCombo.ItemsSource = _fields;
            if (_fields.Count > 0)
                FieldCombo.SelectedIndex = 0;
        }

        private void FieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadSuggestions();
        }

        private void LoadSuggestions()
        {
            if (FieldCombo.SelectedItem is not CustomField field) return;
            if (field.FieldType != "TextWithSuggestions")
            {
                SuggestionsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var suggestions = _service.GetRecentSuggestions(
                field.CustomFieldId, field.SuggestionLimit > 0 ? field.SuggestionLimit : 8);

            if (suggestions.Count > 0)
            {
                SuggestionsList.ItemsSource = suggestions;
                SuggestionsBorder.Visibility = Visibility.Visible;
            }
            else
            {
                SuggestionsBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string val)
                ValueBox.Text = val;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (FieldCombo.SelectedItem is not CustomField field)
            {
                ShowError("يرجى اختيار الحقل.");
                return;
            }

            string? value = string.IsNullOrWhiteSpace(ValueBox.Text)
                ? null : ValueBox.Text.Trim();

            // Confirm
            string confirmMsg = value == null
                ? $"سيتم مسح قيمة حقل '{field.ArabicLabel}' من عدد {_recordIds.Count} سجل. هل تريد المتابعة؟"
                : $"سيتم تعبئة حقل '{field.ArabicLabel}' بـ '{value}' لعدد {_recordIds.Count} سجل. هل تريد المتابعة؟";

            var result = MessageBox.Show(confirmMsg, "تأكيد التعبئة الجماعية",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var (error, count) = _service.BulkFillCustomField(
                _recordIds, field.CustomFieldId, value);

            if (error != null) { ShowError(error); return; }

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