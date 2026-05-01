namespace ArchiveSystem.Core.Models
{
    public class Record
    {
        public int RecordId { get; set; }
        public int DossierId { get; set; }
        public int SequenceNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string PrisonerNumber { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string Status { get; set; } = "Active";
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
        public string? DeletedAt { get; set; }
        public int? DeletedByUserId { get; set; }
        public int? ImportBatchId { get; set; }
    }
}