namespace ArchiveSystem.Core.Models
{
    public class AuditLog
    {
        public int AuditId { get; set; }
        public int? UserId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? OldValueJson { get; set; }
        public string? NewValueJson { get; set; }
        public string CreatedAt { get; set; } = string.Empty;

        // joined — not stored in DB
        public string? UserFullName { get; set; }
    }

    public static class AuditActions
    {
        public const string LoginSuccess = "LoginSuccess";
        public const string LoginFailure = "LoginFailure";
        public const string RecordCreated = "RecordCreated";
        public const string RecordEdited = "RecordEdited";
        public const string RecordDeleted = "RecordDeleted";
        public const string DossierCreated = "DossierCreated";
        public const string DossierEdited = "DossierEdited";
        public const string DossierMoved = "DossierMoved";
        public const string ExcelImportStarted = "ExcelImportStarted";
        public const string ExcelImportApproved = "ExcelImportApproved";
        public const string ExcelImportCompleted = "ExcelImportCompleted";
        public const string ExcelImportFailed = "ExcelImportFailed";
        public const string BackupCreated = "BackupCreated";
        public const string RestoreCompleted = "RestoreCompleted";
        public const string SettingsChanged = "SettingsChanged";
        public const string UserChanged = "UserChanged";
        public const string RoleChanged = "RoleChanged";
        public const string BulkFieldUpdate = "BulkFieldUpdate";
        public const string CustomFieldCreated = "CustomFieldCreated";
        public const string CustomFieldChanged = "CustomFieldChanged";
    }
}