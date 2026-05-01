namespace ArchiveSystem.Core.Models
{
    public class CustomField
    {
        public int CustomFieldId { get; set; }
        public string FieldKey { get; set; } = string.Empty;
        public string ArabicLabel { get; set; } = string.Empty;
        public string? EnglishLabel { get; set; }
        public string AppliesTo { get; set; } = "Record";
        public string FieldType { get; set; } = "Text";
        public bool IsRequired { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public bool ShowInEntry { get; set; } = true;
        public bool ShowInAllData { get; set; } = true;
        public bool ShowInReports { get; set; } = false;
        public bool EnableStatistics { get; set; } = false;
        public bool AllowBulkUpdate { get; set; } = true;
        public int SuggestionLimit { get; set; } = 0;
        public int SortOrder { get; set; } = 0;
        public int? CreatedByUserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public int? UpdatedByUserId { get; set; }
        public string? UpdatedAt { get; set; }
    }

    // all valid field types in one place
    public static class FieldTypes
    {
        public const string Text = "Text";
        public const string TextWithSuggestions = "TextWithSuggestions";
        public const string Number = "Number";
        public const string Date = "Date";
        public const string Boolean = "Boolean";
        public const string SingleChoice = "SingleChoice";
        public const string MultiChoice = "MultiChoice";
    }
}