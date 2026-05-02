using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Pages
{
    public partial class SearchPage : Page
    {
        private readonly RecordService _recordService;

        public SearchPage()
        {
            InitializeComponent();
            _recordService = new RecordService(App.Database);
        }

        private void Search_Click(object sender, RoutedEventArgs e)
            => DoSearch();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void DoSearch()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                ResultCountText.Text = string.Empty;
                ResultsGrid.ItemsSource = null;
                return;
            }

            var results = _recordService.Search(query);
            ResultsGrid.ItemsSource = results;
            ResultCountText.Text = results.Count == 0
                ? "لا توجد نتائج."
                : $"تم العثور على {results.Count} نتيجة.";
        }

        private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not SearchResult result) return;

            // navigate to dossier details
            NavigationService?.Navigate(
                new DossierDetailsPage(result.DossierId));
        }
    }
}