using System.Windows;
using System.Windows.Input;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;
        private bool _isFirstRun = false;
        private bool _passwordVisible = false;

        public LoginWindow()
        {
            InitializeComponent();
            FontScaleManager.Apply(this, FontScaleManager.ToMultiplier(App.FontScaleSetting));
            _authService = new AuthService(App.Database);
            Loaded += LoginWindow_Loaded;
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_passwordVisible)
            {
                PasswordBox.Password = PasswordVisibleBox.Text;
                PasswordVisibleBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                _passwordVisible = false;
            }
            else
            {
                PasswordVisibleBox.Text = PasswordBox.Password;
                PasswordVisibleBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                _passwordVisible = true;
            }
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Check if this is first run (no users exist)
            _isFirstRun = !_authService.HasUsers();

            if (_isFirstRun)
            {
                SetupBorder.Visibility = Visibility.Visible;
                FullNameBox.Visibility = Visibility.Visible;
                LoginButton.Content = "إنشاء الحساب وتسجيل الدخول";
                SubtitleText.Text = "أول تشغيل — أنشئ حساب مدير الأرشيف للبدء.";
            }
            var lastBackup = _authService.GetLastBackupTime();

            if (DateTime.TryParse(lastBackup, out var parsedDate))
            {
                LastBackupTimeText.Text = parsedDate.ToString("HH:mm yyyy-MM-dd");
            }
            else
            {
                LastBackupTimeText.Text = "--:--";
            }

            // Total files & folders — load from DB if the service exposes them,
            // otherwise fall back to zero placeholders.
            try
            {
                long fileCount = _authService.GetTotalFileCount();
                long folderCount = _authService.GetTotalFolderCount();
                TotalFilesText.Text = fileCount.ToString("N0");
                TotalFoldersText.Text = folderCount.ToString("N0");
            }
            catch
            {
                TotalFilesText.Text = "—";
                TotalFoldersText.Text = "—";
            }

            UsernameBox.Focus();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e) => DoLogin();

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoLogin();
        }

        private void DoLogin()
        {
            HideError();

            string username = UsernameBox.Text.Trim();
            string password = _passwordVisible ? PasswordVisibleBox.Text : PasswordBox.Password;

            if (string.IsNullOrEmpty(username))
            {
                ShowError("يرجى إدخال اسم المستخدم.");
                UsernameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("يرجى إدخال كلمة المرور.");
                PasswordBox.Focus();
                return;
            }

            // First run — create admin account
            if (_isFirstRun)
            {
                string fullName = FullNameBox.Text.Trim();

                if (string.IsNullOrEmpty(fullName))
                {
                    ShowError("يرجى إدخال الاسم الكامل.");
                    FullNameBox.Focus();
                    return;
                }

                if (password.Length < 6)
                {
                    ShowError("كلمة المرور يجب أن تكون 6 أحرف على الأقل.");
                    PasswordBox.Focus();
                    return;
                }

                bool created = _authService.CreateFirstAdmin(fullName, username, password);
                if (!created)
                {
                    ShowError("تعذر إنشاء الحساب. يرجى إعادة المحاولة.");
                    return;
                }
            }

            // Attempt login
            var user = _authService.Login(username, password);

            if (user == null)
            {
                ShowError("اسم المستخدم أو كلمة المرور غير صحيحة.");
                PasswordBox.Clear();
                PasswordVisibleBox.Clear();
                PasswordBox.Focus();
                return;
            }

            // ✅ Login successful — open main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
        }
    }
}