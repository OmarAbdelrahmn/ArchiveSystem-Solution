namespace ArchiveSystem.Core.Models
{
    public class Management
    {
        public int ManagementId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ParentManagementId { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }

        // joined — not in DB
        public string? ParentName { get; set; }
        public int DossierCount { get; set; }

        public string DisplayName => ParentName != null
            ? $"  └ {Name}"
            : Name;
    }
}