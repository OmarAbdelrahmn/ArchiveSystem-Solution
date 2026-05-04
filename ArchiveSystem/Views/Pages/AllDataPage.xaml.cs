using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    public partial class AllDataPage : Page
    {
        private readonly AllDataService _service;

        private AllDataFilter _filter = new();
        private int _totalCount = 0;
        private bool _sortAsc = true;

        private List<CustomField> _customFields = new();

        // Tracks IDs when the user chooses "select all filtered results"
        private List<int>? _allFilteredIds = null;
        private int _activeFilteredCount = 0;       // ← ADD


        private System.Windows.Threading.DispatcherTimer? _debounce;

        public AllDataPage()
        {
            InitializeComponent();
            _service = new AllDataService(App.Database);
            SortCombo.SelectedIndex = 0;
            MonthCombo.SelectedIndex = 0;
            StatusCombo.SelectedIndex = 0; // "نشط" (Active) by default
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ──────────────────────────────────────────────────────────────

        private void Initialize()
        {
            if (PermissionHelper.DenyPage(this, Permissions.SearchRecords)) return;

            var years = _service.GetDistinctYears();
            YearCombo.Items.Clear();
            YearCombo.Items.Add(new ComboBoxItem { Content = "الكل", Tag = 0 });
            foreach (var y in years)
                YearCombo.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
            YearCombo.SelectedIndex = 0;

            _customFields = _service.GetAllDataCustomFields();
            AddCustomFieldColumns();
            AddCustomFilterInputs();

            Load();

            PermissionHelper.Apply(BulkFillBtn, Permissions.EditRecord, hideInstead: true);
            PermissionHelper.Apply(ExportBtn, Permissions.SearchRecords, hideInstead: true);
        }

        private void AddCustomFieldColumns()
        {
            // Keep built-in 7 columns, remove any previously injected custom columns
            while (DataGrid.Columns.Count > 7)
                DataGrid.Columns.RemoveAt(DataGrid.Columns.Count - 1);

            foreach (var cf in _customFields)
            {
                var col = new DataGridTextColumn
                {
                    Header = cf.ArabicLabel,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    Binding = new System.Windows.Data.Binding($"CustomValues[{cf.CustomFieldId}]")
                    {
                        TargetNullValue = string.Empty,
                        FallbackValue = string.Empty
                    }
                };
                DataGrid.Columns.Add(col);
            }
        }

        private void AddCustomFilterInputs()
        {
            CustomFilterPanel.Items.Clear();

            foreach (var cf in _customFields)
            {
                // Outer stack so the label, textbox, and toggle sit together
                var sp = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 0, 10, 6),
                    Tag = cf.CustomFieldId   // store field id on the container
                };

                // ── Text input ────────────────────────────────────────────────
                var tb = new TextBox
                {
                    Width = 130,
                    Height = 40,
                    Tag = cf.CustomFieldId
                };
                MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, cf.ArabicLabel);
                tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
                tb.TextChanged += CustomFilter_Changed;

                // ── "فارغ" toggle button ──────────────────────────────────────
                var emptyBtn = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = "فارغ",
                    Width = 130,
                    Height = 28,
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0),
                    Tag = cf.CustomFieldId,
                    ToolTip = "عرض السجلات التي لم تُعبَّأ فيها هذا الحقل"
                };

                // Style it to look like a flat chip
                emptyBtn.Style = (Style)FindResource("MaterialDesignFlatButton");
                emptyBtn.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#C62828"));

                emptyBtn.Checked += (s, e) =>
                {
                    tb.IsEnabled = false;
                    tb.Text = string.Empty;   // clear text so it doesn't conflict
                    StartDebounce();
                };
                emptyBtn.Unchecked += (s, e) =>
                {
                    tb.IsEnabled = true;
                    StartDebounce();
                };

                sp.Children.Add(tb);
                sp.Children.Add(emptyBtn);
                CustomFilterPanel.Items.Add(sp);
            }
        }

        // ── LOAD ──────────────────────────────────────────────────────────────

        private void Load()
        {
            BuildFilter();
            var result = _service.GetFiltered(_filter);
            _totalCount = result.TotalCount;

            DataGrid.ItemsSource = result.Items;

            ResultCountText.Text = _totalCount == 0
                ? "لا توجد نتائج تطابق شروط الفرز الحالية."
                : $"{_totalCount:N0} ملف يطابق شروط الفرز الحالية";

            PageText.Text = $"صفحة {result.Page} من {result.TotalPages}  |  عرض {result.Items.Count} من {_totalCount:N0}";


            // Reset the "select all" checkbox whenever the data reloads
            _allFilteredIds = null;
            SelectAllFilteredChk.IsChecked = false;
            UpdateSelectAllLabel();

            // AFTER
            bool filterActive = IsFilterActive();

            // Fetch the active-only count so the label is accurate regardless of StatusFilter
            _activeFilteredCount = filterActive
                ? _service.GetFilteredActiveCount(_filter)
                : 0;

            SelectAllBorder.Visibility = filterActive && _activeFilteredCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Reset the "select all" checkbox whenever the data reloads
            _allFilteredIds = null;
            SelectAllFilteredChk.IsChecked = false;
            UpdateSelectAllLabel();
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is AllDataRow row && row.DeletedAt != null)
                e.Row.Background = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50));
            else if (e.Row.Item is AllDataRow)
                e.Row.Background = null; // let alternating row style take over
        }

        private void BuildFilter()
        {
            // Status
            _filter.StatusFilter = StatusCombo.SelectedItem is ComboBoxItem si2 && si2.Tag is string st
                ? st : "Active";

            _filter.NameQuery = NameBox.Text.Trim();
            _filter.PrisonerNumber = PNumBox.Text.Trim();
            _filter.DossierNumber = int.TryParse(DossierNumBox.Text, out int dn) ? dn : null;
            _filter.HijriYear = YearCombo.SelectedItem is ComboBoxItem yi && yi.Tag is int y && y > 0 ? y : null;
            _filter.HijriMonth = MonthCombo.SelectedItem is ComboBoxItem mi && mi.Tag is int m && m > 0 ? m : null;
            _filter.HallwayNumber = int.TryParse(HallwayBox.Text, out int hw) ? hw : null;
            _filter.CabinetNumber = int.TryParse(CabinetBox.Text, out int cab) ? cab : null;
            _filter.ShelfNumber = int.TryParse(ShelfBox.Text, out int sh) ? sh : null;
            _filter.CustomFieldFilters.Clear();
            foreach (StackPanel sp in CustomFilterPanel.Items)
            {
                if (sp.Tag is not int cfId) continue;

                // Child 0 = TextBox, Child 1 = ToggleButton
                var tb = sp.Children.Count > 0
                                    ? sp.Children[0] as TextBox : null;
                var emptyBtn = sp.Children.Count > 1
                                    ? sp.Children[1] as System.Windows.Controls.Primitives.ToggleButton
                                    : null;

                if (emptyBtn?.IsChecked == true)
                {
                    // User wants records where this field is empty
                    _filter.CustomFieldFilters[cfId] = "__EMPTY__";
                }
                else if (tb != null && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    _filter.CustomFieldFilters[cfId] = tb.Text.Trim();
                }
            }

            if (SortCombo.SelectedItem is ComboBoxItem si && si.Tag is string col)
                _filter.SortColumn = col;

            _filter.SortAscending = _sortAsc;
        }

        /// <summary>Returns true when any filter field has a non-default value.</summary>
        private bool IsFilterActive()
        {
            if (!string.IsNullOrWhiteSpace(NameBox.Text)) return true;
            if (!string.IsNullOrWhiteSpace(PNumBox.Text)) return true;
            if (!string.IsNullOrWhiteSpace(DossierNumBox.Text)) return true;
            if (!string.IsNullOrWhiteSpace(HallwayBox.Text)) return true;
            if (!string.IsNullOrWhiteSpace(CabinetBox.Text)) return true;
            if (!string.IsNullOrWhiteSpace(ShelfBox.Text)) return true;
            if (YearCombo.SelectedItem is ComboBoxItem yi && yi.Tag is int y && y > 0) return true;
            if (MonthCombo.SelectedItem is ComboBoxItem mi && mi.Tag is int m && m > 0) return true;
            foreach (StackPanel sp in CustomFilterPanel.Items)
                if (sp.Children[0] is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) return true;

            return false;
        }

        // ── FILTER EVENTS (debounced) ─────────────────────────────────────────

        private void Filter_Changed(object sender, RoutedEventArgs e) => StartDebounce();
        private void CustomFilter_Changed(object sender, TextChangedEventArgs e) => StartDebounce();

        private void StartDebounce()
        {
            _debounce?.Stop();
            _debounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _debounce.Tick += (s, _) =>
            {
                _debounce.Stop();
                _filter.Page = 1;
                Load();
            };
            _debounce.Start();
        }

        private void ResetFilter_Click(object sender, RoutedEventArgs e)
        {
            NameBox.Text = string.Empty;
            PNumBox.Text = string.Empty;
            DossierNumBox.Text = string.Empty;
            HallwayBox.Text = string.Empty;
            CabinetBox.Text = string.Empty;
            ShelfBox.Text = string.Empty;
            YearCombo.SelectedIndex = 0;
            MonthCombo.SelectedIndex = 0;
            StatusCombo.SelectedIndex = 0;

            foreach (StackPanel sp in CustomFilterPanel.Items)
            {
                if (sp.Children.Count > 0 && sp.Children[0] is TextBox tb)
                {
                    tb.IsEnabled = true;
                    tb.Text = string.Empty;
                }
                if (sp.Children.Count > 1
                    && sp.Children[1] is System.Windows.Controls.Primitives.ToggleButton btn)
                {
                    btn.IsChecked = false;
                }
            }

            _filter.Page = 1;
            Load();
        }

        // ── SELECT ALL FILTERED ───────────────────────────────────────────────

        private void SelectAllFiltered_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = SelectAllFilteredChk.IsChecked == true;

            if (isChecked)
            {
                // Fetch every matching active record ID across all pages
                BuildFilter();
                _allFilteredIds = _service.GetFilteredIds(_filter);
            }
            else
            {
                _allFilteredIds = null;
            }

            UpdateSelectAllLabel();
        }

        private void UpdateSelectAllLabel()
        {
            // Always show the active-only count — GetFilteredIds forces StatusFilter = "Active",
            // so the label must reflect that, not _totalCount which may include deleted rows.
            int count = _allFilteredIds?.Count ?? _activeFilteredCount;
            SelectAllFilteredChk.Tag = SelectAllFilteredChk.IsChecked == true
                ? $"تم تحديد كل السجلات النشطة ({count:N0})"
                : $"تحديد كل السجلات النشطة ({_activeFilteredCount:N0})";
        }

        // ── SORT ─────────────────────────────────────────────────────────────

        private void Sort_Changed(object sender, SelectionChangedEventArgs e)
        {
            _filter.Page = 1;
            Load();
        }

        private void SortDir_Click(object sender, RoutedEventArgs e)
        {
            _sortAsc = !_sortAsc;
            SortDirBtn.Content = _sortAsc ? "↑ تصاعدي" : "↓ تنازلي";
            _filter.Page = 1;
            Load();
        }

        // ── PAGER ─────────────────────────────────────────────────────────────

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_filter.Page <= 1) return;
            _filter.Page--;
            Load();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            int maxPage = (int)Math.Ceiling((double)_totalCount / _filter.PageSize);
            if (_filter.Page >= maxPage) return;
            _filter.Page++;
            Load();
        }

        // ── BULK FILL ─────────────────────────────────────────────────────────

        private void BulkFill_Click(object sender, RoutedEventArgs e)
        {
            List<int> targetIds;

            if (_allFilteredIds != null && _allFilteredIds.Count > 0)
            {
                // User chose "select all filtered results" — use the pre-fetched IDs
                targetIds = _allFilteredIds;
            }
            else
            {
                // Fall back to whatever rows are selected in the visible DataGrid
                targetIds = DataGrid.SelectedItems
                    .OfType<AllDataRow>()
                    .Where(r => r.DeletedAt == null) // only active records
                    .Select(r => r.RecordId)
                    .ToList();
            }

            if (targetIds.Count == 0)
            {
                MessageBox.Show(
                    "يرجى تحديد سجلات نشطة من الجدول أولاً.\n\n" +
                    "استخدم Ctrl+Click أو Shift+Click لتحديد أكثر من سجل،\n" +
                    "أو استخدم خيار \"تحديد كل النتائج\" للتعبئة الجماعية الكاملة.",
                    "تعبئة جماعية", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Build a human-readable summary of the active filter
            string? filterSummary = BuildFilterSummary();

            var dialog = new BulkFillDialog(targetIds, filterSummary) { Owner = Window.GetWindow(this) };

            if (dialog.ShowDialog() == true) Load();
        }

        private string? BuildFilterSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(NameBox.Text)) parts.Add($"الاسم: {NameBox.Text.Trim()}");
            if (!string.IsNullOrWhiteSpace(PNumBox.Text)) parts.Add($"رقم السجين: {PNumBox.Text.Trim()}");
            if (!string.IsNullOrWhiteSpace(DossierNumBox.Text)) parts.Add($"الدوسية: {DossierNumBox.Text.Trim()}");
            if (!string.IsNullOrWhiteSpace(HallwayBox.Text)) parts.Add($"ممر: {HallwayBox.Text.Trim()}");
            if (YearCombo.SelectedItem is ComboBoxItem yi && yi.Tag is int y && y > 0)
                parts.Add($"سنة: {y}هـ");
            if (MonthCombo.SelectedItem is ComboBoxItem mi && mi.Tag is int m && m > 0)
                parts.Add($"شهر: {m}");
            foreach (StackPanel sp in CustomFilterPanel.Items)
                if (sp.Children[0] is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                    parts.Add($"حقل مخصص: {tb.Text.Trim()}");
            return parts.Count > 0 ? string.Join(" | ", parts) : null;
        }

        // ── CSV EXPORT ────────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"archive_export_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var rows = _service.GetAllForExport(_filter);
            var sb = new StringBuilder();

            var headers = new List<string>
            {
                "رقم الدوسية", "التسلسل", "اسم السجين", "رقم السجين",
                "الشهر الهجري", "السنة الهجرية", "الموقع", "الحالة", "آخر تعديل"
            };
            foreach (var cf in _customFields)
                headers.Add(cf.ArabicLabel);

            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var row in rows)
            {
                var cells = new List<string>
                {
                    row.DossierNumber.ToString(),
                    row.SequenceNumber.ToString(),
                    CsvEscape(row.PersonName),
                    row.PrisonerNumber,
                    row.HijriMonth.ToString(),
                    row.HijriYear.ToString(),
                    CsvEscape(row.LocationDisplay),
                    CsvEscape(row.StatusDisplay),
                    CsvEscape(row.UpdatedAt ?? row.CreatedAt ?? "")
                };
                foreach (var cf in _customFields)
                    cells.Add(CsvEscape(
                        row.CustomValues.TryGetValue(cf.CustomFieldId, out var v) ? v ?? "" : ""));

                sb.AppendLine(string.Join(",", cells));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"تم التصدير بنجاح:\n{dlg.FileName}",
                "تصدير CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string CsvEscape(string? s)
        {
            s ??= string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        // ── INPUT VALIDATION ──────────────────────────────────────────────────

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }
    }
}