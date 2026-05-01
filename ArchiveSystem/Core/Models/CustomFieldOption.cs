namespace ArchiveSystem.Core.Models
{
    public class CustomFieldOption
    {
        public int CustomFieldOptionId { get; set; }
        public int CustomFieldId { get; set; }
        public string ArabicValue { get; set; } = string.Empty;
        public string? EnglishValue { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 0;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
    }
}