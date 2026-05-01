namespace ArchiveSystem.Core.Models
{
    public class RecordCustomFieldValue
    {
        public int ValueId { get; set; }
        public int RecordId { get; set; }
        public int CustomFieldId { get; set; }
        public string? ValueText { get; set; }
        public string? UpdatedAt { get; set; }
        public int? UpdatedByUserId { get; set; }

        // joined — not stored in DB
        public string? FieldArabicLabel { get; set; }
        public string? FieldType { get; set; }
    }
}
