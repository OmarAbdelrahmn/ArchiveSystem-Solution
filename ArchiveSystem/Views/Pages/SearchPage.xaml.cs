using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using QuestPDF.Fluent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    public partial class SearchPage : Page
    {
        private readonly RecordService _recordService;
        private readonly CustomFieldService _customFieldService;
        private readonly ReportService _reportService;
        private readonly DossierService _dossierService;

        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private List<CustomField> _searchableCustomFields = new();

        // Track whether we are in "results" mode or "recent" mode
        private bool _isShowingResults = false;

        public SearchPage()
        {
            InitializeComponent();
            _recordService = new RecordService(App.Database);
            _customFieldService = new CustomFieldService(App.Database);
            _reportService = new ReportService(App.Database);
            _dossierService = new DossierService(App.Database);

            Loaded += (s, e) => Initialize();
        }

        // ── INIT ──────────────────────────────────────────────────────────────

        private void Initialize()
        {
            LoadDashboardStats();
            LoadRecentDossiers();
            LoadCustomFieldFilter();
        }

        // ── DASHBOARD STAT CARDS ──────────────────────────────────────────────

        private void LoadDashboardStats()
        {
            var (totalDossiers, totalRecords, recordsToday, recordsThisMonth, nationalityCount) =
                _recordService.GetDashboardStats();

            StatsPanel.Children.Clear();
            StatsPanel.Children.Add(MakeStatCard("📁", "إجمالي الدوسيات",
                totalDossiers.ToString("N0"), "#1a7a60"));
            StatsPanel.Children.Add(MakeStatCard("👤", "إجمالي السجلات",
                totalRecords.ToString("N0"), "#1565C0"));
            StatsPanel.Children.Add(MakeStatCard("📅", "مُضاف اليوم",
                recordsToday.ToString("N0"), "#6A1B9A"));
            StatsPanel.Children.Add(MakeStatCard("📆", "مُضاف هذا الشهر",
                recordsThisMonth.ToString("N0"), "#E65100"));
            StatsPanel.Children.Add(MakeStatCard("🌍", "عدد الجنسيات",
                nationalityCount.ToString("N0"), "#00695C"));
        }

        private static Border MakeStatCard(string icon, string label, string value, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);

            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 12, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30,
                    color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4,
                    Opacity = 0.06,
                    ShadowDepth = 1
                }
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var textSp = new StackPanel();
            textSp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            textSp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });

            sp.Children.Add(iconBlock);
            sp.Children.Add(textSp);
            card.Child = sp;
            return card;
        }

        // ── RECENT DOSSIERS ───────────────────────────────────────────────────

        private void LoadRecentDossiers()
        {
            var recent = _recordService.GetRecentDossiers(10);
            RecentGrid.ItemsSource = recent;
            RecentPanel.Visibility = Visibility.Visible;
            ResultsGrid.Visibility = Visibility.Collapsed;
            _isShowingResults = false;
        }

        // ── CUSTOM FIELD FILTER ───────────────────────────────────────────────

        private void LoadCustomFieldFilter()
        {
            // Only fields that are active and shown in entry/alldata make sense as search filters
            _searchableCustomFields = _customFieldService.GetActiveEntryFields();

            if (_searchableCustomFields.Count == 0)
            {
                CustomFilterBorder.Visibility = Visibility.Collapsed;
                return;
            }

            CustomFieldCombo.ItemsSource = _searchableCustomFields;
            CustomFieldCombo.SelectedIndex = 0;
            CustomFilterBorder.Visibility = Visibility.Visible;
        }

        private void CustomFieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If user has already typed a field value, re-run search
            if (!string.IsNullOrWhiteSpace(CustomFieldValueBox.Text))
                TriggerSearch();
        }

        private void ClearCustomFilter_Click(object sender, RoutedEventArgs e)
        {
            CustomFieldValueBox.Text = string.Empty;
            CustomFieldCombo.SelectedIndex = 0;
            TriggerSearch();
        }

        // ── SEARCH ────────────────────────────────────────────────────────────

        private void Search_Click(object sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => TriggerSearch();

        private void TriggerSearch()
        {
            _searchTimer?.Stop();
            _searchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _searchTimer.Tick += (s, _) =>
            {
                _searchTimer.Stop();
                DoSearch();
            };
            _searchTimer.Start();
        }

        private void DoSearch()
        {
            var query = SearchBox.Text.Trim();
            var cfValue = CustomFieldValueBox?.Text?.Trim() ?? string.Empty;

            // If both empty, go back to "recent" view
            if (string.IsNullOrEmpty(query) && string.IsNullOrEmpty(cfValue))
            {
                ResultCountText.Text = string.Empty;
                ResultsGrid.Visibility = Visibility.Collapsed;
                RecentPanel.Visibility = Visibility.Visible;
                ClearBtn.Visibility = Visibility.Collapsed;
                QuickPrintBtn.Visibility = Visibility.Collapsed;
                _isShowingResults = false;
                return;
            }

            List<SearchResult> results;

            bool hasCustomFilter = !string.IsNullOrEmpty(cfValue)
                && CustomFieldCombo.SelectedValue is int cfId && cfId > 0;

            if (hasCustomFilter)
            {
                int fieldId = (int)CustomFieldCombo.SelectedValue!;
                results = _recordService.SearchWithCustomField(query, fieldId, cfValue);
            }
            else
            {
                results = _recordService.Search(query);
            }

            ResultsGrid.ItemsSource = results;
            ResultsGrid.Visibility = Visibility.Visible;
            RecentPanel.Visibility = Visibility.Collapsed;
            ClearBtn.Visibility = Visibility.Visible;
            _isShowingResults = true;

            ResultCountText.Text = results.Count == 0
                ? "لا توجد نتائج."
                : $"تم العثور على {results.Count} نتيجة.";

            // Hide quick-print when result list changes (user must re-select)
            QuickPrintBtn.Visibility = Visibility.Collapsed;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            CustomFieldValueBox.Text = string.Empty;
        }

        // ── SELECTION CHANGED → show/hide quick print ─────────────────────────

        private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool canPrint = PermissionHelper.Can(Permissions.PrintDossierFace);
            QuickPrintBtn.Visibility =
                canPrint && ResultsGrid.SelectedItem is SearchResult
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            QuickCardBtn.Visibility =
                ResultsGrid.SelectedItem is SearchResult ? Visibility.Visible : Visibility.Collapsed;
        }

        private void QuickCard_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not SearchResult result) return;
            PrintRecordCard(result);
        }

        private void PrintRecordCard(SearchResult result)
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"record_card_{result.RecordId}_{DateTime.Now:yyyyMMddHHmm}.pdf");

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(10, 6, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.Margin(0.8f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Noto Kufi Arabic"));
                    page.ContentFromRightToLeft();
                    page.Content().Column(col =>
                    {
                        col.Item().Text($"رقم السجين: {result.PrisonerNumber}").Bold().FontSize(13);
                        col.Item().Text($"الاسم: {result.PersonName}");
                        col.Item().Text($"الدوسية: {result.DossierNumber}  |  تسلسل: {result.SequenceNumber}");
                        col.Item().Text($"التاريخ: {result.HijriDisplay}");
                        col.Item().Text($"الموقع: {result.LocationDisplay}");
                    });
                });
            }).GeneratePdf(path);

            try
            {
                System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        // ── QUICK PRINT ───────────────────────────────────────────────────────

        private void QuickPrint_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not SearchResult result) return;
            PrintDossierFace(result.DossierId, result.DossierNumber);
        }

        private void PrintDossierFace(int dossierId, int dossierNumber)
        {
            var data = _reportService.LoadDossierFaceData(dossierId);
            if (data == null || data.Records.Count == 0)
            {
                MessageBox.Show("لا توجد سجلات للطباعة في هذه الدوسية.",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dossier_face_{dossierNumber}_{DateTime.Now:yyyyMMddHHmm}.pdf");

            var err = _reportService.GenerateDossierFacePdf(data, tempPath);
            if (err != null)
            {
                MessageBox.Show(err, "خطأ في الطباعة",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Open with default PDF viewer (which lets user print)
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(tempPath)
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر فتح ملف PDF:\n{ex.Message}\n\nالمسار: {tempPath}",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── DOUBLE CLICK: navigate to dossier details ─────────────────────────

        private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not SearchResult result) return;
            NavigationService?.Navigate(new DossierDetailsPage(result.DossierId));
        }

        private void RecentGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentGrid.SelectedItem is not RecentDossierEntry entry) return;
            NavigationService?.Navigate(new DossierDetailsPage(entry.DossierId));
        }
    }
}