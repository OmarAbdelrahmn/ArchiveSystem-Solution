namespace ArchiveSystem.Core.Models
{
    public class ManagementDossierType
    {
        public int TypeId { get; set; }
        public int ManagementId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
    }
}