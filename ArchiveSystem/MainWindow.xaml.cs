using System.Windows;
using System.Windows.Controls;
using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Pages;

namespace ArchiveSystem
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            FontScaleManager.Apply(this, FontScaleManager.ToMultiplier(App.FontScaleSetting));

            var user = UserSession.CurrentUser;
            CurrentUserText.Text = user?.FullName ?? "مستخدم";

            AppVersionText.Text = $"v{App.AppVersion}";

            ApplyNavPermissions();
            MainFrame.Navigate(new SearchPage());
        }
        // ── Permission-gated navigation ───────────────────────────────────────

        /// <summary>
        /// Hide any nav button the current user has no permission to reach.
        /// The XAML names must match x:Name attributes on each Button.
        /// </summary>
        private void ApplyNavPermissions()
        {
            // Search — open to all authenticated users (no specific permission needed)

            // Entry — requires AddRecord
            PermissionHelper.Apply(NavEntryButton, Permissions.AddRecord, hideInstead: true);

            // All Data — requires SearchRecords
            PermissionHelper.Apply(NavAllDataButton, Permissions.SearchRecords, hideInstead: true);

            // Statistics — requires ViewStatistics
            PermissionHelper.Apply(NavStatsButton, Permissions.ViewStatistics, hideInstead: true);

            // Excel Import — requires ImportExcel
            PermissionHelper.Apply(NavImportButton, Permissions.ImportExcel, hideInstead: true);

            // Reports — requires PrintReports OR PrintDossierFace
            bool canReports = PermissionHelper.Can(Permissions.PrintReports)
                           || PermissionHelper.Can(Permissions.PrintDossierFace);
            NavReportsButton.Visibility = canReports
                ? Visibility.Visible : Visibility.Collapsed;

            // Audit Log — requires ViewAuditLog
            PermissionHelper.Apply(NavAuditLogButton, Permissions.ViewAuditLog, hideInstead: true);

            // Settings — requires at least one management permission
            bool canSettings = PermissionHelper.Can(Permissions.ManageUsers)
                            || PermissionHelper.Can(Permissions.ManageSettings)
                            || PermissionHelper.Can(Permissions.ManageCustomFields)
                            || PermissionHelper.Can(Permissions.ManageArchiveStructure)
                            || PermissionHelper.Can(Permissions.CreateBackup)
                            || PermissionHelper.Can(Permissions.RestoreBackup);
            NavSettingsButton.Visibility = canSettings
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Nav click handlers ────────────────────────────────────────────────

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