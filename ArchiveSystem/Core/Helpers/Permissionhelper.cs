using System.Windows;
using System.Windows.Controls;
using ArchiveSystem.Core.Models;
using ArchiveSystem.Core.Services;

namespace ArchiveSystem.Core.Helpers
{
    /// <summary>
    /// Thin helper so every page can enforce permissions in one line per button.
    /// Usage:
    ///   PermissionHelper.Apply(someButton, Permissions.DeleteRecord);
    ///   PermissionHelper.Apply(someButton, Permissions.EditRecord, hideInstead: true);
    /// </summary>
    public static class PermissionHelper
    {
        // ── Core check ────────────────────────────────────────────────────────

        public static bool Can(string permissionKey)
            => UserSession.HasPermission(permissionKey);

        // ── Apply to a single control ─────────────────────────────────────────

        /// <summary>
        /// If the current user lacks the permission:
        ///   hideInstead = false (default) → disables the control
        ///   hideInstead = true            → collapses the control
        /// </summary>
        public static void Apply(Control control, string permissionKey,
            bool hideInstead = false)
        {
            bool allowed = Can(permissionKey);

            if (hideInstead)
                control.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            else
                control.IsEnabled = allowed;
        }

        /// <summary>Convenience overload for TabItem (which is not a Control).</summary>
        public static void Apply(TabItem tab, string permissionKey)
        {
            bool allowed = Can(permissionKey);
            tab.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Convenience overload for MenuItem.</summary>
        public static void Apply(MenuItem item, string permissionKey,
            bool hideInstead = true)
        {
            bool allowed = Can(permissionKey);
            if (hideInstead)
                item.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            else
                item.IsEnabled = allowed;
        }

        // ── Apply to multiple controls sharing one permission ─────────────────

        public static void ApplyMany(string permissionKey,
            bool hideInstead = false,
            params Control[] controls)
        {
            foreach (var c in controls)
                Apply(c, permissionKey, hideInstead);
        }

        // ── Page-level redirect guard ─────────────────────────────────────────

        /// <summary>
        /// Call at the top of a page's Loaded handler.
        /// If the user lacks the permission, navigates back and shows a message.
        /// Returns true if access is denied (caller should return immediately).
        /// </summary>
        public static bool DenyPage(System.Windows.Controls.Page page,
            string permissionKey)
        {
            if (Can(permissionKey)) return false;

            MessageBox.Show(
                "ليس لديك صلاحية للوصول إلى هذه الصفحة.",
                "وصول مرفوض",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            page.NavigationService?.GoBack();
            return true;
        }
    }
}