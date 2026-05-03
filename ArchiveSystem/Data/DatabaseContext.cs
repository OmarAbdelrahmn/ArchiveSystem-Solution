using Dapper;
using Microsoft.Data.Sqlite;

namespace ArchiveSystem.Data
{
    public class DatabaseContext(string dbPath)
    {
        private readonly string _dbPath = dbPath;

        public SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            conn.Execute("PRAGMA foreign_keys = ON;");
            conn.Execute("PRAGMA journal_mode = WAL;");
            conn.Execute("PRAGMA busy_timeout = 5000;");
            return conn;
        }

        public void InitializeDatabase()
        {
            using var conn = CreateConnection();
            RunMigrations(conn);
        }

        private void RunMigrations(SqliteConnection conn)
        {
            EnsureMigrationsTable(conn);

            if (!MigrationApplied(conn, "001"))
            {
                Migration_001_InitialSchema(conn);
                RecordMigration(conn, "001", "Initial schema - all core tables");
            }

            if (!MigrationApplied(conn, "002"))
            {
                Migration_002_SeedData(conn);
                RecordMigration(conn, "002", "Seed roles, permissions, default settings, nationality field");
            }
        }

        // ─────────────────────────────────────────────
        // MIGRATION TRACKING
        // ─────────────────────────────────────────────

        private void EnsureMigrationsTable(SqliteConnection conn)
        {
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS SchemaMigrations (
                    MigrationId   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Version       TEXT    NOT NULL UNIQUE,
                    Description   TEXT,
                    AppliedAt     TEXT    NOT NULL
                );
            ");
        }

