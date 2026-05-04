using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Dapper;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ArchiveSystem.Views.Pages
{
    // ── View model row ────────────────────────────────────────────────────────
    public class AuditLogRow
    {
        public int AuditId { get; set; }
        public int? UserId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public string Description { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UserFullName { get; set; }

        public string ActionTypeArabic => ActionType switch
        {
            AuditActions.LoginSuccess => "تسجيل دخول ناجح",
            AuditActions.LoginFailure => "تسجيل دخول فاشل",
            AuditActions.RecordCreated => "إضافة سجل",
            AuditActions.RecordEdited => "تعديل سجل",
            AuditActions.RecordDeleted => "حذف سجل",
            AuditActions.DossierCreated => "إنشاء دوسية",
            AuditActions.DossierEdited => "تعديل دوسية",
            AuditActions.DossierMoved => "نقل دوسية",
            AuditActions.ExcelImportCompleted => "اعتماد استيراد Excel",
            AuditActions.ExcelImportFailed => "فشل استيراد Excel",
            AuditActions.BackupCreated => "إنشاء نسخة احتياطية",
            AuditActions.RestoreCompleted => "استعادة نسخة احتياطية",
            AuditActions.SettingsChanged => "تغيير الإعدادات",
            AuditActions.UserChanged => "تغيير بيانات مستخدم",
            AuditActions.RoleChanged => "تغيير دور",
            AuditActions.BulkFieldUpdate => "تعبئة جماعية",
            AuditActions.CustomFieldChanged => "تغيير حقل مخصص",
            AuditActions.DossierDeleted => "حذف دوسية",
            "Logout" => "تسجيل خروج",
            "ImportRolledBack" => "تراجع عن استيراد",
            "ReportPrinted" => "طباعة تقرير",
            _ => ActionType
        };

        public string EntityDisplay =>
            EntityType == null ? "—"
            : EntityId.HasValue ? $"{EntityType} #{EntityId}"
            : EntityType;
    }

    // ── Simple user DTO for the filter dropdown ───────────────────────────────
    file class UserOption
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    public partial class AuditLogPage : Page
    {
        private const int PageSize = 150;
        private int _currentPage = 1;
        private int _totalCount = 0;

        public AuditLogPage()
        {
            InitializeComponent();
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ─────────────────────────────────────────────────────────────

        private void Initialize()
        {
            if (PermissionHelper.DenyPage(this, Permissions.ViewAuditLog)) return;
            LoadActionTypes();
            LoadUsers();
            Load();
        }

        private void LoadActionTypes()
        {
            // Populate distinct action types from the DB
            using var conn = App.Database.CreateConnection();
            var types = conn.Query<string>(
                "SELECT DISTINCT ActionType FROM AuditLog ORDER BY ActionType").AsList();

            ActionCombo.Items.Clear();
            ActionCombo.Items.Add(new ComboBoxItem { Content = "كل الأحداث", Tag = "" });
            foreach (var t in types)
            {
                var row = new AuditLogRow { ActionType = t };
                ActionCombo.Items.Add(new ComboBoxItem
                {
                    Content = row.ActionTypeArabic,
                    Tag = t
                });
            }
            ActionCombo.SelectedIndex = 0;
        }

        private void LoadUsers()
        {
            using var conn = App.Database.CreateConnection();
            var users = conn.Query<UserOption>(
                "SELECT UserId, FullName FROM Users ORDER BY FullName").AsList();

            UserCombo.Items.Clear();
            UserCombo.Items.Add(new ComboBoxItem { Content = "كل المستخدمين", Tag = 0 });
            foreach (var u in users)
                UserCombo.Items.Add(new ComboBoxItem { Content = u.FullName, Tag = u.UserId });
            UserCombo.SelectedIndex = 0;
        }

        // ── LOAD ─────────────────────────────────────────────────────────────

        private void Load()
        {
            var (where, p) = BuildWhere();

            using var conn = App.Database.CreateConnection();

            _totalCount = conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM AuditLog al LEFT JOIN Users u ON u.UserId = al.UserId {where}", p);

            var offset = (_currentPage - 1) * PageSize;
            var qp = new DynamicParameters(p);
            qp.Add("PageSize", PageSize);
            qp.Add("Offset", offset);

            var rows = conn.Query<AuditLogRow>($@"
                SELECT
                    al.AuditId,
                    al.UserId,
                    al.ActionType,
                    al.EntityType,
                    al.EntityId,
                    al.Description,
                    al.CreatedAt,
                    COALESCE(u.FullName, '(نظام)') AS UserFullName
                FROM AuditLog al
                LEFT JOIN Users u ON u.UserId = al.UserId
                {where}
                ORDER BY al.AuditId DESC
                LIMIT @PageSize OFFSET @Offset",
                qp).AsList();

            AuditGrid.ItemsSource = rows;

            int totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalCount / PageSize));

            ResultCountText.Text = _totalCount == 0
                ? "لا توجد سجلات مراجعة تطابق الفلتر."
                : $"{_totalCount:N0} حدث مراجعة";

            PageText.Text = $"صفحة {_currentPage} من {totalPages}  |  عرض {rows.Count} من {_totalCount:N0}";
        }

        // ── WHERE BUILDER ─────────────────────────────────────────────────────

        private (string Sql, DynamicParameters Params) BuildWhere()
        {
            var conditions = new List<string>();
            var p = new DynamicParameters();

            // Action filter
            if (ActionCombo.SelectedItem is ComboBoxItem ac
                && ac.Tag is string at && !string.IsNullOrEmpty(at))
            {
                conditions.Add("al.ActionType = @ActionType");
                p.Add("ActionType", at);
            }

            // User filter
            if (UserCombo.SelectedItem is ComboBoxItem uc
                && uc.Tag is int uid && uid > 0)
            {
                conditions.Add("al.UserId = @UserId");
                p.Add("UserId", uid);
            }

            // Description search
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                conditions.Add("al.Description LIKE @Desc");
                p.Add("Desc", $"%{SearchBox.Text.Trim()}%");
            }

            // Date from
            if (!string.IsNullOrWhiteSpace(DateFromBox.Text))
            {
                conditions.Add("al.CreatedAt >= @DateFrom");
                p.Add("DateFrom", DateFromBox.Text.Trim());
            }

            // Date to  (inclusive — add 1 day so "to 2025-01-31" includes all that day)
            if (!string.IsNullOrWhiteSpace(DateToBox.Text))
            {
                conditions.Add("al.CreatedAt < @DateTo");
                if (DateTime.TryParse(DateToBox.Text.Trim(), out var dt))
                    p.Add("DateTo", dt.AddDays(1).ToString("yyyy-MM-dd"));
                else
                    p.Add("DateTo", DateToBox.Text.Trim());
            }

            string sql = conditions.Count > 0
                ? "WHERE " + string.Join(" AND ", conditions)
                : string.Empty;

            return (sql, p);
        }

        // ── FILTER EVENTS (debounced) ─────────────────────────────────────────

        private System.Windows.Threading.DispatcherTimer? _debounce;

        private void Filter_Changed(object sender, RoutedEventArgs e) => StartDebounce();

        private void StartDebounce()
        {
            _debounce?.Stop();
            _debounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _debounce.Tick += (s, _) =>
            {
                _debounce.Stop();
                _currentPage = 1;
                Load();
            };
            _debounce.Start();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ActionCombo.SelectedIndex = 0;
            UserCombo.SelectedIndex = 0;
            SearchBox.Text = string.Empty;
            DateFromBox.Text = string.Empty;
            DateToBox.Text = string.Empty;
            _currentPage = 1;
            Load();
        }

        // ── PAGER ─────────────────────────────────────────────────────────────

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage <= 1) return;
            _currentPage--;
            Load();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)_totalCount / PageSize);
            if (_currentPage >= totalPages) return;
            _currentPage++;
            Load();
        }

        // ── CSV EXPORT ────────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var (where, p) = BuildWhere();
            var allP = new DynamicParameters(p);
            allP.Add("PageSize", 999_999);
            allP.Add("Offset", 0);

            using var conn = App.Database.CreateConnection();
            var rows = conn.Query<AuditLogRow>($@"
                SELECT al.AuditId, al.UserId, al.ActionType, al.EntityType,
                       al.EntityId, al.Description, al.CreatedAt,
                       COALESCE(u.FullName, '(نظام)') AS UserFullName
                FROM AuditLog al
                LEFT JOIN Users u ON u.UserId = al.UserId
                {where}
                ORDER BY al.AuditId DESC
                LIMIT @PageSize OFFSET @Offset", allP).AsList();

            var sb = new StringBuilder();
            sb.AppendLine("AuditId,التاريخ,المستخدم,نوع الحدث,الكيان,الوصف");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    r.AuditId.ToString(),
                    Esc(r.CreatedAt),
                    Esc(r.UserFullName ?? ""),
                    Esc(r.ActionTypeArabic),
                    Esc(r.EntityDisplay),
                    Esc(r.Description)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"تم التصدير:\n{dlg.FileName}", "تصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── PDF EXPORT ────────────────────────────────────────────────────────────

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var rows = LoadAllRowsForExport();
            if (rows.Count == 0)
            {
                MessageBox.Show("لا توجد سجلات تطابق الفلتر الحالي.",
                    "تصدير PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Let the user pick where to save
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };

            if (dlg.ShowDialog() != true) return;

            string periodLabel = BuildPeriodLabel();
            var reportService = new ReportService(App.Database);
            var err = reportService.GenerateAuditLogPdf(rows, dlg.FileName, periodLabel);

            if (err != null)
            {
                MessageBox.Show(err, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Open the file so the user can review / print
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(dlg.FileName)
                    { UseShellExecute = true });
            }
            catch { /* viewer not available — file was still saved */ }

            MessageBox.Show($"تم التصدير بنجاح:\n{dlg.FileName}",
                "تصدير PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Fetches every row matching the current filter — no paging limit.
        /// Reuses the same WHERE builder used by the paged Load() method.
        /// </summary>
        private List<AuditLogRow> LoadAllRowsForExport()
        {
            var (where, p) = BuildWhere();
            var allP = new DynamicParameters(p);
            allP.Add("PageSize", 999_999);
            allP.Add("Offset", 0);

            using var conn = App.Database.CreateConnection();
            return conn.Query<AuditLogRow>($@"
        SELECT al.AuditId, al.UserId, al.ActionType, al.EntityType,
               al.EntityId, al.Description, al.CreatedAt,
               COALESCE(u.FullName, '(نظام)') AS UserFullName
        FROM AuditLog al
        LEFT JOIN Users u ON u.UserId = al.UserId
        {where}
        ORDER BY al.AuditId DESC
        LIMIT @PageSize OFFSET @Offset",
                allP).AsList();
        }

        /// <summary>
        /// Builds a human-readable period label from the active filter controls,
        /// shown as a subtitle inside the PDF header.
        /// </summary>
        private string BuildPeriodLabel()
        {
            var parts = new List<string>();

            if (ActionCombo.SelectedItem is ComboBoxItem ac
                && ac.Tag is string at && !string.IsNullOrEmpty(at))
                parts.Add($"الحدث: {ac.Content}");

            if (UserCombo.SelectedItem is ComboBoxItem uc
                && uc.Tag is int uid && uid > 0)
                parts.Add($"المستخدم: {uc.Content}");

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                parts.Add($"بحث: {SearchBox.Text.Trim()}");

            if (!string.IsNullOrWhiteSpace(DateFromBox.Text))
                parts.Add($"من: {DateFromBox.Text.Trim()}");

            if (!string.IsNullOrWhiteSpace(DateToBox.Text))
                parts.Add($"إلى: {DateToBox.Text.Trim()}");

            return parts.Count > 0 ? string.Join("  |  ", parts) : "كل السجلات";
        }

        private static string Esc(string? s)
        {
            s ??= "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}