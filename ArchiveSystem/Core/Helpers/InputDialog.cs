using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArchiveSystem.Core.Helpers
{
    /// <summary>
    /// WPF-safe replacement for Microsoft.VisualBasic.Interaction.InputBox.
    /// Works correctly in both Debug and published Release builds.
    /// </summary>
    public static class InputDialog
    {
        /// <summary>
        /// Shows a simple text-input dialog.
        /// Returns the entered text, or null if the user cancelled.
        /// </summary>
        public static string? Show(
            string prompt,
            string title = "",
            string defaultValue = "",
            Window? owner = null)
        {
            string? result = null;

            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 290,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                FlowDirection = FlowDirection.RightToLeft,
                Background = System.Windows.Media.Brushes.WhiteSmoke,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            // Prompt label
            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = new System.Windows.Media.FontFamily("Noto Kufi Arabic, Segoe UI")
            });

            // Text input
            var tb = new TextBox
            {
                Text = defaultValue,
                Height = 46,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 14),
                FontFamily = new System.Windows.Media.FontFamily("Noto Kufi Arabic, Segoe UI"),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#B0BEC5")),
                BorderThickness = new Thickness(1)
            };
            tb.SelectAll();

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var okBtn = new Button
            {
                Content = "موافق",
                Width = 90,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                FontFamily = new System.Windows.Media.FontFamily("Noto Kufi Arabic, Segoe UI"),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#1a7a60")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var cancelBtn = new Button
            {
                Content = "إلغاء",
                Width = 80,
                Height = 34,
                FontFamily = new System.Windows.Media.FontFamily("Noto Kufi Arabic, Segoe UI"),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString("#B0BEC5")),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            okBtn.Click += (_, _) =>
            {
                result = tb.Text;
                win.DialogResult = true;
                win.Close();
            };

            cancelBtn.Click += (_, _) =>
            {
                result = null;
                win.DialogResult = false;
                win.Close();
            };

            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = tb.Text;
                    win.DialogResult = true;
                    win.Close();
                }
                else if (e.Key == Key.Escape)
                {
                    win.DialogResult = false;
                    win.Close();
                }
            };

            btnRow.Children.Add(okBtn);
            btnRow.Children.Add(cancelBtn);
            panel.Children.Add(tb);
            panel.Children.Add(btnRow);
            win.Content = panel;

            win.Loaded += (_, _) =>
            {
                tb.Focus();
                tb.SelectAll();
            };

            win.ShowDialog();
            return result;
        }
    }
}