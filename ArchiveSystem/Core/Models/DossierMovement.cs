namespace ArchiveSystem.Core.Models
{
    public class DossierMovement
    {
        public int MovementId { get; set; }
        public int DossierId { get; set; }
        public int? FromLocationId { get; set; }
        public int ToLocationId { get; set; }
        public string? Reason { get; set; }
        public int? MovedByUserId { get; set; }
        public string MovedAt { get; set; } = string.Empty;

        // joined — not stored in DB
        public Location? FromLocation { get; set; }
        public Location? ToLocation { get; set; }
        public string? MovedByUserName { get; set; }
    }
}