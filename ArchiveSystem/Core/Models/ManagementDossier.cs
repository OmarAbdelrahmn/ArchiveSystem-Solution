namespace ArchiveSystem.Core.Models
{
    public class ManagementDossier
    {
        public int ManagementDossierId { get; set; }
        public int ManagementId { get; set; }
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int? TypeId { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = "Open";
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
        public string? DeletedAt { get; set; }

        // joined — not in DB
        public string? TypeName { get; set; }
        public string? ManagementName { get; set; }

        public string HijriDisplay => $"{HijriMonth}/{HijriYear}هـ";
        public string StatusDisplay => DeletedAt != null ? "محذوف" : Status == "Open" ? "مفتوح" : Status;
    }
}