using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;

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
        private readonly CustomFieldService _customFieldService;

        // Map: CustomFieldId → primary input control (TextBox, CheckBox, ComboBox, or WrapPanel for MultiChoice)
        private readonly Dictionary<int, FrameworkElement> _customInputs = new();

        public EditRecordDialog(
            Record record,
            List<CustomField> customFields,
            Dictionary<int, string?> existingValues)
        {
            InitializeComponent();
            _customFields = customFields;
            _customFieldService = new CustomFieldService(App.Database);

            // Pre-fill fixed fields
            SequenceBox.Text = record.SequenceNumber.ToString();
            PersonNameBox.Text = record.PersonName;
            PrisonerNumberBox.Text = record.PrisonerNumber;
            NotesBox.Text = record.Notes ?? string.Empty;

            BuildCustomFields(existingValues);
        }

        // ── FIELD BUILDER ─────────────────────────────────────────────────────

        private void BuildCustomFields(Dictionary<int, string?> existingValues)
        {
            CustomFieldsPanel.Children.Clear();
            _customInputs.Clear();

            foreach (var cf in _customFields)
            {
                existingValues.TryGetValue(cf.CustomFieldId, out var currentValue);
                var element = BuildFieldControl(cf, currentValue);
                CustomFieldsPanel.Children.Add(element);
            }
        }

        private FrameworkElement BuildFieldControl(CustomField cf, string? currentValue)
        {
            string hintSuffix = cf.IsRequired ? " *" : " (اختياري)";
            string hint = cf.ArabicLabel + hintSuffix;

            switch (cf.FieldType)
            {
                case FieldTypes.TextWithSuggestions:
                    return BuildSuggestionsInput(cf, hint, currentValue);

                case FieldTypes.Number:
                    {
                        var tb = MakeOutlinedTextBox(hint, currentValue);
                        tb.PreviewTextInput += (s, e) =>
                            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[\d\.\-]$");
                        _customInputs[cf.CustomFieldId] = tb;
                        return WrapWithMargin(tb);
                    }

                case FieldTypes.Boolean:
                    {
                        var cb = new CheckBox
                        {
                            Content = cf.ArabicLabel,
                            Margin = new Thickness(0, 0, 0, 14),
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                            IsChecked = currentValue == "true"
                        };
                        _customInputs[cf.CustomFieldId] = cb;
                        return cb;
                    }

                case FieldTypes.SingleChoice:
                    {
                        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
                        MaterialDesignThemes.Wpf.HintAssist.SetHint(combo, hint);
                        combo.Style = (Style)FindResource("MaterialDesignOutlinedComboBox");

                        combo.Items.Add(new ComboBoxItem { Content = "-- لا شيء --", Tag = "" });

                        var options = _customFieldService.GetFieldOptions(cf.CustomFieldId);
                        foreach (var opt in options)
                            combo.Items.Add(new ComboBoxItem { Content = opt.ArabicValue, Tag = opt.ArabicValue });

                        // Restore saved value
                        combo.SelectedIndex = 0;
                        if (!string.IsNullOrEmpty(currentValue))
                        {
                            foreach (ComboBoxItem item in combo.Items)
                            {
                                if (item.Tag?.ToString() == currentValue)
                                {
                                    combo.SelectedItem = item;
                                    break;
                                }
                            }
                        }

                        _customInputs[cf.CustomFieldId] = combo;
                        return combo;
                    }

                case FieldTypes.MultiChoice:
                    {
                        var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
                        wrapper.Children.Add(new TextBlock
                        {
                            Text = hint,
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                            Margin = new Thickness(0, 0, 0, 4)
                        });

                        var checkWrap = new WrapPanel { Orientation = Orientation.Horizontal };
                        var options = _customFieldService.GetFieldOptions(cf.CustomFieldId);

                        // Saved value is comma-joined
                        var selected = new HashSet<string>(
                            (currentValue ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

                        foreach (var opt in options)
                        {
                            var chk = new CheckBox
                            {
                                Content = opt.ArabicValue,
                                Margin = new Thickness(0, 0, 16, 6),
                                Tag = opt.ArabicValue,
                                IsChecked = selected.Contains(opt.ArabicValue)
                            };
                            checkWrap.Children.Add(chk);
                        }

                        wrapper.Children.Add(checkWrap);
                        _customInputs[cf.CustomFieldId] = checkWrap;
                        return wrapper;
                    }

                // Date / plain Text (default)
                default:
                    {
                        var tb = MakeOutlinedTextBox(hint, currentValue);
                        _customInputs[cf.CustomFieldId] = tb;
                        return WrapWithMargin(tb);
                    }
            }
        }

        // ── TextWithSuggestions popup ─────────────────────────────────────────

        private Grid BuildSuggestionsInput(CustomField cf, string hint, string? currentValue)
        {
            var container = new Grid { Margin = new Thickness(0, 0, 0, 14) };

            var tb = MakeOutlinedTextBox(hint, currentValue);
            container.Children.Add(tb);

            var popup = new Popup
            {
                PlacementTarget = tb,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                MaxWidth = 500,
                MinWidth = 280
            };

            var border = new System.Windows.Controls.Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0, 0, 4, 4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    Opacity = 0.15,
                    ShadowDepth = 3,
                    Direction = 270
                }
            };

            var listBox = new ListBox
            {
                MaxHeight = 180,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

            border.Child = listBox;
            popup.Child = border;

            _customInputs[cf.CustomFieldId] = tb;

            int suggLimit = cf.SuggestionLimit > 0 ? cf.SuggestionLimit : 8;

            void RefreshSuggestions()
            {
                var text = tb.Text.Trim();
                var all = _customFieldService.GetSuggestions(cf.CustomFieldId, suggLimit * 2);
                var filtered = string.IsNullOrEmpty(text)
                    ? all.Take(suggLimit).ToList()
                    : all.Where(s => s.Contains(text, StringComparison.OrdinalIgnoreCase))
                         .Take(suggLimit).ToList();

                listBox.Items.Clear();
                foreach (var s in filtered)
                    listBox.Items.Add(new ListBoxItem
                    {
                        Content = s,
                        Padding = new Thickness(12, 7, 12, 7),
                        FontSize = 13
                    });

                popup.IsOpen = filtered.Count > 0;
            }

            tb.GotFocus += (_, _) => RefreshSuggestions();
            tb.TextChanged += (_, _) => { if (tb.IsFocused) RefreshSuggestions(); };
            tb.LostFocus += (_, _) =>
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => { if (!listBox.IsKeyboardFocusWithin) popup.IsOpen = false; });

            tb.PreviewKeyDown += (_, e) =>
            {
                if (!popup.IsOpen) return;
                if (e.Key == Key.Down)
                {
                    listBox.Focus();
                    if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
            };

            listBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && listBox.SelectedItem is ListBoxItem li)
                {
                    tb.Text = li.Content?.ToString() ?? string.Empty;
                    tb.CaretIndex = tb.Text.Length;
                    popup.IsOpen = false;
                    tb.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape) { popup.IsOpen = false; tb.Focus(); e.Handled = true; }
            };

            listBox.MouseLeftButtonUp += (_, _) =>
            {
                if (listBox.SelectedItem is ListBoxItem li)
                {
                    tb.Text = li.Content?.ToString() ?? string.Empty;
                    tb.CaretIndex = tb.Text.Length;
                    popup.IsOpen = false;
                    tb.Focus();
                }
            };

            container.Children.Add(popup);
            return container;
        }

        // ── Value reader ──────────────────────────────────────────────────────

        private string? GetCustomFieldValue(int customFieldId)
        {
            if (!_customInputs.TryGetValue(customFieldId, out var ctrl)) return null;

            if (ctrl is TextBox tb)
                return string.IsNullOrWhiteSpace(tb.Text) ? null : tb.Text.Trim();

            if (ctrl is CheckBox cb)
                return cb.IsChecked == true ? "true" : null;

            if (ctrl is ComboBox combo)
            {
                if (combo.SelectedItem is ComboBoxItem ci
                    && ci.Tag is string v && !string.IsNullOrEmpty(v))
                    return v;
                return null;
            }

            if (ctrl is WrapPanel wp)
            {
                var vals = wp.Children.OfType<CheckBox>()
                             .Where(c => c.IsChecked == true)
                             .Select(c => c.Tag?.ToString() ?? string.Empty)
                             .Where(s => !string.IsNullOrEmpty(s))
                             .ToList();
                return vals.Count > 0 ? string.Join(",", vals) : null;
            }

            return null;
        }

        // ── SAVE ──────────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(PersonNameBox.Text))
            { ShowError("اسم السجين مطلوب."); return; }

            string pNum = PrisonerNumberBox.Text.Trim();
            if (pNum.Length != 10 || !pNum.All(char.IsDigit))
            { ShowError("رقم السجين يجب أن يكون 10 أرقام فقط."); return; }

            // Validate required custom fields
            foreach (var cf in _customFields.Where(f => f.IsRequired))
            {
                string? val = GetCustomFieldValue(cf.CustomFieldId);
                if (string.IsNullOrWhiteSpace(val))
                { ShowError($"حقل '{cf.ArabicLabel}' مطلوب."); return; }
            }

            PersonName = PersonNameBox.Text.Trim();
            PrisonerNumber = pNum;
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

            CustomFieldValues.Clear();
            foreach (var cf in _customFields)
                CustomFieldValues[cf.CustomFieldId] = GetCustomFieldValue(cf.CustomFieldId);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private TextBox MakeOutlinedTextBox(string hint, string? value = null)
        {
            var tb = new TextBox();
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
            tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            if (!string.IsNullOrEmpty(value)) tb.Text = value;
            return tb;
        }

        private static FrameworkElement WrapWithMargin(FrameworkElement el)
        {
            el.Margin = new Thickness(0, 0, 0, 14);
            return el;
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}