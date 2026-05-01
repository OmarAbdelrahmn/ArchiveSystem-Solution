namespace ArchiveSystem.Core.Models
{
    public class ImportBatch
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = "Staging";
        public int TotalSheets { get; set; }
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public int WarningCount { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string? ApprovedAt { get; set; }
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}