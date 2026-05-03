using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class AddEditCustomFieldDialog : Window
    {
        private readonly CustomFieldService _service;
        private readonly CustomField? _editField;
        private readonly bool _isEdit;

        // Local copy of options (persisted immediately for edit, deferred for new)
        private List<CustomFieldOption> _options = new();

        public AddEditCustomFieldDialog(CustomField? field = null)
        {
            InitializeComponent();
            _service = new CustomFieldService(App.Database);
            _editField = field;
            _isEdit = field != null;
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ─────────────────────────────────────────────────────────────

        private void Initialize()
        {
            if (_isEdit && _editField != null)
            {
                TitleText.Text = "تعديل حقل مخصص";
                ArabicLabelBox.Text = _editField.ArabicLabel;
                SortOrderBox.Text = _editField.SortOrder.ToString();
                SuggestionLimitBox.Text = _editField.SuggestionLimit.ToString();

                IsRequiredChk.IsChecked = _editField.IsRequired;
                IsActiveChk.IsChecked = _editField.IsActive;
                ShowInEntryChk.IsChecked = _editField.ShowInEntry;
                ShowInAllDataChk.IsChecked = _editField.ShowInAllData;
                ShowInReportsChk.IsChecked = _editField.ShowInReports;
                EnableStatisticsChk.IsChecked = _editField.EnableStatistics;
                AllowBulkUpdateChk.IsChecked = _editField.AllowBulkUpdate;

                // Match FieldType combo
                foreach (ComboBoxItem item in FieldTypeCombo.Items)
                {
                    if (item.Tag?.ToString() == _editField.FieldType)
                    {
                        FieldTypeCombo.SelectedItem = item;
                        break;
                    }
                }

                // Load options for choice fields
                if (_editField.FieldType is "SingleChoice" or "MultiChoice")
                {
                    _options = _service.GetFieldOptions(_editField.CustomFieldId);
                    RefreshOptionsList();
                }
            }
            else
            {
                TitleText.Text = "إضافة حقل مخصص";
                FieldTypeCombo.SelectedIndex = 0;
            }
        }

        // ── FIELD TYPE CHANGED ────────────────────────────────────────────────

        private void FieldTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? type = (FieldTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (type == null) return;

            SuggestionPanel.Visibility = type == FieldTypes.TextWithSuggestions
                ? Visibility.Visible : Visibility.Collapsed;

            OptionsPanel.Visibility = type is FieldTypes.SingleChoice or FieldTypes.MultiChoice
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── OPTIONS ───────────────────────────────────────────────────────────

        private void AddOption_Click(object sender, RoutedEventArgs e)
        {
            string val = NewOptionBox.Text.Trim();
            if (string.IsNullOrEmpty(val)) return;

            if (_options.Any(o => o.ArabicValue == val))
            {
                ShowError("هذا الخيار موجود مسبقاً.");
                return;
            }

            if (_isEdit && _editField != null)
            {
                // Save immediately for existing fields
                var err = _service.AddFieldOption(_editField.CustomFieldId, val);
                if (err != null) { ShowError(err); return; }
                _options = _service.GetFieldOptions(_editField.CustomFieldId);
            }
            else
            {
                // Buffer locally for new fields — will be saved after CreateField()
                _options.Add(new CustomFieldOption
                {
                    ArabicValue = val,
                    CustomFieldOptionId = -(_options.Count + 1) // temporary negative ID
                });
            }

            RefreshOptionsList();
            NewOptionBox.Text = string.Empty;
            HideError();
        }

        private void RemoveOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int optId) return;

            if (_isEdit && optId > 0)
                _service.DeleteFieldOption(optId);

            _options.RemoveAll(o => o.CustomFieldOptionId == optId);
            RefreshOptionsList();
        }

        private void RefreshOptionsList()
        {
            OptionsList.ItemsSource = null;
            OptionsList.ItemsSource = _options;
        }

        // ── SAVE ──────────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            HideError();

            string? type = (FieldTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(type))
            { ShowError("يرجى اختيار نوع الحقل."); return; }

            int sortOrder = int.TryParse(SortOrderBox.Text, out int so) ? so : 0;
            int suggLimit = int.TryParse(SuggestionLimitBox.Text, out int sl) ? sl : 0;

            if (_isEdit && _editField != null)
            {
                _editField.ArabicLabel = ArabicLabelBox.Text.Trim();
                _editField.FieldType = type;
                _editField.IsRequired = IsRequiredChk.IsChecked == true;
                _editField.IsActive = IsActiveChk.IsChecked == true;
                _editField.ShowInEntry = ShowInEntryChk.IsChecked == true;
                _editField.ShowInAllData = ShowInAllDataChk.IsChecked == true;
                _editField.ShowInReports = ShowInReportsChk.IsChecked == true;
                _editField.EnableStatistics = EnableStatisticsChk.IsChecked == true;
                _editField.AllowBulkUpdate = AllowBulkUpdateChk.IsChecked == true;
                _editField.SuggestionLimit = suggLimit;
                _editField.SortOrder = sortOrder;

                var err = _service.UpdateField(_editField);
                if (err != null) { ShowError(err); return; }
            }
            else
            {
                var newField = new CustomField
                {
                    ArabicLabel = ArabicLabelBox.Text.Trim(),
                    FieldType = type,
                    IsRequired = IsRequiredChk.IsChecked == true,
                    IsActive = IsActiveChk.IsChecked == true,
                    ShowInEntry = ShowInEntryChk.IsChecked == true,
                    ShowInAllData = ShowInAllDataChk.IsChecked == true,
                    ShowInReports = ShowInReportsChk.IsChecked == true,
                    EnableStatistics = EnableStatisticsChk.IsChecked == true,
                    AllowBulkUpdate = AllowBulkUpdateChk.IsChecked == true,
                    SuggestionLimit = suggLimit,
                    SortOrder = sortOrder
                };

                var err = _service.CreateField(newField);
                if (err != null) { ShowError(err); return; }

                // Now add buffered options
                if (_options.Count > 0)
                {
                    var allFields = _service.GetAllFields();
                    var created = allFields
                        .Where(f => f.ArabicLabel == newField.ArabicLabel)
                        .OrderByDescending(f => f.CustomFieldId)
                        .FirstOrDefault();

                    if (created != null)
                    {
                        foreach (var opt in _options)
                            _service.AddFieldOption(created.CustomFieldId, opt.ArabicValue);
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void HideError() => ErrorBorder.Visibility = Visibility.Collapsed;

        private void NumberOnly(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }
}