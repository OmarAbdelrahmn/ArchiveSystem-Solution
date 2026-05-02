using System.Windows;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Pages;

namespace ArchiveSystem
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Show logged-in user name in the sidebar footer
            CurrentUserText.Text = UserSession.CurrentUser?.FullName ?? "مدير الأرشيف";

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

        private void NavAuditLog_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new AuditLogPage());

        private void NavSettings_Click(object sender, RoutedEventArgs e)
            => MainFrame.Navigate(new SettingsPage());
    }
}