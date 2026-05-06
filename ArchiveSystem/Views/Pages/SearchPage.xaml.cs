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
        private SearchResult? _selectedResult;

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
        }

        // ── DASHBOARD STAT CARDS ──────────────────────────────────────────────

        private void LoadDashboardStats()
        {
            var (totalDossiers, totalRecords, recordsToday, recordsThisMonth, nationalityCount) =
                _recordService.GetDashboardStats();

            StatsPanel.Children.Clear();
            StatsPanel.Children.Add(MakeStatCard("الدوسيات", totalDossiers.ToString("N0"), "#1a7a60"));
            StatsPanel.Children.Add(MakeStatCard("الملفات", totalRecords.ToString("N0"), "#1565C0"));
            StatsPanel.Children.Add(MakeStatCard("مدخل اليوم", recordsToday.ToString("N0"), "#6A1B9A"));
            StatsPanel.Children.Add(MakeStatCard("هذا الشهر", recordsThisMonth.ToString("N0"), "#E65100"));
            StatsPanel.Children.Add(MakeStatCard("الجنسيات", nationalityCount.ToString("N0"), "#00695C"));
        }

        private static Border MakeStatCard(string label, string value, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 10, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            card.Child = sp;
            return card;
        }

        // ── RECENT DOSSIERS ───────────────────────────────────────────────────

        private void LoadRecentDossiers()
        {
            var recent = _recordService.GetRecentDossiers(12);
            RecentDossiersList.ItemsSource = recent;
            RecentGrid.ItemsSource = recent;
            ShowEmptyState();
        }

        // ── SEARCH ────────────────────────────────────────────────────────────

        private void Search_Click(object sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer?.Stop();
            _searchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _searchTimer.Tick += (s, _) => { _searchTimer.Stop(); DoSearch(); };
            _searchTimer.Start();
        }

        private void DoSearch()
        {
            var query = SearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                ResultCountText.Text = string.Empty;
                ClearBtn.Visibility = Visibility.Collapsed;
                ShowEmptyState();
                return;
            }

            var results = _recordService.Search(query);

            ClearBtn.Visibility = Visibility.Visible;

            ResultCountText.Text = results.Count == 0
                ? "لا توجد نتائج مطابقة"
                : $"تم العثور على {results.Count:N0} نتيجة";

            ResultsGrid.ItemsSource = results;
            _selectedResult = null;
            HideDetailCard();
            ClearDetailCard();

            if (results.Count == 0)
            {
                ShowEmptyState();
            }
            else
            {
                EmptySearchState.Visibility = Visibility.Collapsed;
                ResultsContainer.Visibility = Visibility.Visible;
                ResultDetailCard.Visibility = Visibility.Collapsed;
                QuickPrintBtn.Visibility = Visibility.Collapsed;
                QuickCardBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            ClearBtn.Visibility = Visibility.Collapsed;
            ResultCountText.Text = string.Empty;
            _selectedResult = null;
            HideDetailCard();
            ShowEmptyState();
        }

        // ── ROW SELECTION → DETAIL CARD ───────────────────────────────────────

        private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not SearchResult result)
            {
                HideDetailCard();
                return;
            }

            _selectedResult = result;
            ShowDetailCard(result);

            bool canPrint = PermissionHelper.Can(Permissions.PrintDossierFace);
            PrintFromDetailBtn.Visibility = canPrint ? Visibility.Visible : Visibility.Collapsed;
            QuickPrintBtn.Visibility = canPrint ? Visibility.Visible : Visibility.Collapsed;
            QuickCardBtn.Visibility = Visibility.Visible;
        }

        private void ShowDetailCard(SearchResult result)
        {
            DetailName.Text = result.PersonName;
            DetailPrisonerNum.Text = $"رقم السجين: {result.PrisonerNumber}  ·  الجنسية: {GetNationality(result.RecordId)}";

            var parts = result.LocationDisplay?.Split('-') ?? Array.Empty<string>();
            string hallway = parts.Length >= 1 ? parts[0].Replace("ممر", "").Trim() : "—";
            string cabinet = parts.Length >= 2 ? parts[1].Replace("كبينة", "").Trim() : "—";
            string shelf = parts.Length >= 3 ? parts[2].Replace("رف", "").Trim() : "—";

            DetailMonth.Text = result.HijriDisplay?.Split('/').FirstOrDefault()?.Trim() ?? "—";
            DetailDossier.Text = result.DossierNumber.ToString();
            DetailHallway.Text = hallway;
            DetailCabinet.Text = cabinet;
            DetailShelf.Text = shelf;

            ResultsContainer.Visibility = Visibility.Visible;
            ResultDetailCard.Visibility = Visibility.Visible;
            EmptySearchState.Visibility = Visibility.Collapsed;
        }

        private string GetNationality(int recordId)
        {
            try
            {
                var values = _customFieldService.GetRecordValues(recordId);
                foreach (var kv in values)
                {
                    var field = _customFieldService.GetAllFields()
                        .FirstOrDefault(f => f.CustomFieldId == kv.Key
                            && f.ArabicLabel.Contains("جنسية"));
                    if (field != null && kv.Value != null)
                        return kv.Value;
                }
            }
            catch { }
            return "غير مدخلة";
        }

        private void HideDetailCard()
        {
            ResultDetailCard.Visibility = Visibility.Collapsed;
            QuickPrintBtn.Visibility = Visibility.Collapsed;
            QuickCardBtn.Visibility = Visibility.Collapsed;
        }

        private void ClearDetailCard()
        {
            DetailName.Text = string.Empty;
            DetailPrisonerNum.Text = string.Empty;
            DetailMonth.Text = "—";
            DetailDossier.Text = "—";
            DetailHallway.Text = "—";
            DetailCabinet.Text = "—";
            DetailShelf.Text = "—";
        }

        private void ShowEmptyState()
        {
            EmptySearchState.Visibility = Visibility.Visible;
            ResultsContainer.Visibility = Visibility.Collapsed;
            ResultDetailCard.Visibility = Visibility.Collapsed;
        }

        // ── OPEN DOSSIER FROM DETAIL ──────────────────────────────────────────

        private void OpenDossierFromDetail_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedResult == null) return;
            NavigationService?.Navigate(new DossierDetailsPage(_selectedResult.DossierId));
        }

        // ── QUICK PRINT ───────────────────────────────────────────────────────

        private void QuickPrint_Click(object sender, RoutedEventArgs e)
        {
            SearchResult? result = _selectedResult
                ?? ResultsGrid.SelectedItem as SearchResult;
            if (result == null) return;
            PrintDossierFace(result.DossierId, result.DossierNumber);
        }

        private void QuickCard_Click(object sender, RoutedEventArgs e)
        {
            SearchResult? result = _selectedResult
                ?? ResultsGrid.SelectedItem as SearchResult;
            if (result == null) return;
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

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { }
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
            if (err != null) { MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true }); }
            catch { }
        }

        private void RecentDossierItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is RecentDossierEntry entry)
                NavigationService?.Navigate(new DossierDetailsPage(entry.DossierId));
        }
        // ── DOUBLE CLICK ──────────────────────────────────────────────────────

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

        // ── STUBS (for XAML compat with old SearchPage) ───────────────────────

        private void CustomFieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void ClearCustomFilter_Click(object sender, RoutedEventArgs e) { }
    }
}