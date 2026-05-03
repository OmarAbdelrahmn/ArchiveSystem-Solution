using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using Dapper;
using Microsoft.Win32;

namespace ArchiveSystem.Views.Pages
{
    // ── View-model wrapper for CustomField — adds Arabic type display ──────────
    public class CustomFieldRow : CustomField
    {
        public string FieldTypeArabic => FieldType switch
        {
            FieldTypes.Text => "نص",
            FieldTypes.TextWithSuggestions => "نص مع اقتراحات",
            FieldTypes.Number => "رقم",
            FieldTypes.Date => "تاريخ",
            FieldTypes.Boolean => "نعم / لا",
            FieldTypes.SingleChoice => "اختيار واحد",
            FieldTypes.MultiChoice => "اختيار متعدد",
            _ => FieldType
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    public partial class SettingsPage : Page
    {
        private readonly UserService _userService;
        private readonly RoleService _roleService;
        private readonly CustomFieldService _customFieldService;
        private readonly LocationService _locationService;
        private readonly BackupService _backupService;

        public SettingsPage()
        {
            InitializeComponent();
            _userService = new UserService(App.Database);
            _roleService = new RoleService(App.Database);
            _customFieldService = new CustomFieldService(App.Database);
            _locationService = new LocationService(App.Database);
            _backupService = new BackupService(App.Database, App.DbPath);

            Loaded += (s, e) => LoadAll();
        }

        // ── LOAD ALL ─────────────────────────────────────────────────────────

        private void LoadAll()
        {
            ApplyPermissions();
            LoadUsers();
            LoadRoles();
            LoadCustomFields();
            LoadLocations();
            LoadBackupHistory();
            LoadAppSettings();
        }

        private void ApplyPermissions()
        {
            // Users tab
            PermissionHelper.Apply(AddUserBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(ChangePasswordBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(ToggleActiveBtn, Permissions.ManageUsers, hideInstead: true);

            // Roles tab
            PermissionHelper.Apply(AddRoleBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(EditRoleBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(DeleteRoleBtn, Permissions.ManageUsers, hideInstead: true);

            // Custom fields tab
            PermissionHelper.Apply(AddFieldBtn, Permissions.ManageCustomFields, hideInstead: true);
            PermissionHelper.Apply(EditFieldBtn, Permissions.ManageCustomFields, hideInstead: true);
            PermissionHelper.Apply(ToggleFieldBtn, Permissions.ManageCustomFields, hideInstead: true);

            // Archive structure tab
            PermissionHelper.Apply(EditLocBtn, Permissions.ManageArchiveStructure, hideInstead: true);
            PermissionHelper.Apply(ToggleLocBtn, Permissions.ManageArchiveStructure, hideInstead: true);

            // Backup tab
            PermissionHelper.Apply(CreateBackupBtn, Permissions.CreateBackup, hideInstead: true);
            PermissionHelper.Apply(RestoreBackupBtn, Permissions.RestoreBackup, hideInstead: true);
            PermissionHelper.Apply(CleanBackupsBtn, Permissions.CreateBackup, hideInstead: true);

            // App settings tab — SaveSettingsBtn permission
            PermissionHelper.Apply(SaveSettingsBtn, Permissions.ManageSettings, hideInstead: true);
        }

        private void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر مجلد حفظ النسخ الاحتياطية",
                Filter = "Folder|*.none",
                FileName = "اختر هذا المجلد",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            if (!string.IsNullOrWhiteSpace(BackupPathBox.Text)
                && System.IO.Directory.Exists(BackupPathBox.Text.Trim()))
            {
                dlg.InitialDirectory = BackupPathBox.Text.Trim();
            }

            if (dlg.ShowDialog() == true)
            {
                string folder = System.IO.Path.GetDirectoryName(dlg.FileName)
                             ?? System.IO.Path.GetFullPath(dlg.FileName);
                BackupPathBox.Text = folder;
            }
        }


        // ═════════════════════════════════════════════════════════════════════
        // USERS TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadUsers() => UsersGrid.ItemsSource = _userService.GetAllUsers();

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddUserDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) LoadUsers();
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not User user)
            { ShowMsg("يرجى اختيار مستخدم أولاً."); return; }

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل كلمة المرور الجديدة (6 أحرف على الأقل):",
                "تغيير كلمة المرور", "");

            if (string.IsNullOrWhiteSpace(input)) return;

            var error = _userService.ChangePassword(user.UserId, input);
            ShowMsg(error ?? "✅ تم تغيير كلمة المرور بنجاح.");
        }

        private void ToggleActive_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not User user)
            { ShowMsg("يرجى اختيار مستخدم أولاً."); return; }

