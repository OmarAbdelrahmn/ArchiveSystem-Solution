using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using Dapper;

namespace ArchiveSystem.Core.Services
{
    public class CustomFieldService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        public List<CustomField> GetAllFields()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CustomField>(
                "SELECT * FROM CustomFields ORDER BY SortOrder, ArabicLabel").AsList();
        }

        public List<CustomField> GetActiveEntryFields()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInEntry = 1
                ORDER BY SortOrder, ArabicLabel").AsList();
        }

        public List<CustomField> GetActiveReportFields()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CustomField>(@"
                SELECT * FROM CustomFields
                WHERE IsActive = 1 AND ShowInReports = 1
                ORDER BY SortOrder, ArabicLabel").AsList();
        }

        public List<CustomFieldOption> GetFieldOptions(int customFieldId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CustomFieldOption>(@"
                SELECT * FROM CustomFieldOptions
                WHERE CustomFieldId = @Id AND IsActive = 1
                ORDER BY SortOrder, ArabicValue",
                new { Id = customFieldId }).AsList();
        }

        public List<string> GetSuggestions(int customFieldId, int limit = 8)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<string>(@"
                    SELECT Value FROM (
                        -- Values already used in records (most recent first)
                        SELECT DISTINCT ValueText AS Value,
                               MAX(UpdatedAt) AS LastUsed,
                               1 AS Source
                        FROM RecordCustomFieldValues
                        WHERE CustomFieldId = @Id
                          AND ValueText IS NOT NULL AND ValueText != ''
                        GROUP BY ValueText

                        UNION

                        -- Manually seeded options (appear even if never used)
                        SELECT ArabicValue AS Value,
                               CreatedAt AS LastUsed,
                               2 AS Source
                        FROM CustomFieldOptions
                        WHERE CustomFieldId = @Id
                          AND IsActive = 1
                    )
                    GROUP BY Value
                    ORDER BY MIN(Source), MAX(LastUsed) DESC
                    LIMIT @Limit",
                new { Id = customFieldId, Limit = limit }).AsList();
        }

        public Dictionary<int, string?> GetRecordValues(int recordId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<(int CustomFieldId, string? ValueText)>(@"
                SELECT CustomFieldId, ValueText
                FROM RecordCustomFieldValues
                WHERE RecordId = @RecordId",
                new { RecordId = recordId })
                .ToDictionary(x => x.CustomFieldId, x => x.ValueText);
        }

        public void SaveFieldValue(int recordId, int customFieldId, string? value)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO RecordCustomFieldValues
                    (RecordId, CustomFieldId, ValueText, UpdatedAt, UpdatedByUserId)
                VALUES (@RecordId, @FieldId, @Value, @Now, @UserId)
                ON CONFLICT(RecordId, CustomFieldId)
                DO UPDATE SET ValueText = @Value, UpdatedAt = @Now, UpdatedByUserId = @UserId",
                new
                {
                    RecordId = recordId,
                    FieldId = customFieldId,
                    Value = string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim(),
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    UserId = UserSession.CurrentUser?.UserId
                });
        }

        public void SaveFieldValues(int recordId,
            IEnumerable<(int FieldId, string? Value)> values,
            Microsoft.Data.Sqlite.SqliteConnection conn,
            Microsoft.Data.Sqlite.SqliteTransaction tx)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            int userId = UserSession.CurrentUser?.UserId ?? 0;
            foreach (var (fieldId, value) in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                conn.Execute(@"
                    INSERT INTO RecordCustomFieldValues
                        (RecordId, CustomFieldId, ValueText, UpdatedAt, UpdatedByUserId)
                    VALUES (@RecordId, @FieldId, @Value, @Now, @UserId)
                    ON CONFLICT(RecordId, CustomFieldId)
                    DO UPDATE SET ValueText = @Value, UpdatedAt = @Now, UpdatedByUserId = @UserId",
                    new { RecordId = recordId, FieldId = fieldId, Value = value.Trim(), Now = now, UserId = userId },
                    tx);
            }
        }

        public string? CreateField(CustomField field)
        {
            if (string.IsNullOrWhiteSpace(field.ArabicLabel))
                return "التسمية العربية مطلوبة.";

            if (string.IsNullOrWhiteSpace(field.FieldKey))
                field.FieldKey = "cf_" + DateTime.UtcNow.Ticks.ToString()[^8..];

            using var conn = _db.CreateConnection();
            int exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM CustomFields WHERE FieldKey = @Key",
                new { Key = field.FieldKey });
            if (exists > 0) return "مفتاح الحقل مستخدم مسبقاً.";

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            conn.Execute(@"
                INSERT INTO CustomFields
                    (FieldKey, ArabicLabel, EnglishLabel, FieldType, IsRequired,
                     IsActive, ShowInEntry, ShowInAllData, ShowInReports,
                     EnableStatistics, AllowBulkUpdate, SuggestionLimit,
                     SortOrder, CreatedByUserId, CreatedAt)
                VALUES
                    (@FieldKey, @ArabicLabel, @EnglishLabel, @FieldType, @IsRequired,
                     @IsActive, @ShowInEntry, @ShowInAllData, @ShowInReports,
                     @EnableStatistics, @AllowBulkUpdate, @SuggestionLimit,
                     @SortOrder, @UserId, @Now)",
                new
                {
                    field.FieldKey,
                    field.ArabicLabel,
                    field.EnglishLabel,
                    field.FieldType,
                    field.IsRequired,
                    field.IsActive,
                    field.ShowInEntry,
                    field.ShowInAllData,
                    field.ShowInReports,
                    field.EnableStatistics,
                    field.AllowBulkUpdate,
                    field.SuggestionLimit,
                    field.SortOrder,
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = now
                });

            WriteAudit(conn, $"تم إنشاء حقل مخصص: {field.ArabicLabel}");
            return null;
        }

        public string? UpdateField(CustomField field)
        {
            if (string.IsNullOrWhiteSpace(field.ArabicLabel))
                return "التسمية العربية مطلوبة.";

            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE CustomFields SET
                    ArabicLabel       = @ArabicLabel,
                    EnglishLabel      = @EnglishLabel,
                    FieldType         = @FieldType,
                    IsRequired        = @IsRequired,
                    IsActive          = @IsActive,
                    ShowInEntry       = @ShowInEntry,
                    ShowInAllData     = @ShowInAllData,
                    ShowInReports     = @ShowInReports,
                    EnableStatistics  = @EnableStatistics,
                    AllowBulkUpdate   = @AllowBulkUpdate,
                    SuggestionLimit   = @SuggestionLimit,
                    SortOrder         = @SortOrder,
                    UpdatedByUserId   = @UserId,
                    UpdatedAt         = @Now
                WHERE CustomFieldId = @CustomFieldId",
                new
                {
                    field.ArabicLabel,
                    field.EnglishLabel,
                    field.FieldType,
                    field.IsRequired,
                    field.IsActive,
                    field.ShowInEntry,
                    field.ShowInAllData,
                    field.ShowInReports,
                    field.EnableStatistics,
                    field.AllowBulkUpdate,
                    field.SuggestionLimit,
                    field.SortOrder,
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    field.CustomFieldId
                });

            WriteAudit(conn, $"تم تعديل حقل مخصص: {field.ArabicLabel}");
            return null;
        }

        public string? AddFieldOption(int customFieldId, string arabicValue)
        {
            if (string.IsNullOrWhiteSpace(arabicValue)) return "القيمة مطلوبة.";
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT OR IGNORE INTO CustomFieldOptions
                    (CustomFieldId, ArabicValue, IsActive, SortOrder, CreatedAt)
                VALUES (@FieldId, @Value, 1, 0, @Now)",
                new
                {
                    FieldId = customFieldId,
                    Value = arabicValue.Trim(),
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            return null;
        }

        public void DeleteFieldOption(int optionId)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE CustomFieldOptions SET IsActive = 0 WHERE CustomFieldOptionId = @Id",
                new { Id = optionId });
        }

        private void WriteAudit(Microsoft.Data.Sqlite.SqliteConnection conn, string desc)
        {
            conn.Execute(@"
                INSERT INTO AuditLog (UserId, ActionType, Description, CreatedAt)
                VALUES (@UserId, @Action, @Desc, @Now)",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Action = AuditActions.CustomFieldChanged,
                    Desc = desc,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
        }
    }
}