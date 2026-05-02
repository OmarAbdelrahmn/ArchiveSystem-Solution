using System.Windows;
using System.Windows.Controls;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class EditRoleDialog : Window
    {
        private readonly RoleService _roleService;
        private readonly Role _role;

        // all possible permissions
        private List<PermissionEntry> _allPermissions = new();

        // currently assigned permissions
        private List<PermissionEntry> _assignedPermissions = new();

        public EditRoleDialog(Role role)
        {
            InitializeComponent();
            _roleService = new RoleService(App.Database);
            _role = role;
            Loaded += (s, e) => LoadData();
        }

        private void LoadData()
        {
            RoleNameBox.Text = _role.RoleName;

            // get all permissions with IsAllowed flag for this role
            var all = _roleService.GetAllPermissionsForRole(_role.RoleId);

            // split into assigned and available
            _assignedPermissions = all.Where(p => p.IsAllowed).ToList();
            RefreshAvailableDropdown(all);
            RefreshAssignedList();
        }

        // Dropdown shows only permissions NOT yet assigned
        private void RefreshAvailableDropdown(List<PermissionEntry>? all = null)
        {
            all ??= _roleService.GetAllPermissionsForRole(_role.RoleId);

            var assignedKeys = _assignedPermissions
                .Select(p => p.PermissionKey)
                .ToHashSet();

            _allPermissions = all
                .Where(p => !assignedKeys.Contains(p.PermissionKey))
                .ToList();

            PermissionCombo.ItemsSource = _allPermissions;
            PermissionCombo.SelectedIndex = _allPermissions.Count > 0 ? 0 : -1;
        }

        private void RefreshAssignedList()
        {
            AssignedList.ItemsSource = null;
            AssignedList.ItemsSource = _assignedPermissions;
        }

        private void AddPermission_Click(object sender, RoutedEventArgs e)
        {
            if (PermissionCombo.SelectedItem is not PermissionEntry selected)
            {
                ShowError("يرجى اختيار صلاحية من القائمة.");
                return;
            }

            HideError();

            // save to DB immediately
            _roleService.SetPermission(_role.RoleId, selected.PermissionKey, true);

            // move from available to assigned
            selected.IsAllowed = true;
            _assignedPermissions.Add(selected);
            _allPermissions.Remove(selected);

            PermissionCombo.ItemsSource = _allPermissions;
            PermissionCombo.SelectedIndex = _allPermissions.Count > 0 ? 0 : -1;

            RefreshAssignedList();
        }

        private void RemovePermission_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not string permKey) return;

            var entry = _assignedPermissions
                .FirstOrDefault(p => p.PermissionKey == permKey);
            if (entry == null) return;

            // save to DB immediately
            _roleService.SetPermission(_role.RoleId, permKey, false);

            // move from assigned back to available
            entry.IsAllowed = false;
            _assignedPermissions.Remove(entry);
            _allPermissions.Add(entry);

            // sort available list by Arabic label
            _allPermissions = _allPermissions
                .OrderBy(p => p.ArabicLabel)
                .ToList();

            PermissionCombo.ItemsSource = _allPermissions;
            PermissionCombo.SelectedIndex = _allPermissions.Count > 0 ? 0 : -1;

            RefreshAssignedList();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            HideError();

            var error = _roleService.UpdateRole(
                _role.RoleId,
                RoleNameBox.Text,
                null);

            if (error != null) { ShowError(error); return; }

            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
        }
    }
}