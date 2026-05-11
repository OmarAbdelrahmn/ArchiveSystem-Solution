using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Dapper;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    public partial class EntryPage : Page
    {
        private readonly DossierService _dossierService;
        private readonly RecordService _recordService;
        private readonly CustomFieldService _customFieldService;
        private Key _saveKey = Key.F5;
        private Key _clearKey = Key.F6;
        private int _currentDossierId = 0;
        private bool _isExistingDossier = false;

        private System.Windows.Threading.DispatcherTimer? _dossierCheckTimer;

        // ── Custom-field runtime state ────────────────────────────────────────
        private List<CustomField> _customFields = new();

        /// <summary>
        /// Maps CustomFieldId → the primary input control for that field.
        /// </summary>
        private readonly Dictionary<int, FrameworkElement> _customInputs = new();

        // ─────────────────────────────────────────────────────────────────────

        public EntryPage()
        {
            InitializeComponent();
            _dossierService = new DossierService(App.Database);
            _recordService = new RecordService(App.Database);
            _customFieldService = new CustomFieldService(App.Database);

            Loaded += (s, e) =>
            {
                if (PermissionHelper.DenyPage(this, Permissions.AddRecord)) return;
                LoadShortcuts();          // ← add this first
                LoadCustomFields();
                SuggestDossierNumber();

                // ── Attach live-search popups to the two fixed fields ─────────
                AttachNameSuggestions();
                AttachPrisonerNumberSuggestions();
            };
        }

        private void LoadShortcuts()
        {
            try
            {
                using var conn = App.Database.CreateConnection();
                string saveStr = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'EntrySaveKey'")
                    ?? "F5";
                string clearStr = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'EntryClearKey'")
                    ?? "F6";

                if (Enum.TryParse<Key>(saveStr, out var sk)) _saveKey = sk;
                if (Enum.TryParse<Key>(clearStr, out var ck)) _clearKey = ck;
            }
            catch { /* keep defaults */ }

            // Update button tooltips so users can see the active shortcut
            UpdateButtonHints();

            // Register page-level shortcut handler (remove old one first)
            PreviewKeyDown -= EntryPage_PreviewKeyDown;
            PreviewKeyDown += EntryPage_PreviewKeyDown;
        }

        private void UpdateButtonHints()
        {
            // Find the Save and Clear buttons via their click handlers (they're
            // in the XAML — we just update their ToolTip text here)
            // The simplest way: walk the visual tree or use x:Name fields.
            // Since the buttons don't have x:Name, we can add them in XAML (see step 4).
            // For now, update tooltips via named references added in step 4:
            if (SaveRecordBtn != null)
                SaveRecordBtn.ToolTip = $"💾 حفظ ({_saveKey})";
            if (ClearRecordBtn != null)
                ClearRecordBtn.ToolTip = $"مسح ({_clearKey})";
        }

        private void EntryPage_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == _saveKey)
            {
                SaveRecord_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == _clearKey)
            {
                ClearRecord_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        // ══════════════════════════════════════════════════════════════════════
        // LIVE-SEARCH POPUP — PersonNameBox
        // ══════════════════════════════════════════════════════════════════════

        private void AttachNameSuggestions()
        {
            var popup = BuildSuggestionPopup(out var listBox);
            popup.PlacementTarget = PersonNameBox;

            void Refresh()
            {
                var q = PersonNameBox.Text.Trim();
                if (q.Length < 1) { popup.IsOpen = false; return; }

                var suggestions = _recordService.GetNameSuggestions(q, 10);
                listBox.Items.Clear();
                foreach (var s in suggestions)
                    listBox.Items.Add(new ListBoxItem
                    {
                        Content = s,
                        Padding = new Thickness(12, 7, 12, 7),
                        FontSize = 13
                    });

                popup.IsOpen = suggestions.Count > 0;
            }

            PersonNameBox.GotFocus += (_, _) => Refresh();
            PersonNameBox.TextChanged += (_, _) => { if (PersonNameBox.IsFocused) Refresh(); };
            PersonNameBox.LostFocus += (_, _) =>
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => { if (!listBox.IsKeyboardFocusWithin) popup.IsOpen = false; });

            PersonNameBox.PreviewKeyDown += (_, e) =>
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

            WireListBoxSelection(listBox, popup, PersonNameBox);

            // Add popup to the page's visual tree via a hidden container
            AttachPopupToPage(popup);
        }

        // ══════════════════════════════════════════════════════════════════════
        // LIVE-SEARCH POPUP — PrisonerNumberBox
        // ══════════════════════════════════════════════════════════════════════

        private void AttachPrisonerNumberSuggestions()
        {
            var popup = BuildSuggestionPopup(out var listBox);
            popup.PlacementTarget = PrisonerNumberBox;

            void Refresh()
            {
                var q = PrisonerNumberBox.Text.Trim();
                if (q.Length < 1) { popup.IsOpen = false; return; }

                var suggestions = _recordService.GetPrisonerNumberSuggestions(q, 10);
                listBox.Items.Clear();
                foreach (var s in suggestions)
                    listBox.Items.Add(new ListBoxItem
                    {
                        Content = s,
                        Padding = new Thickness(12, 7, 12, 7),
                        FontSize = 13
                    });

                popup.IsOpen = suggestions.Count > 0;
            }

            PrisonerNumberBox.GotFocus += (_, _) => Refresh();
            PrisonerNumberBox.TextChanged += (_, _) => { if (PrisonerNumberBox.IsFocused) Refresh(); };
            PrisonerNumberBox.LostFocus += (_, _) =>
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => { if (!listBox.IsKeyboardFocusWithin) popup.IsOpen = false; });

            PrisonerNumberBox.PreviewKeyDown += (_, e) =>
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

            WireListBoxSelection(listBox, popup, PrisonerNumberBox);
            AttachPopupToPage(popup);
        }



        // ══════════════════════════════════════════════════════════════════════
        // SHARED POPUP FACTORY
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a styled Popup + ListBox, returns the ListBox via out param.
        /// PlacementTarget must be set by the caller before opening.
        /// </summary>
        private static Popup BuildSuggestionPopup(out ListBox listBox)
        {
            listBox = new ListBox
            {
                MaxHeight = 220,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0, 0, 4, 4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    Opacity = 0.18,
                    ShadowDepth = 3,
                    Direction = 270
                },
                Child = listBox
            };

            return new Popup
            {
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                MinWidth = 260,
                MaxWidth = 520,
                Child = border
            };
        }

        /// <summary>
        /// Wires keyboard (Enter / Escape) and mouse selection on the popup ListBox
        /// so that clicking or pressing Enter fills <paramref name="target"/> and
        /// closes the popup.
        /// </summary>
        private static void WireListBoxSelection(ListBox listBox, Popup popup, TextBox target)
        {
            listBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && listBox.SelectedItem is ListBoxItem li)
                {
                    target.Text = li.Content?.ToString() ?? string.Empty;
                    target.CaretIndex = target.Text.Length;
                    popup.IsOpen = false;
                    target.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    target.Focus();
                    e.Handled = true;
                }
            };

            listBox.MouseLeftButtonUp += (_, _) =>
            {
                if (listBox.SelectedItem is ListBoxItem li)
                {
                    target.Text = li.Content?.ToString() ?? string.Empty;
                    target.CaretIndex = target.Text.Length;
                    popup.IsOpen = false;
                    target.Focus();
                }
            };
        }

        /// <summary>
        /// WPF Popup must be logically connected to the visual tree to position
        /// correctly.  We add it as a child of the page's root element.
        /// </summary>
        private void AttachPopupToPage(Popup popup)
        {
            // The Page content is a Grid; we can add the Popup directly.
            if (Content is Grid root)
                root.Children.Add(popup);
        }

        // ══════════════════════════════════════════════════════════════════════
        // CUSTOM FIELD BUILDER  (unchanged)
        // ══════════════════════════════════════════════════════════════════════

        private void LoadCustomFields()
        {
            _customFields = _customFieldService.GetActiveEntryFields();
            _customInputs.Clear();
            CustomFieldsPanel.Children.Clear();

            foreach (var cf in _customFields)
                CustomFieldsPanel.Children.Add(BuildFieldControl(cf));
        }

        private FrameworkElement BuildFieldControl(CustomField cf)
        {
            string hintSuffix = cf.IsRequired ? " *" : " (اختياري)";
            string hint = cf.ArabicLabel + hintSuffix;

            switch (cf.FieldType)
            {
                case FieldTypes.TextWithSuggestions:
                    return BuildSuggestionsInput(cf, hint);

                case FieldTypes.Number:
                    {
                        var tb = MakeOutlinedTextBox(hint);
                        tb.PreviewTextInput += (s, e) =>
                            e.Handled = !Regex.IsMatch(e.Text, @"^[\d\.\-]$");
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
                            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
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
                        combo.SelectedIndex = 0;
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
                        foreach (var opt in options)
                            checkWrap.Children.Add(new CheckBox
                            {
                                Content = opt.ArabicValue,
                                Margin = new Thickness(0, 0, 16, 6),
                                Tag = opt.ArabicValue
                            });
                        wrapper.Children.Add(checkWrap);
                        _customInputs[cf.CustomFieldId] = checkWrap;
                        return wrapper;
                    }

                default:
                    {
                        var tb = MakeOutlinedTextBox(hint);
                        _customInputs[cf.CustomFieldId] = tb;
                        return WrapWithMargin(tb);
                    }
            }
        }

        // ── TextWithSuggestions popup (custom fields) ─────────────────────────

        private Grid BuildSuggestionsInput(CustomField cf, string hint)
        {
            var container = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            var tb = MakeOutlinedTextBox(hint);
            container.Children.Add(tb);

            var popup = BuildSuggestionPopup(out var listBox);
            popup.PlacementTarget = tb;

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

            WireListBoxSelection(listBox, popup, tb);
            container.Children.Add(popup);
            return container;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private TextBox MakeOutlinedTextBox(string hint)
        {
            var tb = new TextBox();
            MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, hint);
            tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
            return tb;
        }

        private static FrameworkElement WrapWithMargin(FrameworkElement el)
        {
            el.Margin = new Thickness(0, 0, 0, 14);
            return el;
        }

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

        private void ClearCustomFields()
        {
            foreach (var (_, ctrl) in _customInputs)
            {
                switch (ctrl)
                {
                    case TextBox tb: tb.Text = string.Empty; break;
                    case CheckBox cb: cb.IsChecked = false; break;
                    case ComboBox combo: combo.SelectedIndex = 0; break;
                    case WrapPanel wp:
                        foreach (var chk in wp.Children.OfType<CheckBox>())
                            chk.IsChecked = false;
                        break;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // AUTO SUGGEST  (dossier number & sequence)
        // ══════════════════════════════════════════════════════════════════════

        private void SuggestDossierNumber()
        {
            int next = _dossierService.GetNextDossierNumber();
            DossierNumberBox.Text = next.ToString();
            DossierStatusText.Text = string.Empty;
            UnlockDossierFields();
        }

        private void AutoDossierNumber_Click(object sender, RoutedEventArgs e)
            => SuggestDossierNumber();

        private void AutoSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDossierId > 0)
            {
                SequenceBox.Text = _recordService.GetNextSequenceNumber(_currentDossierId).ToString();
                return;
            }

            if (int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            {
                var existing = _dossierService.GetDossierByNumber(dossierNumber);
                if (existing != null)
                {
                    _currentDossierId = existing.DossierId;
                    SequenceBox.Text = _recordService.GetNextSequenceNumber(_currentDossierId).ToString();
                    return;
                }
            }

            SequenceBox.Text = "1";
        }

        // ══════════════════════════════════════════════════════════════════════
        // REAL-TIME DOSSIER CHECK
        // ══════════════════════════════════════════════════════════════════════

        private void DossierNumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _dossierCheckTimer?.Stop();
            _dossierCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _dossierCheckTimer.Tick += (s, _) => { _dossierCheckTimer.Stop(); CheckDossierNumber(); };
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

                int currentCount = _recordService.GetRecordsByDossier(_currentDossierId).Count;
                int? expected = existing.ExpectedFileCount;
                string countInfo = expected.HasValue
                    ? $"{currentCount} من {expected} ملف"
                    : $"{currentCount} ملف مسجل";

                DossierStatusText.Text = $"✅ دوسية موجودة — {countInfo}";
                DossierStatusText.Foreground = new SolidColorBrush(Color.FromRgb(30, 120, 80));

                SequenceBox.Text = _recordService.GetNextSequenceNumber(_currentDossierId).ToString();
            }
            else
            {
                _currentDossierId = 0;
                _isExistingDossier = false;
                DossierStatusText.Text = "🆕 دوسية جديدة";
                DossierStatusText.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                UnlockDossierFields();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // LOCK / UNLOCK DOSSIER FIELDS
        // ══════════════════════════════════════════════════════════════════════

        private void LockDossierFields() => SetDossierFieldsLock(true, 0.6);
        private void UnlockDossierFields() => SetDossierFieldsLock(false, 1.0);

        private void SetDossierFieldsLock(bool readOnly, double opacity)
        {
            foreach (var tb in new[] { HijriMonthBox, HijriYearBox, ExpectedCountBox,
                                       HallwayBox, CabinetBox, ShelfBox })
            {
                tb.IsReadOnly = readOnly;
                tb.Opacity = opacity;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SAVE
        // ══════════════════════════════════════════════════════════════════════

        private void SaveRecord_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            if (!int.TryParse(DossierNumberBox.Text, out int dossierNumber))
            { ShowError("رقم الدوسية غير صحيح."); return; }

            if (!int.TryParse(SequenceBox.Text, out int sequence))
            { ShowError("رقم التسلسل غير صحيح."); return; }

            string personName = PersonNameBox.Text.Trim();
            string prisonerNumber = PrisonerNumberBox.Text.Trim();
            string? notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

            foreach (var cf in _customFields.Where(f => f.IsRequired))
            {
                string? val = GetCustomFieldValue(cf.CustomFieldId);
                if (string.IsNullOrWhiteSpace(val))
                { ShowError($"حقل '{cf.ArabicLabel}' مطلوب."); return; }
            }

            // ── Get or create dossier ──────────────────────────────────────
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

            var (recordError, newRecordId) = _recordService.AddRecord(
                _currentDossierId, sequence, personName, prisonerNumber, notes);

            if (recordError != null) { ShowError(recordError); return; }

            foreach (var cf in _customFields)
            {
                string? value = GetCustomFieldValue(cf.CustomFieldId);
                if (value != null)
                    _customFieldService.SaveFieldValue(newRecordId, cf.CustomFieldId, value);
            }

            int newCount = _recordService.GetRecordsByDossier(_currentDossierId).Count;
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
            SequenceBox.Text = _recordService.GetNextSequenceNumber(_currentDossierId).ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        // CLEAR
        // ══════════════════════════════════════════════════════════════════════

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
            ClearCustomFields();
            PersonNameBox.Focus();
        }

        // ══════════════════════════════════════════════════════════════════════
        // VALIDATION
        // ══════════════════════════════════════════════════════════════════════

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");

        // ══════════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ══════════════════════════════════════════════════════════════════════

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