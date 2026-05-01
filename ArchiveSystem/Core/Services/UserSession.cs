using ArchiveSystem.Core.Models;

namespace ArchiveSystem.Core.Services
{
    public static class UserSession
    {
        public static User? CurrentUser { get; private set; }
        public static bool IsLoggedIn => CurrentUser != null;

        public static void Login(User user)
        {
            CurrentUser = user;
        }

        public static void Logout()
        {
            CurrentUser = null;
        }

        public static bool HasPermission(string permissionKey)
        {
            if (CurrentUser == null) return false;
            return _permissions.Contains(permissionKey);
        }

        private static readonly HashSet<string> _permissions = new();

        public static void SetPermissions(IEnumerable<string> permissions)
        {
            _permissions.Clear();
            foreach (var p in permissions)
                _permissions.Add(p);
        }
    }
}