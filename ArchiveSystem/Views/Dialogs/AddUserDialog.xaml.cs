using System.Windows;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Views.Dialogs
{
    public partial class AddUserDialog : Window
    {
        private readonly UserService _userService;
        private readonly RoleService _roleService;

        public AddUserDialog()
        {
            InitializeComponent();
            _userService = new UserService(App.Database);
            _roleService = new RoleService(App.Database);
            Loaded += (s, e) => LoadRoles();
        }

        private void LoadRoles()
        {
            RoleCombo.ItemsSource = _roleService.GetAllRoles();
            if (RoleCombo.Items.Count > 0)
                RoleCombo.SelectedIndex = 0;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;

            if (RoleCombo.SelectedValue is not int roleId)
            {
                ShowError("يرجى اختيار الدور.");
                return;
            }

            var error = _userService.CreateUser(
                FullNameBox.Text,
                UsernameBox.Text,
                PasswordBox.Password,
                roleId,
                EmployeeNumberBox.Text.Trim() == "" ? null : EmployeeNumberBox.Text.Trim());

            if (error != null) { ShowError(error); return; }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}