using System.IO;
using System.Windows;
using System.Windows.Input;
using ArchiveSystem.Core.Services;
using Dapper;
using Microsoft.Win32;

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
            RefreshLoginState();
            UsernameBox.Focus();
        }

        // ── Refresh the login screen state (called after restore too) ─────────

        private void RefreshLoginState()
        {
            _isFirstRun = !_authService.HasUsers();

            if (_isFirstRun)
            {
                SetupBorder.Visibility = Visibility.Visible;
                FullNameBox.Visibility = Visibility.Visible;
                LoginButton.Content = "إنشاء الحساب وتسجيل الدخول";
                SubtitleText.Text = "أول تشغيل — أنشئ حساب مدير الأرشيف للبدء.";
            }
            else
            {
                SetupBorder.Visibility = Visibility.Collapsed;
                FullNameBox.Visibility = Visibility.Collapsed;
                LoginButton.Content = "دخول إلى النظام";
                SubtitleText.Text = "أدخل بيانات حسابك للدخول إلى النظام.";
            }

            // Refresh stats
            try
            {
                var lastBackup = _authService.GetLastBackupTime();
                LastBackupTimeText.Text = DateTime.TryParse(lastBackup, out var dt)
                    ? dt.ToString("HH:mm yyyy-MM-dd")
                    : "--:--";

                TotalFilesText.Text = _authService.GetTotalFileCount().ToString("N0");
                TotalFoldersText.Text = _authService.GetTotalFolderCount().ToString("N0");
            }
            catch
            {
                LastBackupTimeText.Text = "--:--";
                TotalFilesText.Text = "—";
                TotalFoldersText.Text = "—";
            }
        }

        // ── RESTORE DATABASE ──────────────────────────────────────────────────

        /// <summary>
        /// Lets the user pick a .db backup file, copies it over the current
        /// database, reinitialises the connection, and refreshes the login screen.
        /// No login is required — this runs before authentication.
        /// </summary>
        private void RestoreDb_Click(object sender, RoutedEventArgs e)
        {
            // Warn the user clearly
            var confirm = MessageBox.Show(
                "⚠️ سيتم استبدال قاعدة البيانات الحالية بالملف الذي ستختاره.\n\n" +
                "• إذا كان الجهاز الحالي يحتوي على بيانات، ستُفقد جميعها.\n" +
                "• تأكد أن الملف الذي ستختاره هو نسخة احتياطية صحيحة من هذا النظام.\n\n" +
                "هل تريد المتابعة؟",
                "استعادة قاعدة البيانات",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // File picker
            var dlg = new OpenFileDialog
            {
                Title = "اختر ملف قاعدة البيانات (النسخة الاحتياطية)",
                Filter = "Database Files (*.db)|*.db|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            string sourcePath = dlg.FileName;

            try
            {
                // Basic sanity check — SQLite files start with "SQLite format 3"
                byte[] header = new byte[16];
                using (var fs = File.OpenRead(sourcePath))
                    fs.Read(header, 0, 16);

                string magic = System.Text.Encoding.ASCII.GetString(header);
                if (!magic.StartsWith("SQLite format 3"))
                {
                    MessageBox.Show(
                        "الملف المختار لا يبدو قاعدة بيانات صحيحة.\nيرجى اختيار ملف نسخة احتياطية بامتداد .db من هذا النظام.",
                        "ملف غير صالح",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ── Step 1: close current connection cleanly ─────────────────
                // The DatabaseContext doesn't hold a persistent connection
                // (each call to CreateConnection() opens a new one), but we
                // do a WAL checkpoint to flush any pending writes.
                try
                {
                    using var conn = App.Database.CreateConnection();
                    conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                }
                catch { /* ignore — db may be empty on first run */ }

                // ── Step 2: copy the chosen file over the current db ─────────
                string destPath = App.DbPath;
                File.Copy(sourcePath, destPath, overwrite: true);

                // ── Step 3: also copy WAL/SHM sidecars if present ────────────
                foreach (var sidecar in new[] { sourcePath + "-wal", sourcePath + "-shm" })
                {
                    if (File.Exists(sidecar))
                        File.Copy(sidecar, destPath + Path.GetExtension(sidecar), overwrite: true);
                    else
                    {
                        // Delete stale sidecars on the destination so SQLite
                        // doesn't try to replay an old WAL against the new db.
                        string destSidecar = destPath + Path.GetExtension(sidecar);
                        if (File.Exists(destSidecar)) File.Delete(destSidecar);
                    }
                }

                // ── Step 4: re-initialise the database layer ─────────────────
                // Re-run migrations so any schema additions in this version
                // are applied to the restored (possibly older) database.
                App.Database.InitializeDatabase();

                // ── Step 5: re-read font scale setting from the restored db ──
                try
                {
                    using var conn2 = App.Database.CreateConnection();
                    App.FontScaleSetting = conn2.ExecuteScalar<string>(
                        "SELECT SettingValue FROM AppSettings WHERE SettingKey = @K",
                        new { K = Core.Models.SettingKeys.FontScale })
                        ?? Core.Services.FontScaleManager.KeyNormal;
                }
                catch { /* keep current setting */ }

                // ── Step 6: refresh _authService and the login screen ─────────
                // AuthService holds a reference to App.Database which is the
                // same DatabaseContext object — it will now read the new file.
                HideError();
                UsernameBox.Text = string.Empty;
                PasswordBox.Clear();
                PasswordVisibleBox.Text = string.Empty;

                RefreshLoginState();

                MessageBox.Show(
                    "✅ تم استعادة قاعدة البيانات بنجاح.\n\nيمكنك الآن تسجيل الدخول بحسابك المعتاد.",
                    "تمت الاستعادة",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"خطأ أثناء استعادة قاعدة البيانات:\n{ex.Message}",
                    "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── LOGIN ─────────────────────────────────────────────────────────────

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

            var user = _authService.Login(username, password);

            if (user == null)
            {
                ShowError("اسم المستخدم أو كلمة المرور غير صحيحة.");
                PasswordBox.Clear();
                PasswordVisibleBox.Clear();
                PasswordBox.Focus();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        private void HideError()
            => ErrorBorder.Visibility = Visibility.Collapsed;
    }
}