using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;
using System.Windows;
using System.Windows.Media;

namespace ArchiveSystem.Core.Services
{
    /// <summary>
    /// Reads the FontScale app-setting and applies a WPF LayoutTransform to any
    /// Window so that all text and controls scale proportionally without touching
    /// individual FontSize attributes in XAML.
    ///
    /// Supported keys (stored in AppSettings as SettingKeys.FontScale):
    ///   "Normal"  → 1.00×  (default, identity transform)
    ///   "Large"   → 1.25×
    ///
    /// How it works:
    ///   A ScaleTransform on Window.Content causes WPF to divide available
    ///   logical pixels by the scale factor during measure, then render at
    ///   scale× — so 13-pt text reads as ~16-pt on screen at Large, every
    ///   padding and icon grows proportionally, and no XAML needs changing.
    /// </summary>
    public static class FontScaleManager
    {
        // ── Public scale-key constants ────────────────────────────────────────
        public const string KeyNormal = "Normal";
        public const string KeyLarge = "Large";

        private static double _current = 1.0;

        /// <summary>The multiplier most recently applied to a window.</summary>
        public static double CurrentScale => _current;

        // ── Conversion ────────────────────────────────────────────────────────

        /// <summary>"Normal" → 1.0 · "Large" → 1.25</summary>
        public static double ToMultiplier(string? key) => key switch
        {
            KeyLarge => 1.25,
            _ => 1.0
        };

        /// <summary>Inverse of <see cref="ToMultiplier"/>.</summary>
        public static string ToKey(double multiplier) =>
            multiplier >= 1.2 ? KeyLarge : KeyNormal;

        // ── Read + apply on startup ───────────────────────────────────────────

        /// <summary>
        /// Reads FontScale from the database and applies the resulting
        /// LayoutTransform to <paramref name="window"/>.
        /// Safe to call before the window is visible; silently falls back to
        /// Normal if the database is not yet seeded.
        /// </summary>
        public static void LoadAndApply(DatabaseContext db, Window window)
        {
            string key = KeyNormal;
            try
            {
                using var conn = db.CreateConnection();
                key = conn.ExecuteScalar<string?>(
                          "SELECT SettingValue FROM AppSettings WHERE SettingKey = @K",
                          new { K = SettingKeys.FontScale })
                      ?? KeyNormal;
            }
            catch { /* DB not yet seeded — use Normal */ }

            Apply(window, ToMultiplier(key));
        }

        // ── Core apply ────────────────────────────────────────────────────────

        /// <summary>
        /// Applies <paramref name="scale"/> as a LayoutTransform to
        /// <paramref name="window"/>'s root content element.
        /// Passing 1.0 removes any existing transform (identity).
        /// </summary>
        public static void Apply(Window window, double scale)
        {
            _current = scale;

            if (window.Content is not UIElement root) return;

            root.SetValue(
                FrameworkElement.LayoutTransformProperty,
                scale == 1.0
                    ? Transform.Identity
                    : new ScaleTransform(scale, scale));
        }

        /// <summary>
        /// Updates the stored scale and immediately re-applies it to the running
        /// <see cref="Application.MainWindow"/> so the change is visible without
        /// restarting the application.
        /// </summary>
        public static void ReApplyToMainWindow(double scale)
        {
            _current = scale;
            if (Application.Current?.MainWindow is Window win)
                Apply(win, scale);
        }
    }
}