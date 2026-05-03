namespace ArchiveSystem.Core.Models
{
    public class AppSetting
    {
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
        public int? UpdatedByUserId { get; set; }
    }

    // all setting keys in one place — no magic strings anywhere
    public static class SettingKeys
    {
        public const string DefaultHijriMonth = "DefaultHijriMonth";
        public const string DefaultHijriYear = "DefaultHijriYear";
        public const string CurrentDossierNumber = "CurrentDossierNumber";
        public const string AutoSequenceEnabled = "AutoSequenceEnabled";
        public const string PreventDuplicatePrisonerNumber = "PreventDuplicatePrisonerNumber";
        public const string RequireExactTenDigitNumber = "RequireExactTenDigitNumber";
        public const string RequireLocation = "RequireLocation";
        public const string RequireMovementReason = "RequireMovementReason";
        public const string BackupPath = "BackupPath";
        public const string BackupTime = "BackupTime";
        public const string BackupRetentionDays = "BackupRetentionDays";
        public const string ThemeColor = "ThemeColor";
        public const string FontScale = "FontScale";
        public const string AuditEditsEnabled = "AuditEditsEnabled";
        public const string AuditPrintingEnabled = "AuditPrintingEnabled";
        public const string AuditImportsEnabled = "AuditImportsEnabled";
        public const string AppVersion = "AppVersion";
    }
}