using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    // ── View-model that augments WeeklyCount with a pre-calculated bar width ──
    // BarWidth is a pixel value (0-200) scaled to the maximum count in the
    // current data set.  The bar chart ItemsControl binds directly to this.
    internal class WeeklyBarRow
    {
        public int Year { get; init; }
        public int Week { get; init; }
        public int Count { get; init; }
        public double BarWidth { get; init; }   // pixels, 0-200
        public string Label => $"{Year} - أسبوع {Week:D2}";
    }

    public partial class StatisticsPage : Page
    {
        private readonly StatisticsService _service;
        private int _totalDossiers = 1; // avoid div-by-zero

        public StatisticsPage()
        {
            InitializeComponent();
            _service = new StatisticsService(App.Database);
            Loaded += (s, e) => LoadAll();
        }

        private void LoadAll()
        {
            if (PermissionHelper.DenyPage(this, Permissions.ViewStatistics)) return;
            LoadSummaryCards();
            LoadYearFilter();       // Hijri year filter for monthly grid
            LoadMonthly();
            LoadCompletion();
            LoadLocations();
            LoadWeeklyYearFilter(); // Gregorian year filter for weekly grid
            LoadWeekly();
            LoadCustomFieldSelector();
        }

        // ── SUMMARY CARDS ────────────────────────────────────────────────────

        private void LoadSummaryCards()
        {
            var s = _service.GetSummary();
            _totalDossiers = s.TotalDossiers > 0 ? s.TotalDossiers : 1;

            // after existing 4 cards:
            double avg = _service.GetAverageDailyEntries();
            SummaryCards.Children.Add(MakeCard("متوسط الإدخال اليومي", avg.ToString("F1"), "#00695C", "📈"));

            SummaryCards.Children.Clear();
            SummaryCards.Children.Add(MakeCard("إجمالي الدوسيات", s.TotalDossiers.ToString("N0"), "#1a7a60", "📁"));
            SummaryCards.Children.Add(MakeCard("إجمالي السجلات", s.TotalRecords.ToString("N0"), "#1565C0", "👤"));
            SummaryCards.Children.Add(MakeCard("مُضاف اليوم", s.RecordsToday.ToString("N0"), "#6A1B9A", "📅"));
            SummaryCards.Children.Add(MakeCard("مُضاف هذا الشهر", s.RecordsThisMonth.ToString("N0"), "#E65100", "📆"));
        }

        private static Border MakeCard(string label, string value, string hexColor, string icon)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);

            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 14, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 6,
                    Opacity = 0.08,
                    ShadowDepth = 2
                }
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = icon, FontSize = 26, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            });

            card.Child = sp;
            return card;
        }

        // ── MONTHLY BREAKDOWN ────────────────────────────────────────────────

        private void LoadYearFilter()
        {
            var years = new StatisticsService(App.Database)
                .GetMonthlyBreakdown(topN: 200)
                .Select(m => m.HijriYear)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            MonthlyYearCombo.Items.Clear();
            MonthlyYearCombo.Items.Add(new ComboBoxItem { Content = "كل السنوات", Tag = 0 });
            foreach (var y in years)
                MonthlyYearCombo.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });

            MonthlyYearCombo.SelectedIndex = 0;
        }

        private void LoadMonthly(int? filterYear = null)
        {
            var data = _service.GetMonthlyBreakdown(filterYear, topN: 24);
            MonthlyGrid.ItemsSource = data;
        }

        private void MonthlyYear_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (MonthlyYearCombo.SelectedItem is not ComboBoxItem ci) return;
            int? year = ci.Tag is int y && y > 0 ? y : null;
            LoadMonthly(year);
        }

        // ── WEEKLY BREAKDOWN ─────────────────────────────────────────────────

        /// <summary>
        /// Populates the WeeklyYearCombo with the distinct Gregorian years that
        /// have records, plus an "all years" entry.
        /// </summary>
        private void LoadWeeklyYearFilter()
        {
            var years = _service.GetDistinctRecordYears();

            WeeklyYearCombo.Items.Clear();
            WeeklyYearCombo.Items.Add(new ComboBoxItem { Content = "كل السنوات", Tag = 0 });
            foreach (var y in years)
                WeeklyYearCombo.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });

            // Default to the current Gregorian year if it is in the list;
            // otherwise fall back to "all years".
            int currentYear = DateTime.UtcNow.Year;
            bool found = false;
            foreach (ComboBoxItem item in WeeklyYearCombo.Items)
            {
                if (item.Tag is int yr && yr == currentYear)
                {
                    WeeklyYearCombo.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found) WeeklyYearCombo.SelectedIndex = 0;
        }

        private void LoadWeekly(int? filterYear = null)
        {
            var raw = _service.GetWeeklyBreakdown(filterYear, topN: 16);

            if (raw.Count == 0)
            {
                WeeklyGrid.ItemsSource = null;
                WeeklyBarsPanel.ItemsSource = null;
                WeeklyTotalText.Text = "لا توجد بيانات";
                return;
            }

            // Total for the badge
            int total = raw.Sum(w => w.Count);
            WeeklyTotalText.Text = $"الإجمالي: {total:N0} سجل";

            // DataGrid source (plain WeeklyCount)
            WeeklyGrid.ItemsSource = raw;

            // Bar chart source — scale bar widths to the maximum count
            const double MaxBarPx = 200.0;
            int maxCount = raw.Max(w => w.Count);
            double scale = maxCount > 0 ? MaxBarPx / maxCount : 0;

            var barRows = raw.Select(w => new WeeklyBarRow
            {
                Year = w.Year,
                Week = w.Week,
                Count = w.Count,
                BarWidth = Math.Round(w.Count * scale, 1)
            }).ToList();

            WeeklyBarsPanel.ItemsSource = barRows;
        }

        private void WeeklyYear_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (WeeklyYearCombo.SelectedItem is not ComboBoxItem ci) return;
            int? year = ci.Tag is int y && y > 0 ? y : null;
            LoadWeekly(year);
        }

        // ── COMPLETION BARS ───────────────────────────────────────────────────

        private void LoadCompletion()
        {
            var (complete, incomplete, unknown) = _service.GetCompletionBreakdown();
            double total = complete + incomplete + unknown;
            if (total == 0) total = 1;
            double barWidth = 200;

            CompleteLabel.Text = $"مكتملة: {complete:N0}";
            IncompleteLabel.Text = $"غير مكتملة: {incomplete:N0}";
            UnknownLabel.Text = $"غير محددة: {unknown:N0}";

            CompleteBar.Width = barWidth * complete / total;
            IncompleteBar.Width = barWidth * incomplete / total;
            UnknownBar.Width = barWidth * unknown / total;
        }

        // ── LOCATIONS ─────────────────────────────────────────────────────────

        private void LoadLocations()
        {
            LocationGrid.ItemsSource = _service.GetLocationStats(10);
        }

        // ── CUSTOM FIELD STATS ────────────────────────────────────────────────

        private void LoadCustomFieldSelector()
        {
            var fields = _service.GetStatEnabledFields();
            if (fields.Count == 0)
            {
                CustomStatsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            CustomStatsBorder.Visibility = Visibility.Visible;
            CustomFieldCombo.ItemsSource = fields;
            CustomFieldCombo.SelectedIndex = 0;

            // Populate Hijri year filter for custom field stats
            var hijriYears = _service.GetMonthlyBreakdown(topN: 200)
                .Select(m => m.HijriYear)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            CustomFieldYearCombo.Items.Clear();
            CustomFieldYearCombo.Items.Add(new ComboBoxItem { Content = "كل السنوات", Tag = 0 });
            foreach (var y in hijriYears)
                CustomFieldYearCombo.Items.Add(new ComboBoxItem { Content = $"{y}هـ", Tag = y });
            CustomFieldYearCombo.SelectedIndex = 0;
        }

        private void CustomField_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CustomFieldCombo.SelectedValue is not int fieldId) return;

            int? hijriYear = null;
            if (CustomFieldYearCombo.SelectedItem is ComboBoxItem yi
                && yi.Tag is int y && y > 0)
                hijriYear = y;

            CustomStatsGrid.ItemsSource = _service.GetCustomFieldStats(fieldId, hijriYear: hijriYear);
        }

        // ── REFRESH ───────────────────────────────────────────────────────────

        private void Refresh_Click(object sender, RoutedEventArgs e)
            => LoadAll();
    }
}