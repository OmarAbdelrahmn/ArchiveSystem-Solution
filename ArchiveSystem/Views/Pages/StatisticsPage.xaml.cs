using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
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
            LoadYearFilter();
            LoadMonthly();
            LoadCompletion();
            LoadLocations();
            LoadCustomFieldSelector();
        }

        // ── SUMMARY CARDS ────────────────────────────────────────────────────

        private void LoadSummaryCards()
        {
            var s = _service.GetSummary();
            _totalDossiers = s.TotalDossiers > 0 ? s.TotalDossiers : 1;

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

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 26,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var valText = new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            };

            var lblText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            };

            sp.Children.Add(iconText);
            sp.Children.Add(valText);
            sp.Children.Add(lblText);
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
        }

        private void CustomField_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CustomFieldCombo.SelectedValue is not int fieldId) return;
            CustomStatsGrid.ItemsSource = _service.GetCustomFieldStats(fieldId);
        }

        // ── REFRESH ───────────────────────────────────────────────────────────

        private void Refresh_Click(object sender, RoutedEventArgs e)
            => LoadAll();
    }
}