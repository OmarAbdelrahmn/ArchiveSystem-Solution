using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using Microsoft.Win32;

namespace ArchiveSystem.Views.Pages
{
    public partial class AllDataPage : Page
    {
        private readonly AllDataService _service;

        private AllDataFilter _filter = new();
        private int _totalCount = 0;
        private bool _sortAsc = true;

        private List<CustomField> _customFields = new();

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
        }

        private void AddCustomFieldColumns()
        {
            // keep built-in 7 columns (added Status column)
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
                var sp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 10, 6) };

                var tb = new TextBox { Width = 130, Height = 40, Tag = cf.CustomFieldId };
                MaterialDesignThemes.Wpf.HintAssist.SetHint(tb, cf.ArabicLabel);
                tb.Style = (Style)FindResource("MaterialDesignOutlinedTextBox");
                tb.TextChanged += CustomFilter_Changed;

                sp.Children.Add(tb);
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
            ColorDeletedRows(result.Items);

            ResultCountText.Text = _totalCount == 0
                ? "لا توجد نتائج تطابق شروط الفرز الحالية."
                : $"{_totalCount:N0} ملف يطابق شروط الفرز الحالية";

            PageText.Text = $"صفحة {result.Page} من {result.TotalPages}  |  عرض {result.Items.Count} من {_totalCount:N0}";
        }

        // Give deleted rows a reddish tint via row style after binding
        private void ColorDeletedRows(List<AllDataRow> rows)
        {
            // Applied via DataGrid.LoadingRow event (simpler approach)
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is AllDataRow row && row.DeletedAt != null)
                e.Row.Background = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50));
            else if (e.Row.Item is AllDataRow)
                e.Row.Background = null; // let alternating style take over
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
                if (sp.Children[0] is not TextBox tb) continue;
                if (tb.Tag is not int cfId) continue;
                if (!string.IsNullOrWhiteSpace(tb.Text))
                    _filter.CustomFieldFilters[cfId] = tb.Text.Trim();
            }

            if (SortCombo.SelectedItem is ComboBoxItem si && si.Tag is string col)
                _filter.SortColumn = col;

            _filter.SortAscending = _sortAsc;
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
            StatusCombo.SelectedIndex = 0; // back to "Active"

            foreach (StackPanel sp in CustomFilterPanel.Items)
                if (sp.Children[0] is TextBox tb) tb.Text = string.Empty;

            _filter.Page = 1;
            Load();
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
            var selected = DataGrid.SelectedItems
                .OfType<AllDataRow>()
                .Where(r => r.DeletedAt == null) // only active records
                .Select(r => r.RecordId)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "يرجى تحديد سجلات نشطة من الجدول أولاً.\n\nاستخدم Ctrl+Click أو Shift+Click لتحديد أكثر من سجل.",
                    "تعبئة جماعية", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new BulkFillDialog(selected) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) Load();
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
                "رقم الدوسية","التسلسل","اسم السجين","رقم السجين",
                "الشهر الهجري","السنة الهجرية","الموقع","الحالة","آخر تعديل"
            };
            foreach (var cf in _customFields) headers.Add(cf.ArabicLabel);
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
                    cells.Add(CsvEscape(row.CustomValues.TryGetValue(cf.CustomFieldId, out var v) ? v ?? "" : ""));

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