            bool newState = !user.IsActive;
            string action = newState ? "تفعيل" : "تعطيل";

            if (MessageBox.Show($"هل تريد {action} حساب '{user.FullName}'؟",
                action, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            var err = _userService.UpdateUser(user.UserId, user.FullName,
                user.EmployeeNumber, newState);

            if (err != null) ShowMsg(err);
            else LoadUsers();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ROLES TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadRoles() => RolesGrid.ItemsSource = _roleService.GetAllRoles();

        private void AddRole_Click(object sender, RoutedEventArgs e)
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل اسم الدور الجديد:", "إضافة دور", "");

            if (string.IsNullOrWhiteSpace(input)) return;

            var err = _roleService.CreateRole(input, null);
            if (err != null) ShowMsg(err);
            else LoadRoles();
        }

        private void EditRole_Click(object sender, RoutedEventArgs e)
        {
            if (RolesGrid.SelectedItem is not Role role)
            { ShowMsg("يرجى اختيار دور أولاً."); return; }

            var dialog = new EditRoleDialog(role) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) LoadRoles();
        }

        private void DeleteRole_Click(object sender, RoutedEventArgs e)
        {
            if (RolesGrid.SelectedItem is not Role role)
            { ShowMsg("يرجى اختيار دور أولاً."); return; }

            if (MessageBox.Show($"هل تريد حذف الدور '{role.RoleName}'؟",
                "حذف دور", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            var err = _roleService.DeleteRole(role.RoleId);
            if (err != null) ShowMsg(err);
            else LoadRoles();
        }

        // ═════════════════════════════════════════════════════════════════════
        // CUSTOM FIELDS TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadCustomFields()
        {
            var fields = _customFieldService.GetAllFields()
                .Select(f => new CustomFieldRow
                {
                    CustomFieldId = f.CustomFieldId,
                    FieldKey = f.FieldKey,
                    ArabicLabel = f.ArabicLabel,
                    FieldType = f.FieldType,
                    IsRequired = f.IsRequired,
                    IsActive = f.IsActive,
                    ShowInEntry = f.ShowInEntry,
                    ShowInAllData = f.ShowInAllData,
                    ShowInReports = f.ShowInReports,
                    EnableStatistics = f.EnableStatistics,
                    AllowBulkUpdate = f.AllowBulkUpdate,
                    SuggestionLimit = f.SuggestionLimit,
                    SortOrder = f.SortOrder,
                    CreatedAt = f.CreatedAt
                }).ToList();

            FieldsGrid.ItemsSource = fields;
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AddEditCustomFieldDialog(null) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) LoadCustomFields();
        }

        private void EditField_Click(object sender, RoutedEventArgs e)
        {
            if (FieldsGrid.SelectedItem is not CustomFieldRow row)
            { ShowMsg("يرجى اختيار حقل أولاً."); return; }

            OpenEditFieldDialog(row.CustomFieldId);
        }

        private void FieldsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FieldsGrid.SelectedItem is not CustomFieldRow row) return;
            OpenEditFieldDialog(row.CustomFieldId);
        }

