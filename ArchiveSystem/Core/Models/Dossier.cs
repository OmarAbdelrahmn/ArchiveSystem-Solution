namespace ArchiveSystem.Core.Models
{
    public class Dossier
    {
        public int DossierId { get; set; }
        public int DossierNumber { get; set; }
        public int HijriMonth { get; set; }
        public int HijriYear { get; set; }
        public int? ExpectedFileCount { get; set; }
        public int? CurrentLocationId { get; set; }
        public string Status { get; set; } = "Open";
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
        public string? ClosedAt { get; set; }
        public int? ImportBatchId { get; set; }

        // joined — not stored in DB
        public Location? CurrentLocation { get; set; }
        public int ActualFileCount { get; set; }
    }
}