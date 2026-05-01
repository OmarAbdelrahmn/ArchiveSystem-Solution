using System.Configuration;
using System.Windows;

namespace ArchiveSystem
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // open on search page by default
            MainFrame.Navigate(new SearchPage());
        }

        private void NavSearch_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new SearchPage());

        private void NavEntry_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new EntryPage());

        private void NavAllData_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new AllDataPage());

        private void NavStats_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new StatisticsPage());

        private void NavImport_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new ExcelImportPage());

        private void NavReports_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new ReportsPage());

        private void NavSettings_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new SettingsPage());
    }
}