using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using Dapper;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchiveSystem.Views.Pages
{
    // ── View-model row for the suggestions grid ───────────────────────────────
    public class SuggestionRow
    {
        public string Value { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public string? LastUsed { get; set; }
    }

    public partial class FieldSuggestionsPage : Page
    {
        private readonly CustomFieldService _customFieldService;
        private int _selectedFieldId = 0;
        private string _selectedFieldLabel = string.Empty;

        public FieldSuggestionsPage()
        {
            InitializeComponent();
            _customFieldService = new CustomFieldService(App.Database);
            Loaded += (s, e) => Initialize();
        }

        // ── INIT ──────────────────────────────────────────────────────────────

        private void Initialize()
        {
            if (PermissionHelper.DenyPage(this, Permissions.ManageFieldSuggestions)) return;
            LoadFields();
        }

        private void LoadFields()
        {
            using var conn = App.Database.CreateConnection();
            var fields = conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1
                AND FieldType = 'TextWithSuggestions'
                ORDER BY SortOrder, ArabicLabel").AsList();

            FieldsList.ItemsSource = fields;

            if (fields.Count > 0)
                FieldsList.SelectedIndex = 0;
        }

        // ── FIELD SELECTION ───────────────────────────────────────────────────

        private void FieldsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FieldsList.SelectedItem is not CustomField field) return;

            _selectedFieldId = field.CustomFieldId;
            _selectedFieldLabel = field.ArabicLabel;

            FieldTitleText.Text = $"اقتراحات حقل: {field.ArabicLabel}";
            NewSuggestionBox.IsEnabled = true;
            AddSuggestionBtn.IsEnabled = true;
            ClearAllBtn.IsEnabled = true;

            HideMsg();
            LoadSuggestions();
        }

        // ── LOAD SUGGESTIONS ──────────────────────────────────────────────────

        private void LoadSuggestions()
        {
            if (_selectedFieldId == 0) return;

            using var conn = App.Database.CreateConnection();

            var rows = conn.Query<SuggestionRow>(@"
                SELECT
                    ValueText        AS Value,
                    COUNT(*)         AS UsageCount,
                    MAX(UpdatedAt)   AS LastUsed
                FROM RecordCustomFieldValues
                WHERE CustomFieldId = @FieldId
                AND   ValueText IS NOT NULL
                AND   ValueText != ''
                GROUP BY ValueText
                ORDER BY COUNT(*) DESC, MAX(UpdatedAt) DESC",
                new { FieldId = _selectedFieldId }).AsList();

            if (rows.Count == 0)
            {
                SuggestionsGrid.Visibility = Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Visible;
                StatsText.Text = "لا توجد اقتراحات محفوظة لهذا الحقل.";
            }
            else
            {
                SuggestionsGrid.ItemsSource = rows;
                SuggestionsGrid.Visibility = Visibility.Visible;
                EmptyText.Visibility = Visibility.Collapsed;

                int totalRecords = rows.Sum(r => r.UsageCount);
                StatsText.Text =
                    $"{rows.Count} قيمة مختلفة  |  مستخدمة في {totalRecords} سجل";
            }
        }

        // ── ADD SUGGESTION ────────────────────────────────────────────────────

        private void AddSuggestion_Click(object sender, RoutedEventArgs e)
        {
            HideMsg();

            string val = NewSuggestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(val))
            {
                ShowMsg("يرجى إدخال قيمة الاقتراح.", success: false);
                return;
            }

            // Check duplicate
            using var conn = App.Database.CreateConnection();
            int exists = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM RecordCustomFieldValues
                WHERE CustomFieldId = @FieldId
                AND   ValueText = @Val",
                new { FieldId = _selectedFieldId, Val = val });

            if (exists > 0)
            {
                ShowMsg($"القيمة '{val}' موجودة مسبقاً في الاقتراحات.", success: false);
                return;
            }

            // We insert a "seed" row with a dummy RecordId = 0 is not possible due to FK.
            // Instead we use CustomFieldOptions for TextWithSuggestions seeding.
            // The correct approach: add to CustomFieldOptions so it always appears.
            var err = _customFieldService.AddFieldOption(_selectedFieldId, val);
            if (err != null)
            {
                ShowMsg(err, success: false);
                return;
            }

            NewSuggestionBox.Text = string.Empty;
            ShowMsg($"✅ تم إضافة الاقتراح '{val}' بنجاح.", success: true);
            LoadSuggestions();
        }

        // ── DELETE SINGLE SUGGESTION ──────────────────────────────────────────

        private void DeleteSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not string value) return;

            using var conn = App.Database.CreateConnection();

            // Count how many records use this value
            int count = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM RecordCustomFieldValues
                WHERE CustomFieldId = @FieldId AND ValueText = @Val",
                new { FieldId = _selectedFieldId, Val = value });

            string warningMsg = count > 0
                ? $"⚠️ هذه القيمة مستخدمة في {count} سجل.\n\nحذفها سيمسح القيمة من تلك السجلات.\n\nهل تريد المتابعة؟"
                : $"هل تريد حذف الاقتراح '{value}'؟";

            var confirm = MessageBox.Show(
                warningMsg,
                "تأكيد الحذف",
                MessageBoxButton.YesNo,
                count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // Null out all record values for this suggestion
            if (count > 0)
            {
                conn.Execute(@"
                    UPDATE RecordCustomFieldValues
                    SET ValueText = NULL, UpdatedAt = @Now, UpdatedByUserId = @UserId
                    WHERE CustomFieldId = @FieldId AND ValueText = @Val",
                    new
                    {
                        FieldId = _selectedFieldId,
                        Val = value,
                        Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                        UserId = UserSession.CurrentUser?.UserId
                    });
            }

            // Also remove from CustomFieldOptions if it exists there
            conn.Execute(@"
                UPDATE CustomFieldOptions
                SET IsActive = 0, UpdatedAt = @Now
                WHERE CustomFieldId = @FieldId AND ArabicValue = @Val",
                new
                {
                    FieldId = _selectedFieldId,
                    Val = value,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            // Audit
            conn.Execute(@"
                INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                VALUES (@UserId, 'CustomFieldChanged', @Desc, @Now)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Desc = $"حذف اقتراح '{value}' من حقل '{_selectedFieldLabel}' — {count} سجل متأثر",
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });

            ShowMsg(
                count > 0
                    ? $"✅ تم حذف الاقتراح ومسحه من {count} سجل."
                    : "✅ تم حذف الاقتراح.",
                success: true);

            LoadSuggestions();
        }

        // ── CLEAR ALL ─────────────────────────────────────────────────────────

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFieldId == 0) return;

            using var conn = App.Database.CreateConnection();

            int totalRecords = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM RecordCustomFieldValues
                WHERE CustomFieldId = @FieldId
                AND   ValueText IS NOT NULL AND ValueText != ''",
                new { FieldId = _selectedFieldId });

            int distinctValues = conn.ExecuteScalar<int>(@"
                SELECT COUNT(DISTINCT ValueText) FROM RecordCustomFieldValues
                WHERE CustomFieldId = @FieldId
                AND   ValueText IS NOT NULL AND ValueText != ''",
                new { FieldId = _selectedFieldId });

            if (distinctValues == 0)
            {
                ShowMsg("لا توجد اقتراحات لمسحها.", success: false);
                return;
            }

            var confirm = MessageBox.Show(
                $"⚠️ سيتم مسح {distinctValues} قيمة مختلفة من {totalRecords} سجل.\n\n" +
                $"هذا الإجراء لا يمكن التراجع عنه.\n\nهل تريد المتابعة؟",
                "تأكيد مسح كل الاقتراحات",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            conn.Execute(@"
                UPDATE RecordCustomFieldValues
                SET ValueText = NULL, UpdatedAt = @Now, UpdatedByUserId = @UserId
                WHERE CustomFieldId = @FieldId
                AND   ValueText IS NOT NULL AND ValueText != ''",
                new
                {
                    FieldId = _selectedFieldId,
                    Now = now,
                    UserId = UserSession.CurrentUser?.UserId
                });

            // Deactivate all options too
            conn.Execute(@"
                UPDATE CustomFieldOptions
                SET IsActive = 0, UpdatedAt = @Now
                WHERE CustomFieldId = @FieldId",
                new { FieldId = _selectedFieldId, Now = now });

            conn.Execute(@"
                INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                VALUES (@UserId, 'CustomFieldChanged', @Desc, @Now)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Desc = $"مسح كل اقتراحات حقل '{_selectedFieldLabel}' — {totalRecords} سجل متأثر",
                    Now = now
                });

            ShowMsg(
                $"✅ تم مسح {distinctValues} قيمة من {totalRecords} سجل.",
                success: true);

            LoadSuggestions();
        }

        // ── REFRESH ───────────────────────────────────────────────────────────

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadFields();
            if (_selectedFieldId > 0)
                LoadSuggestions();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void ShowMsg(string msg, bool success)
        {
            MsgText.Text = msg;
            MsgBorder.Background = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(success ? "#E8F5E9" : "#FFEBEE"));
            MsgBorder.BorderBrush = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(success ? "#A5D6A7" : "#EF9A9A"));
            MsgBorder.BorderThickness = new System.Windows.Thickness(1);
            MsgBorder.CornerRadius = new System.Windows.CornerRadius(4);
            MsgText.Foreground = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(success ? "#2E7D32" : "#C62828"));
            MsgBorder.Visibility = Visibility.Visible;
        }

        private void HideMsg() => MsgBorder.Visibility = Visibility.Collapsed;
    }
}