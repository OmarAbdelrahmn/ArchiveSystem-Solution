using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Pages;
using Dapper;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchiveSystem
{
    public partial class MainWindow : Window
    {
        private bool _sidebarCollapsed = false;

        private Button? _activeNavBtn;

        private void SetActiveNav(Button clicked)
        {
            // Reset all nav buttons
            foreach (var btn in new[] {
        NavSearchButton, NavEntryButton, NavAllDataButton, NavStatsButton,
        NavImportButton, NavReportsButton, NavSuggestionsButton,
        NavAuditLogButton, NavBulkHistoryButton, NavSettingsButton })
            {
                if (btn == null) continue;
                btn.Background = Brushes.Transparent;
                btn.Foreground = Brushes.White;
            }

            // Highlight the clicked one
            clicked.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            clicked.Foreground = Brushes.White;
            _activeNavBtn = clicked;
        }

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            SidebarCol.Width = _sidebarCollapsed
                ? new GridLength(48)
                : new GridLength(220);
            SidebarToggleBtn.Content = _sidebarCollapsed ? "▶" : "◀";

            // Hide text on all nav buttons when collapsed
            foreach (var btn in new[] {
        NavEntryButton, NavAllDataButton, NavStatsButton,
        NavImportButton, NavReportsButton, NavSuggestionsButton,
        NavAuditLogButton, NavBulkHistoryButton, NavSettingsButton })
            {
                if (btn.Visibility == Visibility.Visible)
                    btn.Content = _sidebarCollapsed
                        ? btn.Content.ToString()!.Substring(0, 2)   // keep emoji only
                        : GetNavLabel(btn.Name);
            }
        }

        private static string GetNavLabel(string name) => name switch
        {
            "NavEntryButton" => "➕   إدخال بيانات",
            "NavAllDataButton" => "📋   كل البيانات",
            "NavStatsButton" => "📊   الإحصاءات",
            "NavImportButton" => "📥   استيراد Excel",
            "NavReportsButton" => "🖨️   التقارير",
            "NavSuggestionsButton" => "💡   الاقتراحات",
            "NavAuditLogButton" => "📜   سجل المراجعة",
            "NavBulkHistoryButton" => "📝   التعبئة الجماعية",
            "NavSettingsButton" => "⚙️   الإعدادات",
            _ => name
        };

        public MainWindow()
        {
            InitializeComponent();
            FontScaleManager.Apply(this, FontScaleManager.ToMultiplier(App.FontScaleSetting));

            var user = UserSession.CurrentUser;
            CurrentUserText.Text = user?.FullName ?? "مستخدم";

            try
            {
                using var conn = App.Database.CreateConnection();
                var role = conn.ExecuteScalar<string?>(@"
                    SELECT r.RoleName FROM Roles r
                    JOIN UserRoles ur ON ur.RoleId = r.RoleId
                    WHERE ur.UserId = @UserId
                    LIMIT 1",
                    new { UserId = user?.UserId });
                CurrentUserRoleText.Text = role ?? string.Empty;
            }
            catch { }

            AppVersionText.Text = $"v{App.AppVersion}";

            ApplyNavPermissions();

            SetActiveNav(NavSearchButton); // default page is Search

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

            PermissionHelper.Apply(NavBulkHistoryButton, Permissions.ViewAuditLog, hideInstead: true);

            PermissionHelper.Apply(NavSuggestionsButton, Permissions.ManageFieldSuggestions, hideInstead: true);

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
        {
            SetActiveNav(NavSearchButton);
            MainFrame.Navigate(new SearchPage());
        }

        private void NavEntry_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavEntryButton);
            MainFrame.Navigate(new EntryPage());
        }

        private void NavAllData_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavAllDataButton);
            MainFrame.Navigate(new AllDataPage());
        }

        private void NavStats_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavStatsButton);
            MainFrame.Navigate(new StatisticsPage());
        }

        private void NavImport_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavImportButton);
            MainFrame.Navigate(new ExcelImportPage());
        }

        private void NavReports_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavReportsButton);
            MainFrame.Navigate(new ReportsPage());
        }

        private void NavAuditLog_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavAuditLogButton);
            MainFrame.Navigate(new AuditLogPage());
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavSettingsButton);
            MainFrame.Navigate(new SettingsPage());
        }

        private void NavBulkHistory_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavBulkHistoryButton);
            MainFrame.Navigate(new BulkFillHistoryPage());
        }

        private void NavSuggestions_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(NavSuggestionsButton);
            MainFrame.Navigate(new FieldSuggestionsPage());
        }
    }
}