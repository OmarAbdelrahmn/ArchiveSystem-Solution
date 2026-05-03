using ArchiveSystem.Core.Helpers;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;
using ArchiveSystem.Views.Dialogs;
using System.Windows;
using System.Windows.Controls;

namespace ArchiveSystem.Views.Pages
{
    public partial class SettingsPage : Page
    {
        private readonly UserService _userService;
        private readonly RoleService _roleService;

        public SettingsPage()
        {
            InitializeComponent();
            _userService = new UserService(App.Database);
            _roleService = new RoleService(App.Database);
            Loaded += (s, e) => LoadAll();
        }

        private void LoadAll()
        {
            ApplyPermissions();
            LoadUsers();
            LoadRoles();
        }

        private void ApplyPermissions()
        {
            // Users tab buttons
            PermissionHelper.Apply(AddUserBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(ChangePasswordBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(ToggleActiveBtn, Permissions.ManageUsers, hideInstead: true);

            // Roles tab buttons
            PermissionHelper.Apply(AddRoleBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(EditRoleBtn, Permissions.ManageUsers, hideInstead: true);
            PermissionHelper.Apply(DeleteRoleBtn, Permissions.ManageUsers, hideInstead: true);
        }

        private void LoadUsers()
        {
            UsersGrid.ItemsSource = _userService.GetAllUsers();
        }

        private void LoadRoles()
        {
            RolesGrid.ItemsSource = _roleService.GetAllRoles();
        }

        // ── USERS ────────────────────────────────────

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddUserDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                LoadUsers();
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not User user)
            {
                ShowMessage("يرجى اختيار مستخدم أولاً.");
                return;
            }

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل كلمة المرور الجديدة (6 أحرف على الأقل):",
                "تغيير كلمة المرور", "");

            if (string.IsNullOrWhiteSpace(input)) return;

            var error = _userService.ChangePassword(user.UserId, input);
            ShowMessage(error ?? "✅ تم تغيير كلمة المرور بنجاح.");
        }

        private void ToggleActive_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not User user)
            {
                ShowMessage("يرجى اختيار مستخدم أولاً.");
                return;
            }

            bool newState = !user.IsActive;
            string action = newState ? "تفعيل" : "تعطيل";

            var result = MessageBox.Show(
                $"هل تريد {action} حساب '{user.FullName}'؟",
                action, MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var error = _userService.UpdateUser(
                user.UserId, user.FullName,
                user.EmployeeNumber, newState);

            if (error != null) ShowMessage(error);
            else LoadUsers();
        }

        // ── ROLES ────────────────────────────────────

        private void AddRole_Click(object sender, RoutedEventArgs e)
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "أدخل اسم الدور الجديد:",
                "إضافة دور", "");

            if (string.IsNullOrWhiteSpace(input)) return;

            var error = _roleService.CreateRole(input, null);
            if (error != null) ShowMessage(error);
            else LoadRoles();
        }

        private void EditRole_Click(object sender, RoutedEventArgs e)
        {
            if (RolesGrid.SelectedItem is not Role role)
            {
                ShowMessage("يرجى اختيار دور أولاً.");
                return;
            }

            var dialog = new EditRoleDialog(role)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
                LoadRoles();
        }

        private void DeleteRole_Click(object sender, RoutedEventArgs e)
        {
            if (RolesGrid.SelectedItem is not Role role)
            {
                ShowMessage("يرجى اختيار دور أولاً.");
                return;
            }

            var result = MessageBox.Show(
                $"هل تريد حذف الدور '{role.RoleName}'؟",
                "حذف دور",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var error = _roleService.DeleteRole(role.RoleId);
            if (error != null) ShowMessage(error);
            else LoadRoles();
        }

        private void ShowMessage(string msg)
        {
            MessageBox.Show(msg, "أرشيف الملفات",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}