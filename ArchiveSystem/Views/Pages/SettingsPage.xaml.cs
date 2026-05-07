using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using Dapper;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        private string _selectedThemeColor = "#178567";


        // ── Theme colour presets ──────────────────────────────────────────────────

        private static readonly (string Hex, string Label)[] ThemePresets =
        {
            ("#178567", "أخضر فيروزي (افتراضي)"),
            ("#1565C0", "أزرق"),
            ("#6A1B9A", "بنفسجي"),
            ("#AD1457", "وردي داكن"),
            ("#C62828", "أحمر"),
            ("#E65100", "برتقالي"),
            ("#558B2F", "أخضر زيتي"),
            ("#37474F", "رمادي أردوازي"),
        };

        private void BuildColorSwatches(string activeHex)
        {
            ColorSwatchPanel.Children.Clear();

            foreach (var (hex, label) in ThemePresets)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                bool isActive = string.Equals(hex, activeHex,
                    StringComparison.OrdinalIgnoreCase);

                var swatch = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(0, 0, 10, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = label,
                    BorderThickness = new Thickness(isActive ? 3 : 0),
                    BorderBrush = Brushes.White,
                    Effect = isActive
                        ? (System.Windows.Media.Effects.Effect)
                          new System.Windows.Media.Effects.DropShadowEffect
                          {
                              Color = color,
                              BlurRadius = 10,
                              Opacity = 0.7,
                              ShadowDepth = 0
                          }
                        : null
                };

                string capturedHex = hex;
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    _selectedThemeColor = capturedHex;
                    SelectedColorText.Text = $"اللون المختار: {capturedHex}  ({label})";
                    BuildColorSwatches(capturedHex);   // refresh ring
                };

                ColorSwatchPanel.Children.Add(swatch);
            }
        }

        /// <summary>Applies a hex color to the running MaterialDesign theme palette.</summary>
        private static void ApplyThemeColor(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var helper = new MaterialDesignThemes.Wpf.PaletteHelper();
                var theme = helper.GetTheme();
                theme.SetPrimaryColor(color);
                theme.SetSecondaryColor(color);
                helper.SetTheme(theme);
            }
            catch { /* ignore invalid hex */ }
        }

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
            AppVersionLabel.Text = App.AppVersion;
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

            // Field definition buttons — full schema access required
            PermissionHelper.Apply(AddFieldBtn, Permissions.ManageCustomFields, hideInstead: true);
            PermissionHelper.Apply(EditFieldBtn, Permissions.ManageCustomFields, hideInstead: true);
            PermissionHelper.Apply(ToggleFieldBtn, Permissions.ManageCustomFields, hideInstead: true);
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

        private void ApplyCustomHex_Click(object sender, RoutedEventArgs e)
        {
            string hex = CustomHexBox.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex, @"^#[0-9A-Fa-f]{6}$"))
            {
                ShowMsg("صيغة اللون غير صحيحة. مثال: #1a7a60");
                return;
            }
            _selectedThemeColor = hex;
            SelectedColorText.Text = $"اللون المختار: {hex}";
            BuildColorSwatches(hex);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ARCHIVE STRUCTURE (LOCATIONS) TAB
        // ═════════════════════════════════════════════════════════════════════
        private void LoadLocations()
            => LocationsGrid.ItemsSource = _locationService.GetOccupancy();

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
            if (LocationsGrid.SelectedItem is not LocationService.LocationOccupancy loc)
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
            if (LocationsGrid.SelectedItem is not LocationService.LocationOccupancy loc)
            { ShowMsg("يرجى اختيار موقع أولاً."); return; }

            bool newState = !loc.IsActive;
            string action = newState ? "تفعيل" : "تعطيل";

            if (MessageBox.Show(
                $"هل تريد {action} الموقع '{loc.Display}'؟",
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

                BackupTimeBox.Text = GetSetting(map, SettingKeys.BackupTime, "02:00");

                // Font scale
                foreach (ComboBoxItem item in FontScaleCombo.Items)
                {
                    if (item.Tag?.ToString() == GetSetting(map, SettingKeys.FontScale, FontScaleManager.KeyNormal))
                    {
                        FontScaleCombo.SelectedItem = item;
                        break;
                    }
                }
                if (FontScaleCombo.SelectedItem == null)
                    FontScaleCombo.SelectedIndex = 0;

                foreach (ComboBoxItem item in DensityCombo.Items)
                {
                    if (item.Tag?.ToString() == GetSetting(map, SettingKeys.Density, "Comfortable"))
                    { DensityCombo.SelectedItem = item; break; }
                }
                if (DensityCombo.SelectedItem == null) DensityCombo.SelectedIndex = 0;

                _selectedThemeColor = GetSetting(map, SettingKeys.ThemeColor, "#178567");
                BuildColorSwatches(_selectedThemeColor);
                SelectedColorText.Text = $"اللون الحالي: {_selectedThemeColor}";

            }
            catch (Exception ex)
            {
                ShowSettingsMsg($"خطأ أثناء تحميل الإعدادات: {ex.Message}", success: false);
            }
        }

        private static void ApplyDensity(string density)
        {
            double padding = density == "Compact" ? 4 : 8;
            // Apply via a global style override — simplest approach is ResourceDictionary merge
            // For now, store and read on next launch (no runtime re-render needed)
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

                // ── Read the current (old) settings BEFORE any writes ─────────
                var oldRows = Dapper.SqlMapper.Query<AppSetting>(conn,
                    "SELECT SettingKey, SettingValue FROM AppSettings").AsList();
                var oldMap = oldRows.ToDictionary(r => r.SettingKey, r => r.SettingValue);

                // ── Build the new settings map (mirrors what we are about to save)
                string fontScaleKey = (FontScaleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)
                                          ?.Tag?.ToString()
                                      ?? FontScaleManager.KeyNormal;

                var newMap = new Dictionary<string, string>
                {
                    [SettingKeys.BackupPath] = BackupPathBox.Text.Trim(),
                    [SettingKeys.BackupRetentionDays] = retDays.ToString(),
                    [SettingKeys.PreventDuplicatePrisonerNumber] = BoolStr(PreventDuplicateChk),
                    [SettingKeys.RequireExactTenDigitNumber] = BoolStr(RequireExactTenChk),
                    [SettingKeys.RequireLocation] = BoolStr(RequireLocationChk),
                    [SettingKeys.RequireMovementReason] = BoolStr(RequireMovementReasonChk),
                    [SettingKeys.AuditEditsEnabled] = BoolStr(AuditEditsChk),
                    [SettingKeys.AuditPrintingEnabled] = BoolStr(AuditPrintingChk),
                    [SettingKeys.AuditImportsEnabled] = BoolStr(AuditImportsChk),
                    [SettingKeys.FontScale] = fontScaleKey,
                    [SettingKeys.ThemeColor] = _selectedThemeColor,
                    [SettingKeys.Density] = (DensityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Comfortable",
                    [SettingKeys.BackupTime] = BackupTimeBox.Text.Trim(),
                };

                // ── Compute diff: only keys whose value actually changed ────────
                var changedKeys = newMap.Keys
                    .Where(k => !oldMap.TryGetValue(k, out var ov) || ov != newMap[k])
                    .ToList();

                // ── Persist all settings ───────────────────────────────────────
                foreach (var (key, value) in newMap)
                    SaveSetting(conn, key, value, now, userId);

                // ── Apply live changes ─────────────────────────────────────────
                ApplyThemeColor(_selectedThemeColor);
                App.FontScaleSetting = fontScaleKey;
                FontScaleManager.ReApplyToMainWindow(FontScaleManager.ToMultiplier(fontScaleKey));

                // ── Build JSON snapshots of the changed keys only ──────────────
                string? oldJson = null;
                string? newJson = null;

                if (changedKeys.Count > 0)
                {
                    var oldSnapshot = changedKeys.ToDictionary(
                        k => k,
                        k => oldMap.TryGetValue(k, out var v) ? v : null);

                    var newSnapshot = changedKeys.ToDictionary(
                        k => k,
                        k => newMap[k]);

                    oldJson = System.Text.Json.JsonSerializer.Serialize(oldSnapshot);
                    newJson = System.Text.Json.JsonSerializer.Serialize(newSnapshot);
                }

                // ── Audit ──────────────────────────────────────────────────────
                conn.Execute(@"
                    INSERT INTO AuditLog
                        (UserId, ActionType, Description, OldValueJson, NewValueJson, CreatedAt)
                    VALUES
                        (@UserId, 'SettingsChanged', @Desc, @OldJson, @NewJson, @Now)",
                    new
                    {
                        UserId = userId,
                        Desc = changedKeys.Count > 0
                            ? $"تم تعديل {changedKeys.Count} إعداد: {string.Join("، ", changedKeys)}"
                            : "تم حفظ الإعدادات (بدون تغييرات)",
                        OldJson = oldJson,
                        NewJson = newJson,
                        Now = now
                    });

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