        private void OpenEditFieldDialog(int customFieldId)
        {
            var field = _customFieldService.GetAllFields()
                .FirstOrDefault(f => f.CustomFieldId == customFieldId);

            if (field == null) return;

            var dlg = new AddEditCustomFieldDialog(field) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) LoadCustomFields();
        }

        private void ToggleField_Click(object sender, RoutedEventArgs e)
        {
            if (FieldsGrid.SelectedItem is not CustomFieldRow row)
            { ShowMsg("يرجى اختيار حقل أولاً."); return; }

            var field = _customFieldService.GetAllFields()
                .FirstOrDefault(f => f.CustomFieldId == row.CustomFieldId);

            if (field == null) return;

            field.IsActive = !field.IsActive;
            string state = field.IsActive ? "تفعيل" : "تعطيل";

            if (MessageBox.Show($"هل تريد {state} حقل '{field.ArabicLabel}'؟",
                state, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            var err = _customFieldService.UpdateField(field);
            if (err != null) ShowMsg(err);
            else LoadCustomFields();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ARCHIVE STRUCTURE (LOCATIONS) TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadLocations()
            => LocationsGrid.ItemsSource = _locationService.GetAllLocations();

        private void AddLocation_Click(object sender, RoutedEventArgs e)
        {
            LocErrorBorder.Visibility = Visibility.Collapsed;

            if (!int.TryParse(LocHallwayBox.Text, out int h) || h <= 0 ||
                !int.TryParse(LocCabinetBox.Text, out int c) || c <= 0 ||
                !int.TryParse(LocShelfBox.Text, out int s) || s <= 0)
            {
                ShowLocError("يرجى إدخال أرقام الممر والكبينة والرف بشكل صحيح.");
                return;
            }

            int? capacity = int.TryParse(LocCapacityBox.Text, out int cap) && cap > 0
                ? cap : null;

            string? label = string.IsNullOrWhiteSpace(LocLabelBox.Text)
                ? null : LocLabelBox.Text.Trim();

            var err = _locationService.CreateLocation(h, c, s, label, capacity);
            if (err != null) { ShowLocError(err); return; }

            LocHallwayBox.Text = LocCabinetBox.Text = LocShelfBox.Text = string.Empty;
            LocLabelBox.Text = LocCapacityBox.Text = string.Empty;
            LoadLocations();
        }

        private void EditLocation_Click(object sender, RoutedEventArgs e)
        {
            if (LocationsGrid.SelectedItem is not Location loc)
            { ShowMsg("يرجى اختيار موقع أولاً."); return; }

            string? newLabel = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل التسمية الجديدة (اتركها فارغة لحذفها):",
                "تعديل التسمية", loc.Label ?? "");

            var err = _locationService.UpdateLocation(
                loc.LocationId,
                string.IsNullOrWhiteSpace(newLabel) ? null : newLabel.Trim(),
                loc.Capacity,
                loc.IsActive);

            if (err != null) ShowMsg(err);
            else LoadLocations();
        }

        private void ToggleLocation_Click(object sender, RoutedEventArgs e)
        {
            if (LocationsGrid.SelectedItem is not Location loc)
            { ShowMsg("يرجى اختيار موقع أولاً."); return; }

            bool newState = !loc.IsActive;
            string action = newState ? "تفعيل" : "تعطيل";

            if (MessageBox.Show(
                $"هل تريد {action} الموقع '{loc.DisplayName}'؟",
                action, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            var err = _locationService.UpdateLocation(
                loc.LocationId, loc.Label, loc.Capacity, newState);

            if (err != null) ShowMsg(err);
            else LoadLocations();
        }

        private void ShowLocError(string msg)
        {
            LocErrorText.Text = msg;
            LocErrorBorder.Visibility = Visibility.Visible;
        }

        // ═════════════════════════════════════════════════════════════════════
        // BACKUP TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadBackupHistory()
        {
            BackupHistoryGrid.ItemsSource = _backupService.GetBackupHistory(30);

            int days = GetRetentionDays();
            RetentionInfoText.Text = $"حذف النسخ التلقائية الأقدم من {days} يوم";
        }

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            var folder = _backupService.GetDefaultBackupFolder();
            var (err, path) = _backupService.CreateBackup(folder, "Manual");

            if (err != null)
                ShowBackupMsg(err, success: false);
            else
            {
                ShowBackupMsg(
                    $"✅ تم إنشاء النسخة الاحتياطية بنجاح:\n{System.IO.Path.GetFileName(path)}",
                    success: true);
                LoadBackupHistory();
            }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Database Backup (*.db)|*.db",
                Title = "اختر ملف النسخة الاحتياطية"
            };
            if (dlg.ShowDialog() != true) return;

            var confirm = MessageBox.Show(
                "⚠️ تحذير: سيتم استبدال قاعدة البيانات الحالية بالكامل بهذه النسخة.\n\n" +
                "جميع البيانات التي أضفتها بعد تاريخ النسخة ستُفقد.\n\n" +
                "سيتم أولاً إنشاء نسخة احتياطية من الوضع الحالي.\n\n" +
                "هل تريد المتابعة؟",
                "تأكيد الاستعادة",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var err = _backupService.RestoreBackup(dlg.FileName);
            if (err != null)
            {
                ShowBackupMsg(err, success: false);
                return;
            }

            MessageBox.Show(
                "✅ تمت الاستعادة بنجاح.\n\nيرجى إعادة تشغيل التطبيق لتحميل البيانات الجديدة.",
                "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CleanBackups_Click(object sender, RoutedEventArgs e)
        {
            int days = GetRetentionDays();
            var folder = _backupService.GetDefaultBackupFolder();

            var confirm = MessageBox.Show(
                $"سيتم حذف النسخ الاحتياطية التلقائية الأقدم من {days} يوم من المجلد:\n{folder}\n\nهل تريد المتابعة؟",
                "تأكيد التنظيف", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            _backupService.CleanOldBackups(folder, days);
            ShowBackupMsg("✅ تم تنظيف النسخ القديمة.", success: true);
            LoadBackupHistory();
        }

        private void RefreshBackupHistory_Click(object sender, RoutedEventArgs e)
            => LoadBackupHistory();

        private void ShowBackupMsg(string msg, bool success)
        {
            BackupMsgText.Text = msg;
            BackupMsgBorder.Background = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
            BackupMsgBorder.BorderBrush = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF9A9A"));
            BackupMsgBorder.BorderThickness = new Thickness(1);
            BackupMsgText.Foreground = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
            BackupMsgBorder.Visibility = Visibility.Visible;
        }

        private int GetRetentionDays()
        {
            try
            {
                using var conn = App.Database.CreateConnection();
                var v = conn.ExecuteScalar<string?>(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'BackupRetentionDays'");
                if (v != null && int.TryParse(v, out int d)) return d;
            }
            catch { /* ignore */ }
            return 365;
        }

        // ═════════════════════════════════════════════════════════════════════
        // APP SETTINGS TAB
        // ═════════════════════════════════════════════════════════════════════

        private void LoadAppSettings()
        {
            try
            {
                using var conn = App.Database.CreateConnection();
                var rows = conn.Query<AppSetting>(
                    "SELECT SettingKey, SettingValue FROM AppSettings").AsList();

                var map = rows.ToDictionary(r => r.SettingKey, r => r.SettingValue);

                BackupPathBox.Text = GetSetting(map, SettingKeys.BackupPath, "");
                RetentionDaysBox.Text = GetSetting(map, SettingKeys.BackupRetentionDays, "365");

                PreventDuplicateChk.IsChecked = GetBool(map, SettingKeys.PreventDuplicatePrisonerNumber);
                RequireExactTenChk.IsChecked = GetBool(map, SettingKeys.RequireExactTenDigitNumber);
                RequireLocationChk.IsChecked = GetBool(map, SettingKeys.RequireLocation);
                RequireMovementReasonChk.IsChecked = GetBool(map, SettingKeys.RequireMovementReason);

                AuditEditsChk.IsChecked = GetBool(map, SettingKeys.AuditEditsEnabled, def: true);
                AuditPrintingChk.IsChecked = GetBool(map, SettingKeys.AuditPrintingEnabled, def: true);
                AuditImportsChk.IsChecked = GetBool(map, SettingKeys.AuditImportsEnabled, def: true);
            }
            catch (Exception ex)
            {
                ShowSettingsMsg($"خطأ أثناء تحميل الإعدادات: {ex.Message}", success: false);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsMsgBorder.Visibility = Visibility.Collapsed;

            if (!int.TryParse(RetentionDaysBox.Text, out int retDays) || retDays < 1)
            {
                ShowSettingsMsg("مدة الاحتفاظ يجب أن تكون رقماً موجباً.", success: false);
                return;
            }

            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;
                using var conn = App.Database.CreateConnection();

                SaveSetting(conn, SettingKeys.BackupPath,
                    BackupPathBox.Text.Trim(), now, userId);
                SaveSetting(conn, SettingKeys.BackupRetentionDays,
                    retDays.ToString(), now, userId);
                SaveSetting(conn, SettingKeys.PreventDuplicatePrisonerNumber,
                    BoolStr(PreventDuplicateChk), now, userId);
                SaveSetting(conn, SettingKeys.RequireExactTenDigitNumber,
                    BoolStr(RequireExactTenChk), now, userId);
                SaveSetting(conn, SettingKeys.RequireLocation,
                    BoolStr(RequireLocationChk), now, userId);
                SaveSetting(conn, SettingKeys.RequireMovementReason,
                    BoolStr(RequireMovementReasonChk), now, userId);
                SaveSetting(conn, SettingKeys.AuditEditsEnabled,
                    BoolStr(AuditEditsChk), now, userId);
                SaveSetting(conn, SettingKeys.AuditPrintingEnabled,
                    BoolStr(AuditPrintingChk), now, userId);
                SaveSetting(conn, SettingKeys.AuditImportsEnabled,
                    BoolStr(AuditImportsChk), now, userId);

                conn.Execute(@"
                    INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                    VALUES (@UserId, 'SettingsChanged', 'تم تعديل إعدادات النظام', @Now)",
                    new { UserId = userId, Now = now });

                ShowSettingsMsg("✅ تم حفظ الإعدادات بنجاح.", success: true);
                LoadBackupHistory();
            }
            catch (Exception ex)
            {
                ShowSettingsMsg($"خطأ أثناء الحفظ: {ex.Message}", success: false);
            }
        }

        //private void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
        //{
        //    var dialog = new System.Windows.Forms.FolderBrowserDialog
        //    {
        //        Description = "اختر مجلد حفظ النسخ الاحتياطية",
        //        UseDescriptionForTitle = true,
        //        SelectedPath = BackupPathBox.Text.Trim()
        //    };

        //    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        //        BackupPathBox.Text = dialog.SelectedPath;
        //}

        private void ShowSettingsMsg(string msg, bool success)
        {
            SettingsMsgText.Text = msg;
            SettingsMsgBorder.Background = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
            SettingsMsgBorder.BorderBrush = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF9A9A"));
            SettingsMsgBorder.BorderThickness = new Thickness(1);
            SettingsMsgText.Foreground = success
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828"));
            SettingsMsgBorder.Visibility = Visibility.Visible;
        }

        // ── Settings helpers ──────────────────────────────────────────────────

        private static string GetSetting(
            Dictionary<string, string> map, string key, string defaultVal)
            => map.TryGetValue(key, out var v) ? v : defaultVal;

        private static bool GetBool(
            Dictionary<string, string> map, string key, bool def = false)
        {
            if (!map.TryGetValue(key, out var v)) return def;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string BoolStr(CheckBox chk)
            => chk.IsChecked == true ? "true" : "false";

        private static void SaveSetting(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            string key, string value, string now, int userId)
        {
            conn.Execute(@"
                INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAt, UpdatedByUserId)
                VALUES (@Key, @Value, @Now, @UserId)
                ON CONFLICT(SettingKey)
                DO UPDATE SET SettingValue = @Value,
                              UpdatedAt = @Now,
                              UpdatedByUserId = @UserId",
                new { Key = key, Value = value, Now = now, UserId = userId });
        }

        // ═════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private void ShowMsg(string msg) =>
            MessageBox.Show(msg, "أرشيف الملفات",
                MessageBoxButton.OK, MessageBoxImage.Information);

        private void NumberOnly(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }
}