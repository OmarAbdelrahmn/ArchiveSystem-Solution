using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ArchiveSystem.Views.Pages
{
    public partial class BulkFillHistoryPage : Page
    {
        private readonly AllDataService _service;
        private List<BulkFillBatchRow> _allRows = new();

        public BulkFillHistoryPage()
        {
            InitializeComponent();
            _service = new AllDataService(App.Database);
            Loaded += (s, e) => Initialize();
        }

        private void Initialize()
        {
            if (PermissionHelper.DenyPage(this, Permissions.ViewAuditLog)) return;
            LoadAll();
            PopulateFieldFilter();
        }

        private void LoadAll()
        {
            _allRows = _service.GetBulkFillHistory(500);
            ApplyFilter();
        }

        private void PopulateFieldFilter()
        {
            var fields = _allRows
                .Select(r => r.FieldLabel)
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            FieldCombo.Items.Clear();
            FieldCombo.Items.Add(new ComboBoxItem { Content = "كل الحقول", Tag = "" });
            foreach (var f in fields)
                FieldCombo.Items.Add(new ComboBoxItem { Content = f, Tag = f });
            FieldCombo.SelectedIndex = 0;
        }

        // ── FILTER ────────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            var rows = _allRows.AsEnumerable();

            // Field filter
            if (FieldCombo.SelectedItem is ComboBoxItem fc
                && fc.Tag is string field && !string.IsNullOrEmpty(field))
                rows = rows.Where(r => r.FieldLabel == field);

            // User filter
            if (!string.IsNullOrWhiteSpace(UserBox.Text))
                rows = rows.Where(r =>
                    r.ExecutedByName != null &&
                    r.ExecutedByName.Contains(UserBox.Text.Trim(),
                        StringComparison.OrdinalIgnoreCase));

            // Date from
            if (!string.IsNullOrWhiteSpace(DateFromBox.Text))
                rows = rows.Where(r =>
                    string.Compare(r.ExecutedAt, DateFromBox.Text.Trim(),
                        StringComparison.Ordinal) >= 0);

            // Date to (inclusive day)
            if (!string.IsNullOrWhiteSpace(DateToBox.Text)
                && DateTime.TryParse(DateToBox.Text.Trim(), out var toDate))
            {
                string toStr = toDate.AddDays(1).ToString("yyyy-MM-dd");
                rows = rows.Where(r =>
                    string.Compare(r.ExecutedAt, toStr,
                        StringComparison.Ordinal) < 0);
            }

            var filtered = rows.ToList();
            HistoryGrid.ItemsSource = filtered;

            int totalRecords = filtered.Sum(r => r.RecordCount);
            SummaryText.Text = filtered.Count == 0
                ? "لا توجد عمليات تعبئة جماعية."
                : $"{filtered.Count} عملية تعبئة  |  إجمالي السجلات المتأثرة: {totalRecords:N0}";
        }

        private System.Windows.Threading.DispatcherTimer? _debounce;

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _debounce?.Stop();
            _debounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };
            _debounce.Start();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            FieldCombo.SelectedIndex = 0;
            UserBox.Text = string.Empty;
            DateFromBox.Text = string.Empty;
            DateToBox.Text = string.Empty;
            ApplyFilter();
        }

        // ── CSV EXPORT ────────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"bulk_fill_history_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var rows = HistoryGrid.ItemsSource as IEnumerable<BulkFillBatchRow>
                       ?? _allRows;

            var sb = new StringBuilder();
            sb.AppendLine("التاريخ,المستخدم,الحقل,القيمة الجديدة,عدد السجلات");

            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Esc(r.ExecutedAt),
                    Esc(r.ExecutedByName ?? ""),
                    Esc(r.FieldLabel),
                    Esc(r.ValueDisplay),
                    r.RecordCount.ToString()
                }));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"تم التصدير:\n{dlg.FileName}",
                "تصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string Esc(string? s)
        {
            s ??= "";
            return s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;
        }
    }
}