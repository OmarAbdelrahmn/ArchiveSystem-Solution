namespace ArchiveSystem.Core.Models
{
    public class UserRole
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }
}