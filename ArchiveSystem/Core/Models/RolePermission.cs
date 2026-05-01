namespace ArchiveSystem.Core.Models
{
    public class RolePermission
    {
        public int RolePermissionId { get; set; }
        public int RoleId { get; set; }
        public string PermissionKey { get; set; } = string.Empty;
        public bool IsAllowed { get; set; } = false;
        public string? UpdatedAt { get; set; }
        public int? UpdatedByUserId { get; set; }
    }
}