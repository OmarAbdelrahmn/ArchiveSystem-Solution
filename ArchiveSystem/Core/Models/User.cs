namespace ArchiveSystem.Core.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? EmployeeNumber { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string? PasswordSalt { get; set; }
        public bool IsActive { get; set; } = true;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
        public string? LastLoginAt { get; set; }
    }
}