        private bool MigrationApplied(SqliteConnection conn, string version)
        {
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = @Version",
                new { Version = version }) > 0;
        }

        private void RecordMigration(SqliteConnection conn, string version, string description)
        {
            conn.Execute(@"
                INSERT INTO SchemaMigrations (Version, Description, AppliedAt)
                VALUES (@Version, @Description, @AppliedAt)",
                new
                {
                    Version = version,
                    Description = description,
                    AppliedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                });
        }

        // ─────────────────────────────────────────────
        // MIGRATION 001 — ALL TABLES
        // ─────────────────────────────────────────────

        private void Migration_001_InitialSchema(SqliteConnection conn)
        {
            conn.Execute(@"

                -- USERS
                CREATE TABLE IF NOT EXISTS Users (
                    UserId          INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName        TEXT    NOT NULL,
                    Username        TEXT    NOT NULL UNIQUE,
                    EmployeeNumber  TEXT,
                    PasswordHash    TEXT    NOT NULL,
                    PasswordSalt    TEXT,
                    IsActive        INTEGER NOT NULL DEFAULT 1,
                    CreatedAt       TEXT    NOT NULL,
                    UpdatedAt       TEXT,
                    LastLoginAt     TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_Users_FullName       ON Users (FullName);
                CREATE INDEX IF NOT EXISTS IX_Users_EmployeeNumber ON Users (EmployeeNumber);

                -- ROLES
                CREATE TABLE IF NOT EXISTS Roles (
                    RoleId        INTEGER PRIMARY KEY AUTOINCREMENT,
                    RoleName      TEXT    NOT NULL UNIQUE,
                    Description   TEXT,
                    IsSystemRole  INTEGER NOT NULL DEFAULT 0,
                    CreatedAt     TEXT    NOT NULL,
                    UpdatedAt     TEXT
                );

                -- USER ROLES
                CREATE TABLE IF NOT EXISTS UserRoles (
                    UserId    INTEGER NOT NULL,
                    RoleId    INTEGER NOT NULL,
                    CreatedAt TEXT    NOT NULL,
                    PRIMARY KEY (UserId, RoleId),
                    FOREIGN KEY (UserId) REFERENCES Users (UserId),
                    FOREIGN KEY (RoleId) REFERENCES Roles (RoleId)
                );

                -- ROLE PERMISSIONS
                CREATE TABLE IF NOT EXISTS RolePermissions (
                    RolePermissionId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    RoleId            INTEGER NOT NULL,
                    PermissionKey     TEXT    NOT NULL,
                    IsAllowed         INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt         TEXT,
                    UpdatedByUserId   INTEGER,
                    UNIQUE (RoleId, PermissionKey),
                    FOREIGN KEY (RoleId)          REFERENCES Roles (RoleId),
                    FOREIGN KEY (UpdatedByUserId)  REFERENCES Users (UserId)
                );

                -- LOCATIONS
                CREATE TABLE IF NOT EXISTS Locations (
                    LocationId      INTEGER PRIMARY KEY AUTOINCREMENT,
                    HallwayNumber   INTEGER NOT NULL,
                    CabinetNumber   INTEGER NOT NULL,
                    ShelfNumber     INTEGER NOT NULL,
                    Label           TEXT,
                    Capacity        INTEGER,
                    IsActive        INTEGER NOT NULL DEFAULT 1,
                    CreatedAt       TEXT    NOT NULL,
                    UpdatedAt       TEXT,
                    UNIQUE (HallwayNumber, CabinetNumber, ShelfNumber)
                );
                CREATE INDEX IF NOT EXISTS IX_Locations_Hallway  ON Locations (HallwayNumber);
                CREATE INDEX IF NOT EXISTS IX_Locations_IsActive ON Locations (IsActive);

                -- DOSSIERS
                CREATE TABLE IF NOT EXISTS Dossiers (
                    DossierId         INTEGER PRIMARY KEY AUTOINCREMENT,
                    DossierNumber     INTEGER NOT NULL UNIQUE,
                    HijriMonth        INTEGER NOT NULL,
                    HijriYear         INTEGER NOT NULL,
                    ExpectedFileCount INTEGER,
                    CurrentLocationId INTEGER,
                    Status            TEXT    NOT NULL DEFAULT 'Open',
                    CreatedByUserId   INTEGER,
                    CreatedAt         TEXT    NOT NULL,
                    UpdatedAt         TEXT,
                    ClosedAt          TEXT,
                    ImportBatchId     INTEGER,
                    FOREIGN KEY (CurrentLocationId) REFERENCES Locations (LocationId),
                    FOREIGN KEY (CreatedByUserId)   REFERENCES Users (UserId),
                    CHECK (HijriMonth >= 1 AND HijriMonth <= 12),
                    CHECK (Status IN ('Open','Complete','NeedsReview','Archived'))
                );
                CREATE INDEX IF NOT EXISTS IX_Dossiers_HijriYearMonth  ON Dossiers (HijriYear, HijriMonth);
                CREATE INDEX IF NOT EXISTS IX_Dossiers_CurrentLocation  ON Dossiers (CurrentLocationId);
                CREATE INDEX IF NOT EXISTS IX_Dossiers_Status           ON Dossiers (Status);

                -- RECORDS
                CREATE TABLE IF NOT EXISTS Records (
                    RecordId          INTEGER PRIMARY KEY AUTOINCREMENT,
                    DossierId         INTEGER NOT NULL,
                    SequenceNumber    INTEGER NOT NULL,
                    PersonName        TEXT    NOT NULL,
                    PrisonerNumber    TEXT    NOT NULL,
                    Notes             TEXT,
                    Status            TEXT    NOT NULL DEFAULT 'Active',
                    CreatedByUserId   INTEGER,
                    CreatedAt         TEXT    NOT NULL,
                    UpdatedAt         TEXT,
                    DeletedAt         TEXT,
                    DeletedByUserId   INTEGER,
                    ImportBatchId     INTEGER,
                    UNIQUE (DossierId, SequenceNumber),
                    FOREIGN KEY (DossierId)       REFERENCES Dossiers (DossierId),
                    FOREIGN KEY (CreatedByUserId) REFERENCES Users (UserId),
                    FOREIGN KEY (DeletedByUserId) REFERENCES Users (UserId),
                    CHECK (Status IN ('Active','Deleted'))
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Records_PrisonerNumber ON Records (PrisonerNumber) WHERE DeletedAt IS NULL;
                CREATE INDEX      IF NOT EXISTS IX_Records_PersonName       ON Records (PersonName);
                CREATE INDEX      IF NOT EXISTS IX_Records_DossierId        ON Records (DossierId);

                -- DOSSIER MOVEMENTS
                CREATE TABLE IF NOT EXISTS DossierMovements (
                    MovementId      INTEGER PRIMARY KEY AUTOINCREMENT,
                    DossierId       INTEGER NOT NULL,
                    FromLocationId  INTEGER,
                    ToLocationId    INTEGER NOT NULL,
                    Reason          TEXT,
                    MovedByUserId   INTEGER,
                    MovedAt         TEXT    NOT NULL,
                    FOREIGN KEY (DossierId)      REFERENCES Dossiers  (DossierId),
                    FOREIGN KEY (FromLocationId) REFERENCES Locations (LocationId),
                    FOREIGN KEY (ToLocationId)   REFERENCES Locations (LocationId),
                    FOREIGN KEY (MovedByUserId)  REFERENCES Users     (UserId)
                );
                CREATE INDEX IF NOT EXISTS IX_DossierMovements_DossierId ON DossierMovements (DossierId);

                -- CUSTOM FIELDS
                CREATE TABLE IF NOT EXISTS CustomFields (
                    CustomFieldId     INTEGER PRIMARY KEY AUTOINCREMENT,
                    FieldKey          TEXT    NOT NULL UNIQUE,
                    ArabicLabel       TEXT    NOT NULL,
                    EnglishLabel      TEXT,
                    AppliesTo         TEXT    NOT NULL DEFAULT 'Record',
                    FieldType         TEXT    NOT NULL DEFAULT 'Text',
                    IsRequired        INTEGER NOT NULL DEFAULT 0,
                    IsActive          INTEGER NOT NULL DEFAULT 1,
                    ShowInEntry       INTEGER NOT NULL DEFAULT 1,
                    ShowInAllData     INTEGER NOT NULL DEFAULT 1,
                    ShowInReports     INTEGER NOT NULL DEFAULT 0,
                    EnableStatistics  INTEGER NOT NULL DEFAULT 0,
                    AllowBulkUpdate   INTEGER NOT NULL DEFAULT 1,
                    SuggestionLimit   INTEGER NOT NULL DEFAULT 0,
                    SortOrder         INTEGER NOT NULL DEFAULT 0,
                    CreatedByUserId   INTEGER,
                    CreatedAt         TEXT    NOT NULL,
                    UpdatedByUserId   INTEGER,
                    UpdatedAt         TEXT,
                    FOREIGN KEY (CreatedByUserId) REFERENCES Users (UserId),
                    FOREIGN KEY (UpdatedByUserId) REFERENCES Users (UserId),
                    CHECK (AppliesTo IN ('Record')),
                    CHECK (FieldType IN ('Text','TextWithSuggestions','Number','Date','Boolean','SingleChoice','MultiChoice'))
                );
                CREATE INDEX IF NOT EXISTS IX_CustomFields_IsActive    ON CustomFields (IsActive);
                CREATE INDEX IF NOT EXISTS IX_CustomFields_ShowInEntry ON CustomFields (ShowInEntry);

                -- CUSTOM FIELD OPTIONS
                CREATE TABLE IF NOT EXISTS CustomFieldOptions (
                    CustomFieldOptionId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomFieldId        INTEGER NOT NULL,
                    ArabicValue          TEXT    NOT NULL,
                    EnglishValue         TEXT,
                    IsActive             INTEGER NOT NULL DEFAULT 1,
                    SortOrder            INTEGER NOT NULL DEFAULT 0,
                    CreatedAt            TEXT    NOT NULL,
                    UpdatedAt            TEXT,
                    UNIQUE (CustomFieldId, ArabicValue),
                    FOREIGN KEY (CustomFieldId) REFERENCES CustomFields (CustomFieldId)
                );

                -- RECORD CUSTOM FIELD VALUES
                CREATE TABLE IF NOT EXISTS RecordCustomFieldValues (
                    ValueId           INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecordId          INTEGER NOT NULL,
                    CustomFieldId     INTEGER NOT NULL,
                    ValueText         TEXT,
                    UpdatedAt         TEXT,
                    UpdatedByUserId   INTEGER,
                    UNIQUE (RecordId, CustomFieldId),
                    FOREIGN KEY (RecordId)        REFERENCES Records      (RecordId),
                    FOREIGN KEY (CustomFieldId)   REFERENCES CustomFields (CustomFieldId),
                    FOREIGN KEY (UpdatedByUserId) REFERENCES Users        (UserId)
                );
                CREATE INDEX IF NOT EXISTS IX_RecordCustomFieldValues_RecordId     ON RecordCustomFieldValues (RecordId);
                CREATE INDEX IF NOT EXISTS IX_RecordCustomFieldValues_CustomFieldId ON RecordCustomFieldValues (CustomFieldId);
                CREATE INDEX IF NOT EXISTS IX_RecordCustomFieldValues_ValueText     ON RecordCustomFieldValues (ValueText);

                -- BULK FIELD UPDATE BATCHES
                CREATE TABLE IF NOT EXISTS BulkFieldUpdateBatches (
                    BatchId         INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomFieldId   INTEGER NOT NULL,
                    NewValue        TEXT,
                    RecordCount     INTEGER NOT NULL DEFAULT 0,
                    ExecutedByUserId INTEGER,
                    ExecutedAt      TEXT    NOT NULL,
                    FOREIGN KEY (CustomFieldId)    REFERENCES CustomFields (CustomFieldId),
                    FOREIGN KEY (ExecutedByUserId) REFERENCES Users        (UserId)
                );

                -- IMPORT BATCHES
                CREATE TABLE IF NOT EXISTS ImportBatches (
                    ImportBatchId     INTEGER PRIMARY KEY AUTOINCREMENT,
                    FileName          TEXT    NOT NULL,
                    Status            TEXT    NOT NULL DEFAULT 'Staging',
                    TotalSheets       INTEGER NOT NULL DEFAULT 0,
                    TotalDossiers     INTEGER NOT NULL DEFAULT 0,
                    TotalRecords      INTEGER NOT NULL DEFAULT 0,
                    WarningCount      INTEGER NOT NULL DEFAULT 0,
                    ApprovedByUserId  INTEGER,
                    ApprovedAt        TEXT,
                    CreatedByUserId   INTEGER,
                    CreatedAt         TEXT    NOT NULL,
                    Notes             TEXT,
                    FOREIGN KEY (ApprovedByUserId) REFERENCES Users (UserId),
                    FOREIGN KEY (CreatedByUserId)  REFERENCES Users (UserId),
                    CHECK (Status IN ('Staging','ReadyForReview','Imported','Failed','RolledBack'))
                );

                -- IMPORT STAGING DOSSIERS
                CREATE TABLE IF NOT EXISTS ImportStagingDossiers (
                    StagingDossierId   INTEGER PRIMARY KEY AUTOINCREMENT,
                    ImportBatchId      INTEGER NOT NULL,
                    SheetName          TEXT,
                    DossierNumber      INTEGER,
                    HijriMonth         INTEGER,
                    HijriYear          INTEGER,
                    ExpectedFileCount  INTEGER,
                    HallwayNumber      INTEGER,
                    CabinetNumber      INTEGER,
                    ShelfNumber        INTEGER,
                    ActualRowCount     INTEGER NOT NULL DEFAULT 0,
                    Status             TEXT    NOT NULL DEFAULT 'Pending',
                    FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (ImportBatchId),
                    CHECK (Status IN ('Pending','Ready','NeedsReview','Rejected'))
                );

                -- IMPORT STAGING RECORDS
                CREATE TABLE IF NOT EXISTS ImportStagingRecords (
                    StagingRecordId    INTEGER PRIMARY KEY AUTOINCREMENT,
                    ImportBatchId      INTEGER NOT NULL,
                    StagingDossierId   INTEGER NOT NULL,
                    SequenceNumber     INTEGER,
                    PersonName         TEXT,
                    PrisonerNumber     TEXT,
                    HallwayNumber      INTEGER,
                    CabinetNumber      INTEGER,
                    ShelfNumber        INTEGER,
                    RowNumber          INTEGER,
                    Status             TEXT    NOT NULL DEFAULT 'Pending',
                    FOREIGN KEY (ImportBatchId)    REFERENCES ImportBatches        (ImportBatchId),
                    FOREIGN KEY (StagingDossierId) REFERENCES ImportStagingDossiers (StagingDossierId),
                    CHECK (Status IN ('Pending','Ready','HasWarning','Rejected'))
                );

                -- IMPORT WARNINGS
                CREATE TABLE IF NOT EXISTS ImportWarnings (
                    WarningId         INTEGER PRIMARY KEY AUTOINCREMENT,
                    ImportBatchId     INTEGER NOT NULL,
                    StagingDossierId  INTEGER,
                    StagingRecordId   INTEGER,
                    WarningType       TEXT    NOT NULL,
                    WarningMessage    TEXT    NOT NULL,
                    SuggestedAction   TEXT,
                    IsResolved        INTEGER NOT NULL DEFAULT 0,
                    ResolvedByUserId  INTEGER,
                    ResolvedAt        TEXT,
                    FOREIGN KEY (ImportBatchId)    REFERENCES ImportBatches         (ImportBatchId),
                    FOREIGN KEY (StagingDossierId) REFERENCES ImportStagingDossiers (StagingDossierId),
                    FOREIGN KEY (StagingRecordId)  REFERENCES ImportStagingRecords  (StagingRecordId),
                    FOREIGN KEY (ResolvedByUserId) REFERENCES Users                 (UserId)
                );
                CREATE INDEX IF NOT EXISTS IX_ImportWarnings_Batch      ON ImportWarnings (ImportBatchId);
                CREATE INDEX IF NOT EXISTS IX_ImportWarnings_IsResolved  ON ImportWarnings (IsResolved);

                -- AUDIT LOG
                CREATE TABLE IF NOT EXISTS AuditLog (
                    AuditId      INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId       INTEGER,
                    ActionType   TEXT    NOT NULL,
                    EntityType   TEXT,
                    EntityId     INTEGER,
                    Description  TEXT    NOT NULL,
                    OldValueJson TEXT,
                    NewValueJson TEXT,
                    CreatedAt    TEXT    NOT NULL,
                    FOREIGN KEY (UserId) REFERENCES Users (UserId)
                );
                CREATE INDEX IF NOT EXISTS IX_AuditLog_UserId     ON AuditLog (UserId);
                CREATE INDEX IF NOT EXISTS IX_AuditLog_ActionType ON AuditLog (ActionType);
                CREATE INDEX IF NOT EXISTS IX_AuditLog_CreatedAt  ON AuditLog (CreatedAt);
                CREATE INDEX IF NOT EXISTS IX_AuditLog_Entity     ON AuditLog (EntityType, EntityId);

                -- BACKUPS
                CREATE TABLE IF NOT EXISTS Backups (
                    BackupId          INTEGER PRIMARY KEY AUTOINCREMENT,
                    BackupPath        TEXT    NOT NULL,
                    BackupType        TEXT    NOT NULL,
                    Status            TEXT    NOT NULL,
                    FileSizeBytes     INTEGER,
                    CreatedByUserId   INTEGER,
                    CreatedAt         TEXT    NOT NULL,
                    Notes             TEXT,
                    FOREIGN KEY (CreatedByUserId) REFERENCES Users (UserId),
                    CHECK (BackupType IN ('Automatic','Manual','BeforeImport','BeforeRestore','BeforeMigration')),
                    CHECK (Status IN ('Success','Failed'))
                );
                CREATE INDEX IF NOT EXISTS IX_Backups_CreatedAt  ON Backups (CreatedAt);
                CREATE INDEX IF NOT EXISTS IX_Backups_BackupType ON Backups (BackupType);

                -- APP SETTINGS
                CREATE TABLE IF NOT EXISTS AppSettings (
                    SettingKey        TEXT PRIMARY KEY,
                    SettingValue      TEXT NOT NULL,
                    UpdatedAt         TEXT,
                    UpdatedByUserId   INTEGER,
                    FOREIGN KEY (UpdatedByUserId) REFERENCES Users (UserId)
                );

                -- SEARCH VIEW
                CREATE VIEW IF NOT EXISTS View_RecordSearch AS
                SELECT
                    r.RecordId,
                    r.PersonName,
                    r.PrisonerNumber,
                    r.SequenceNumber,
                    r.Status        AS RecordStatus,
                    d.DossierId,
                    d.DossierNumber,
                    d.HijriMonth,
                    d.HijriYear,
                    d.Status        AS DossierStatus,
                    l.HallwayNumber,
                    l.CabinetNumber,
                    l.ShelfNumber,
                    r.CreatedAt,
                    r.UpdatedAt
                FROM Records r
                JOIN  Dossiers  d ON d.DossierId  = r.DossierId
                LEFT JOIN Locations l ON l.LocationId = d.CurrentLocationId
                WHERE r.DeletedAt IS NULL;

                -- DOSSIER COUNT VIEW
                CREATE VIEW IF NOT EXISTS View_DossierCounts AS
                SELECT
                    d.DossierId,
                    d.DossierNumber,
                    d.ExpectedFileCount,
                    COUNT(r.RecordId) AS ActualFileCount,
                    CASE
                        WHEN d.ExpectedFileCount IS NULL            THEN 'Unknown'
                        WHEN d.ExpectedFileCount = COUNT(r.RecordId) THEN 'Complete'
                        ELSE 'Mismatch'
                    END AS CountStatus
                FROM Dossiers d
                LEFT JOIN Records r ON r.DossierId = d.DossierId AND r.DeletedAt IS NULL
                GROUP BY d.DossierId, d.DossierNumber, d.ExpectedFileCount;
            ");
        }

        // ─────────────────────────────────────────────
        // MIGRATION 002 — SEED DATA
        // ─────────────────────────────────────────────

        private void Migration_002_SeedData(SqliteConnection conn)
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            // ── Roles ────────────────────────────────
            conn.Execute(@"
                INSERT OR IGNORE INTO Roles (RoleName, Description, IsSystemRole, CreatedAt)
                VALUES
                  ('مدير الأرشيف', 'صلاحيات كاملة على النظام',      1, @now),
                  ('إدخال بيانات',  'إضافة وتعديل السجلات',          1, @now),
                  ('مراجع',         'عرض السجلات والتقارير فقط',     1, @now);
            ", new { now });

            // ── Permissions: Archive Manager gets everything ──
            var allPermissions = new[]
            {
                "SearchRecords","ViewDossier","AddRecord","EditRecord","DeleteRecord",
                "CreateDossier","EditDossier","MoveDossier","PrintReports","PrintDossierFace",
                "ImportExcel","ApproveExcelImport","ViewStatistics","ManageArchiveStructure",
                "ManageCustomFields","ManageUsers","ManageSettings","CreateBackup",
                "RestoreBackup","ViewAuditLog"
            };

            var managerRoleId = conn.ExecuteScalar<int>(
                "SELECT RoleId FROM Roles WHERE RoleName = 'مدير الأرشيف'");

            var dataEntryRoleId = conn.ExecuteScalar<int>(
                "SELECT RoleId FROM Roles WHERE RoleName = 'إدخال بيانات'");

            var reviewerRoleId = conn.ExecuteScalar<int>(
                "SELECT RoleId FROM Roles WHERE RoleName = 'مراجع'");

            foreach (var perm in allPermissions)
            {
                conn.Execute(@"
                    INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionKey, IsAllowed, UpdatedAt)
                    VALUES (@RoleId, @PermissionKey, 1, @now)",
                    new { RoleId = managerRoleId, PermissionKey = perm, now });
            }

            // Data Entry permissions
            var dataEntryPerms = new[]
            {
                "SearchRecords","ViewDossier","AddRecord","EditRecord",
                "CreateDossier","PrintDossierFace","ViewStatistics","ManageFieldSuggestions"
            };
            foreach (var perm in dataEntryPerms)
            {
                conn.Execute(@"
                    INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionKey, IsAllowed, UpdatedAt)
                    VALUES (@RoleId, @PermissionKey, 1, @now)",
                    new { RoleId = dataEntryRoleId, PermissionKey = perm, now });
            }

            // Reviewer permissions
            var reviewerPerms = new[]
            {
                "SearchRecords","ViewDossier","PrintReports",
                "PrintDossierFace","ViewStatistics"
            };
            foreach (var perm in reviewerPerms)
            {
                conn.Execute(@"
                    INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionKey, IsAllowed, UpdatedAt)
                    VALUES (@RoleId, @PermissionKey, 1, @now)",
                    new { RoleId = reviewerRoleId, PermissionKey = perm, now });
            }

            // ── Default App Settings ──────────────────
            var settings = new Dictionary<string, string>
            {
                ["PreventDuplicatePrisonerNumber"] = "true",
                ["RequireExactTenDigitNumber"] = "true",
                ["RequireLocation"] = "true",
                ["RequireMovementReason"] = "true",
                ["BackupRetentionDays"] = "365",
                ["AutoSequenceEnabled"] = "true",
                ["ThemeColor"] = "#178567",
                ["FontScale"] = "Normal",
                ["AuditEditsEnabled"] = "true",
                ["AuditPrintingEnabled"] = "true",
                ["AuditImportsEnabled"] = "true",
            };

            foreach (var s in settings)
            {
                conn.Execute(@"
                    INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, UpdatedAt)
                    VALUES (@Key, @Value, @now)",
                    new { Key = s.Key, Value = s.Value, now });
            }

            // ── Default Custom Field: الجنسية ─────────
            conn.Execute(@"
                INSERT OR IGNORE INTO CustomFields
                    (FieldKey, ArabicLabel, FieldType, IsRequired, IsActive,
                     ShowInEntry, ShowInAllData, ShowInReports, EnableStatistics,
                     AllowBulkUpdate, SuggestionLimit, SortOrder, CreatedAt)
                VALUES
                    ('nationality', 'الجنسية', 'TextWithSuggestions', 0, 1,
                     1, 1, 0, 1,
                     1, 8, 1, @now);
            ", new { now });
        }
    }
}