using ArchiveSystem.Core.Models;
using ArchiveSystem.Data;
using ClosedXML.Excel;
using Dapper;
using System.IO;
using System.Text.RegularExpressions;

namespace ArchiveSystem.Core.Services
{
    // ── Warning type constants ─────────────────────────────────────────────────
    public static class ImportWarningTypes
    {
        public const string MissingDossierMetadata = "MissingDossierMetadata";
        public const string MissingHeaderRow = "MissingHeaderRow";
        public const string CountMismatch = "CountMismatch";
        public const string MissingSequence = "MissingSequence";
        public const string DuplicateSequence = "DuplicateSequence";
        public const string SequenceGap = "SequenceGap";
        public const string InvalidSequence = "InvalidSequence";
        public const string MissingName = "MissingName";
        public const string SuspiciousName = "SuspiciousName";
        public const string MissingPrisonerNumber = "MissingPrisonerNumber";
        public const string InvalidPrisonerNumber = "InvalidPrisonerNumber";
        public const string DuplicateInSheet = "DuplicatePrisonerNumberInSheet";
        public const string DuplicateInImport = "DuplicatePrisonerNumberInImport";
        public const string DuplicateInDatabase = "DuplicatePrisonerNumberInDatabase";
        public const string MixedLocationInDossier = "MixedLocationInDossier";
        public const string InvalidLocation = "InvalidLocation";
        public const string SheetNameTitleMismatch = "SheetNameTitleMismatch";
    }

    public class StagedDossierView
    {
        public int StagingDossierId { get; set; }
        public string SheetName { get; set; } = string.Empty;
        public int? DossierNumber { get; set; }
        public int? HijriMonth { get; set; }
        public int? HijriYear { get; set; }
        public int? ExpectedCount { get; set; }
        public int ActualRowCount { get; set; }
        public string Status { get; set; } = "Pending";
        public int WarningCount { get; set; }
        public string StatusDisplay => Status switch
        {
            "Ready" => "✅ جاهز",
            "NeedsReview" => "⚠️ يحتاج مراجعة",
            "Rejected" => "❌ مرفوض",
            _ => "⏳ قيد المعالجة"
        };
    }

    public class ImportWarningView
    {
        public int WarningId { get; set; }
        public string SheetName { get; set; } = string.Empty;
        public int? RowNumber { get; set; }
        public int? DossierNumber { get; set; }
        public string WarningType { get; set; } = string.Empty;
        public string WarningMessage { get; set; } = string.Empty;
        public string SuggestedAction { get; set; } = string.Empty;
        public bool IsResolved { get; set; }
        public string RowDisplay => RowNumber.HasValue ? RowNumber.ToString()! : "-";
    }

    public class StagingResult
    {
        public int BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int TotalSheets { get; set; }
        public int TotalDossiers { get; set; }
        public int TotalRecords { get; set; }
        public int ReadySheets { get; set; }
        public int NeedsReviewSheets { get; set; }
        public int WarningCount { get; set; }
        public int DuplicateCount { get; set; }
        public string? Error { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    public class ExcelImportService(DatabaseContext db)
    {
        private readonly DatabaseContext _db = db;

        private static readonly Regex TitleRegex = new(
            @"دوسية\s*رقم\s*\(?\s*(\d+)\s*\)?" +
            @".*?شهر\s*\(?\s*(\d+)\s*\)?" +
            @".*?لعام\s*\(?\s*(\d+)\s*\)?هـ?" +
            @".*?عدد\s*\(?\s*(\d+)\s*\)?",
            RegexOptions.Singleline);

        private static readonly string[] RequiredHeaders =
            ["تسلسل", "اسم السجين", "رقم السجين"];

        // ── STAGE BATCH ───────────────────────────────────────────────────────
        public StagingResult StageBatch(string filePath)
        {
            var result = new StagingResult { FileName = Path.GetFileName(filePath) };

            try
            {
                using var workbook = new XLWorkbook(filePath);
                using var conn = _db.CreateConnection();
                using var tx = conn.BeginTransaction();

                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;

                int batchId = conn.ExecuteScalar<int>(@"
                    INSERT INTO ImportBatches
                        (FileName, Status, TotalSheets, CreatedByUserId, CreatedAt)
                    VALUES (@FileName, 'Staging', 0, @UserId, @Now);
                    SELECT last_insert_rowid();",
                    new { FileName = result.FileName, UserId = userId, Now = now }, tx);

                result.BatchId = batchId;

                var seenInImport = new HashSet<string>();
                var existingPrisoners = conn
                    .Query<string>("SELECT PrisonerNumber FROM Records WHERE DeletedAt IS NULL",
                        transaction: tx)
                    .ToHashSet();

                int totalSheets = workbook.Worksheets.Count;
                int totalDossiers = 0, totalRecords = 0, totalWarnings = 0, totalDupes = 0;
                int readySheets = 0, needsReviewSheets = 0;

                foreach (var ws in workbook.Worksheets)
                {
                    var sheetResult = ProcessSheet(
                        conn, tx, ws, batchId, seenInImport, existingPrisoners, now);

                    totalDossiers++;
                    totalRecords += sheetResult.Records;
                    totalWarnings += sheetResult.Warnings;
                    totalDupes += sheetResult.Duplicates;

                    if (sheetResult.Status == "Ready") readySheets++;
                    else if (sheetResult.Status is "NeedsReview" or "Rejected")
                        needsReviewSheets++;
                }

                conn.Execute(@"
                    UPDATE ImportBatches
                    SET TotalSheets   = @Sheets,
                        TotalDossiers = @Dossiers,
                        TotalRecords  = @Records,
                        WarningCount  = @Warnings,
                        Status        = 'ReadyForReview'
                    WHERE ImportBatchId = @Id",
                    new
                    {
                        Sheets = totalSheets,
                        Dossiers = totalDossiers,
                        Records = totalRecords,
                        Warnings = totalWarnings,
                        Id = batchId
                    }, tx);

                tx.Commit();

                result.TotalSheets = totalSheets;
                result.TotalDossiers = totalDossiers;
                result.TotalRecords = totalRecords;
                result.WarningCount = totalWarnings;
                result.DuplicateCount = totalDupes;
                result.ReadySheets = readySheets;
                result.NeedsReviewSheets = needsReviewSheets;
            }
            catch (Exception ex)
            {
                result.Error = $"خطأ أثناء قراءة الملف: {ex.Message}";
            }

            return result;
        }

        // ── PROCESS ONE SHEET ─────────────────────────────────────────────────
        private record SheetProcessResult(int Records, int Warnings, int Duplicates, string Status);

        private SheetProcessResult ProcessSheet(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            Microsoft.Data.Sqlite.SqliteTransaction tx,
            IXLWorksheet ws,
            int batchId,
            HashSet<string> seenInImport,
            HashSet<string> existingInDb,
            string now)
        {
            string sheetName = ws.Name;
            int warnings = 0, duplicates = 0;
            string sheetStatus = "Ready";

            // ── 1. Parse title row ────────────────────────────────────────────
            int? dossierNumber = null, hijriMonth = null, hijriYear = null, expectedCount = null;
            bool titleParsed = false;

            for (int r = 1; r <= Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 5); r++)
            {
                string cellText = GetRowText(ws, r);
                var m = TitleRegex.Match(cellText);
                if (m.Success)
                {
                    dossierNumber = int.Parse(m.Groups[1].Value);
                    hijriMonth = int.Parse(m.Groups[2].Value);
                    hijriYear = int.Parse(m.Groups[3].Value);
                    expectedCount = int.Parse(m.Groups[4].Value);
                    titleParsed = true;
                    break;
                }
            }

            // ── 2. Insert staging dossier ─────────────────────────────────────
            int stagingDossierId = conn.ExecuteScalar<int>(@"
                INSERT INTO ImportStagingDossiers
                    (ImportBatchId, SheetName, DossierNumber, HijriMonth,
                     HijriYear, ExpectedFileCount, Status)
                VALUES (@BatchId, @Sheet, @DNum, @Month, @Year, @Expected, 'Pending');
                SELECT last_insert_rowid();",
                new
                {
                    BatchId = batchId,
                    Sheet = sheetName,
                    DNum = dossierNumber,
                    Month = hijriMonth,
                    Year = hijriYear,
                    Expected = expectedCount
                }, tx);

            // ── 3a. SheetNameTitleMismatch warning ────────────────────────────
            // FIX: Was detected but AddWarning was never called — now fixed
            if (titleParsed && dossierNumber.HasValue)
            {
                string sheetDigits = Regex.Replace(sheetName, @"\D", "");
                if (!string.IsNullOrEmpty(sheetDigits) &&
                    int.TryParse(sheetDigits, out int sheetNum) &&
                    sheetNum != dossierNumber.Value)
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, null,
                        ImportWarningTypes.SheetNameTitleMismatch,
                        $"اسم الشيت '{sheetName}' لا يتطابق مع رقم الدوسية {dossierNumber} في العنوان",
                        "تحقق من الشيت الصحيح أو صحح رقم الدوسية");
                    warnings++;
                    sheetStatus = "NeedsReview";
                }
            }

            // ── 3b. Handle missing title ──────────────────────────────────────
            if (!titleParsed)
            {
                AddWarning(conn, tx, batchId, stagingDossierId, null,
                    ImportWarningTypes.MissingDossierMetadata,
                    $"لم يتم العثور على بيانات الدوسية في شيت '{sheetName}'",
                    "يرجى إدخال البيانات يدوياً في شاشة المراجعة");
                warnings++;
                sheetStatus = "NeedsReview";
            }

            // ── 4. Find header row ────────────────────────────────────────────
            int headerRow = -1;
            var colMap = new Dictionary<string, int>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 10;

            for (int r = 1; r <= Math.Min(10, lastRow); r++)
            {
                var row = ws.Row(r);
                var cellTexts = new Dictionary<string, int>();
                foreach (var cell in row.CellsUsed())
                {
                    string txt = cell.GetString().Trim();
                    if (!string.IsNullOrEmpty(txt))
                        cellTexts[txt] = cell.Address.ColumnNumber;
                }
                if (RequiredHeaders.All(h => cellTexts.ContainsKey(h)))
                {
                    headerRow = r;
                    colMap = cellTexts;
                    break;
                }
            }

            if (headerRow == -1)
            {
                AddWarning(conn, tx, batchId, stagingDossierId, null,
                    ImportWarningTypes.MissingHeaderRow,
                    $"لم يتم العثور على صف الترويسة في شيت '{sheetName}'",
                    "تأكد من وجود الأعمدة: تسلسل، اسم السجين، رقم السجين");
                warnings++;

                conn.Execute(
                    "UPDATE ImportStagingDossiers SET Status = 'NeedsReview' WHERE StagingDossierId = @Id",
                    new { Id = stagingDossierId }, tx);

                return new SheetProcessResult(0, warnings, duplicates, "NeedsReview");
            }

            // ── 5. Read record rows ───────────────────────────────────────────
            int seqCol = colMap.GetValueOrDefault("تسلسل", -1);
            int nameCol = colMap.GetValueOrDefault("اسم السجين", -1);
            int numCol = colMap.GetValueOrDefault("رقم السجين", -1);
            int hallCol = colMap.GetValueOrDefault("ممر", -1);
            int cabCol = colMap.GetValueOrDefault("كبينة", -1);
            int shelfCol = colMap.GetValueOrDefault("رف", -1);

            var seenSeqInSheet = new HashSet<int>();
            var seenPrisonersInSheet = new HashSet<string>();
            var locationValues = new List<(int? Hall, int? Cab, int? Shelf)>();

            int recordCount = 0;
            bool hasWarnings = !titleParsed;
            int prevSeq = 0;

            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                string rawSeq = seqCol > 0 ? row.Cell(seqCol).GetString().Trim() : "";
                string rawName = nameCol > 0 ? row.Cell(nameCol).GetString().Trim() : "";
                string rawNum = numCol > 0 ? row.Cell(numCol).GetString().Trim() : "";

                if (string.IsNullOrEmpty(rawSeq) &&
                    string.IsNullOrEmpty(rawName) &&
                    string.IsNullOrEmpty(rawNum)) continue;

                int? hall = hallCol > 0 ? ParseInt(row.Cell(hallCol).GetString()) : null;
                int? cab = cabCol > 0 ? ParseInt(row.Cell(cabCol).GetString()) : null;
                int? shelf = shelfCol > 0 ? ParseInt(row.Cell(shelfCol).GetString()) : null;
                locationValues.Add((hall, cab, shelf));

                int stagingRecordId = conn.ExecuteScalar<int>(@"
                    INSERT INTO ImportStagingRecords
                        (ImportBatchId, StagingDossierId, SequenceNumber,
                         PersonName, PrisonerNumber,
                         HallwayNumber, CabinetNumber, ShelfNumber,
                         RowNumber, Status)
                    VALUES (@BatchId, @DossId, @Seq, @Name, @PNum,
                            @Hall, @Cab, @Shelf, @Row, 'Pending');
                    SELECT last_insert_rowid();",
                    new
                    {
                        BatchId = batchId,
                        DossId = stagingDossierId,
                        Seq = ParseInt(rawSeq),
                        Name = rawName,
                        PNum = rawNum,
                        Hall = hall,
                        Cab = cab,
                        Shelf = shelf,
                        Row = r
                    }, tx);

                recordCount++;
                bool rowHasWarning = false;
                string rowStatus = "Ready";

                // Sequence validation
                if (string.IsNullOrEmpty(rawSeq))
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.MissingSequence,
                        $"تسلسل مفقود في الصف {r}", "يرجى إضافة رقم التسلسل");
                    warnings++; rowHasWarning = true; rowStatus = "HasWarning";
                }
                else if (!int.TryParse(rawSeq, out int seqVal))
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.InvalidSequence,
                        $"تسلسل غير رقمي '{rawSeq}' في الصف {r}", "يرجى تصحيح رقم التسلسل");
                    warnings++; rowHasWarning = true; rowStatus = "HasWarning";
                }
                else
                {
                    if (seenSeqInSheet.Contains(seqVal))
                    {
                        AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                            ImportWarningTypes.DuplicateSequence,
                            $"تسلسل مكرر {seqVal} في الصف {r}", "يرجى مراجعة أرقام التسلسل");
                        warnings++; rowHasWarning = true; rowStatus = "HasWarning";
                    }
                    else
                    {
                        seenSeqInSheet.Add(seqVal);
                        if (prevSeq > 0 && seqVal != prevSeq + 1)
                        {
                            AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                                ImportWarningTypes.SequenceGap,
                                $"فجوة في التسلسل: بعد {prevSeq} جاء {seqVal} في الصف {r}",
                                "تأكد من عدم وجود صفوف مفقودة");
                            warnings++;
                        }
                        prevSeq = seqVal;
                    }
                }

                // Name validation
                if (string.IsNullOrEmpty(rawName))
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.MissingName,
                        $"اسم مفقود في الصف {r}", "يرجى إكمال الاسم");
                    warnings++; rowHasWarning = true; rowStatus = "HasWarning";
                }
                else if (rawName.Length < 3)
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.SuspiciousName,
                        $"اسم قصير جداً '{rawName}' في الصف {r}", "يرجى التحقق من الاسم");
                    warnings++;
                }

                // Prisoner number validation
                string cleanNum = Regex.Replace(rawNum, @"\s", "");
                if (string.IsNullOrEmpty(cleanNum))
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.MissingPrisonerNumber,
                        $"رقم السجين مفقود في الصف {r}", "يرجى إدخال رقم السجين");
                    warnings++; rowHasWarning = true; rowStatus = "HasWarning";
                }
                else if (cleanNum.Length != 10 || !cleanNum.All(char.IsDigit))
                {
                    AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                        ImportWarningTypes.InvalidPrisonerNumber,
                        $"رقم السجين غير صحيح '{cleanNum}' في الصف {r} (يجب أن يكون 10 أرقام)",
                        "يرجى تصحيح الرقم");
                    warnings++; rowHasWarning = true; rowStatus = "HasWarning"; duplicates++;
                }
                else
                {
                    if (seenPrisonersInSheet.Contains(cleanNum))
                    {
                        AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                            ImportWarningTypes.DuplicateInSheet,
                            $"رقم السجين {cleanNum} مكرر داخل نفس الشيت في الصف {r}",
                            "يرجى مراجعة الأرقام المكررة");
                        warnings++; rowHasWarning = true; rowStatus = "HasWarning"; duplicates++;
                    }
                    else if (seenInImport.Contains(cleanNum))
                    {
                        AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                            ImportWarningTypes.DuplicateInImport,
                            $"رقم السجين {cleanNum} مكرر في ملف الاستيراد (صف {r})",
                            "يرجى مراجعة الأرقام المكررة عبر الشيتات");
                        warnings++; rowHasWarning = true; rowStatus = "HasWarning"; duplicates++;
                    }
                    else if (existingInDb.Contains(cleanNum))
                    {
                        AddWarning(conn, tx, batchId, stagingDossierId, stagingRecordId,
                            ImportWarningTypes.DuplicateInDatabase,
                            $"رقم السجين {cleanNum} موجود مسبقاً في قاعدة البيانات (صف {r})",
                            "يرجى مراجعة: السجل موجود مسبقاً");
                        warnings++; rowHasWarning = true; rowStatus = "HasWarning"; duplicates++;
                    }
                    else
                    {
                        seenPrisonersInSheet.Add(cleanNum);
                        seenInImport.Add(cleanNum);
                    }
                }

                if (rowHasWarning) hasWarnings = true;

                conn.Execute(
                    "UPDATE ImportStagingRecords SET Status = @S WHERE StagingRecordId = @Id",
                    new { S = rowStatus, Id = stagingRecordId }, tx);
            }

            // ── 6. Location analysis ─────────────────────────────────────────
            var validLocs = locationValues
                .Where(l => l.Hall.HasValue && l.Cab.HasValue && l.Shelf.HasValue)
                .ToList();

            int? finalHall = null, finalCab = null, finalShelf = null;

            if (validLocs.Count > 0)
            {
                var distinct = validLocs.Distinct().ToList();
                if (distinct.Count == 1)
                {
                    finalHall = distinct[0].Hall;
                    finalCab = distinct[0].Cab;
                    finalShelf = distinct[0].Shelf;
                }
                else
                {
                    var most = validLocs
                        .GroupBy(l => l)
                        .OrderByDescending(g => g.Count())
                        .First().Key;
                    finalHall = most.Hall;
                    finalCab = most.Cab;
                    finalShelf = most.Shelf;

                    AddWarning(conn, tx, batchId, stagingDossierId, null,
                        ImportWarningTypes.MixedLocationInDossier,
                        $"مواقع مختلفة في الشيت '{sheetName}'. سيُستخدم: ممر {finalHall} - كبينة {finalCab} - رف {finalShelf}",
                        "يرجى التحقق من الموقع الصحيح");
                    warnings++; hasWarnings = true;
                }

                // FIX: InvalidLocation — was silently ignored; now warns manager to decide
                if (finalHall.HasValue && finalCab.HasValue && finalShelf.HasValue)
                {
                    int locExists = conn.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM Locations
                        WHERE HallwayNumber = @H AND CabinetNumber = @C AND ShelfNumber = @S",
                        new { H = finalHall, C = finalCab, S = finalShelf }, tx);

                    if (locExists == 0)
                    {
                        // FIX: Actually add the warning and mark NeedsReview so manager can decide
                        AddWarning(conn, tx, batchId, stagingDossierId, null,
                            ImportWarningTypes.InvalidLocation,
                            $"الموقع ممر {finalHall} - كبينة {finalCab} - رف {finalShelf} غير موجود في النظام",
                            "سيتم إنشاء الموقع تلقائياً عند الاعتماد — أو يرجى إنشاؤه مسبقاً من إعدادات الأرشيف");
                        warnings++; hasWarnings = true;
                        // Status becomes NeedsReview so manager sees it — but it is NOT a blocker
                        sheetStatus = "NeedsReview";
                    }
                }
            }

            // ── 7. Count mismatch ─────────────────────────────────────────────
            if (expectedCount.HasValue && recordCount != expectedCount.Value)
            {
                AddWarning(conn, tx, batchId, stagingDossierId, null,
                    ImportWarningTypes.CountMismatch,
                    $"عدد الصفوف الفعلي ({recordCount}) لا يطابق العنوان ({expectedCount})",
                    "يرجى التأكد من صحة عدد الملفات");
                warnings++; hasWarnings = true;
            }

            // ── 8. Update staging dossier ─────────────────────────────────────
            string finalStatus = hasWarnings ? "NeedsReview" : "Ready";

            conn.Execute(@"
                UPDATE ImportStagingDossiers
                SET HallwayNumber  = @Hall,
                    CabinetNumber  = @Cab,
                    ShelfNumber    = @Shelf,
                    ActualRowCount = @Count,
                    Status         = @Status
                WHERE StagingDossierId = @Id",
                new
                {
                    Hall = finalHall,
                    Cab = finalCab,
                    Shelf = finalShelf,
                    Count = recordCount,
                    Status = finalStatus,
                    Id = stagingDossierId
                }, tx);

            return new SheetProcessResult(recordCount, warnings, duplicates, finalStatus);
        }

        // ── MANUAL UPDATE of staging dossier metadata (FIX: now fully implemented) ──
        /// <summary>
        /// Allows the manager to manually set metadata when title parsing failed.
        /// Resolves the MissingDossierMetadata warning and re-evaluates sheet status.
        /// </summary>
        public string? UpdateStagingDossier(int stagingDossierId,
            int dossierNumber, int hijriMonth, int hijriYear, int? expectedCount)
        {
            if (dossierNumber <= 0) return "رقم الدوسية غير صحيح.";
            if (hijriMonth < 1 || hijriMonth > 12) return "الشهر يجب أن يكون بين 1 و 12.";
            if (hijriYear < 1400 || hijriYear > 1600) return "السنة الهجرية غير صحيحة.";

            using var conn = _db.CreateConnection();

            // Check dossier number not already used in another staging dossier in this batch
            var batchId = conn.ExecuteScalar<int>(
                "SELECT ImportBatchId FROM ImportStagingDossiers WHERE StagingDossierId = @Id",
                new { Id = stagingDossierId });

            int dupInBatch = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM ImportStagingDossiers
                WHERE ImportBatchId = @BatchId
                AND DossierNumber = @DNum
                AND StagingDossierId != @Id",
                new { BatchId = batchId, DNum = dossierNumber, Id = stagingDossierId });

            if (dupInBatch > 0)
                return $"رقم الدوسية {dossierNumber} مستخدم مسبقاً في هذه الدفعة.";

            // Resolve the MissingDossierMetadata warning
            conn.Execute(@"
                UPDATE ImportWarnings
                SET IsResolved = 1, ResolvedByUserId = @UserId, ResolvedAt = @Now
                WHERE StagingDossierId = @Id
                AND WarningType = 'MissingDossierMetadata'
                AND IsResolved = 0",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Id = stagingDossierId
                });

            // Check if count mismatch warning should be updated
            if (expectedCount.HasValue)
            {
                int actualCount = conn.ExecuteScalar<int>(
                    "SELECT ActualRowCount FROM ImportStagingDossiers WHERE StagingDossierId = @Id",
                    new { Id = stagingDossierId });

                if (actualCount == expectedCount.Value)
                {
                    // Resolve existing count mismatch warning if it exists
                    conn.Execute(@"
                        UPDATE ImportWarnings
                        SET IsResolved = 1, ResolvedByUserId = @UserId, ResolvedAt = @Now
                        WHERE StagingDossierId = @Id
                        AND WarningType = 'CountMismatch'
                        AND IsResolved = 0",
                        new
                        {
                            UserId = UserSession.CurrentUser?.UserId,
                            Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Id = stagingDossierId
                        });
                }
            }

            conn.Execute(@"
                UPDATE ImportStagingDossiers
                SET DossierNumber     = @DNum,
                    HijriMonth        = @Month,
                    HijriYear         = @Year,
                    ExpectedFileCount = @Expected,
                    Status            = 'NeedsReview'
                WHERE StagingDossierId = @Id",
                new
                {
                    DNum = dossierNumber,
                    Month = hijriMonth,
                    Year = hijriYear,
                    Expected = expectedCount,
                    Id = stagingDossierId
                });

            return null;
        }

        // ── GET STAGED DOSSIERS ───────────────────────────────────────────────
        public List<StagedDossierView> GetStagedDossiers(int batchId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<StagedDossierView>(@"
                SELECT
                    sd.StagingDossierId,
                    sd.SheetName,
                    sd.DossierNumber,
                    sd.HijriMonth,
                    sd.HijriYear,
                    sd.ExpectedFileCount AS ExpectedCount,
                    sd.ActualRowCount,
                    sd.Status,
                    COUNT(iw.WarningId) AS WarningCount
                FROM ImportStagingDossiers sd
                LEFT JOIN ImportWarnings iw
                    ON iw.StagingDossierId = sd.StagingDossierId AND iw.IsResolved = 0
                WHERE sd.ImportBatchId = @BatchId
                GROUP BY sd.StagingDossierId
                ORDER BY sd.DossierNumber",
                new { BatchId = batchId }).AsList();
        }

        // ── GET WARNINGS ──────────────────────────────────────────────────────
        public List<ImportWarningView> GetWarnings(int batchId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ImportWarningView>(@"
                SELECT
                    iw.WarningId,
                    sd.SheetName,
                    sr.RowNumber,
                    sd.DossierNumber,
                    iw.WarningType,
                    iw.WarningMessage,
                    COALESCE(iw.SuggestedAction, '') AS SuggestedAction,
                    iw.IsResolved
                FROM ImportWarnings iw
                LEFT JOIN ImportStagingDossiers sd ON sd.StagingDossierId = iw.StagingDossierId
                LEFT JOIN ImportStagingRecords  sr ON sr.StagingRecordId  = iw.StagingRecordId
                WHERE iw.ImportBatchId = @BatchId
                ORDER BY sd.DossierNumber, sr.RowNumber",
                new { BatchId = batchId }).AsList();
        }

        // ── CAN APPROVE ───────────────────────────────────────────────────────
        public string? CanApprove(int batchId)
        {
            using var conn = _db.CreateConnection();
            var blockers = conn.Query<string>(@"
                SELECT DISTINCT WarningType
                FROM ImportWarnings
                WHERE ImportBatchId = @BatchId AND IsResolved = 0
                AND WarningType IN (
                    'MissingDossierMetadata','MissingHeaderRow',
                    'MissingPrisonerNumber','InvalidPrisonerNumber',
                    'DuplicatePrisonerNumberInSheet',
                    'DuplicatePrisonerNumberInImport',
                    'DuplicatePrisonerNumberInDatabase',
                    'MissingName'
                )",
                new { BatchId = batchId }).ToList();

            if (blockers.Count == 0) return null;
            return "لا يمكن الاعتماد بسبب تحذيرات غير محلولة:\n" +
                   string.Join("\n", blockers.Select(TranslateWarningType));
        }

        // ── CHECK FOR EDITED RECORDS (for rollback warning) ───────────────────
        public int CountEditedRecords(int batchId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE ImportBatchId = @BatchId
                  AND DeletedAt IS NULL
                  AND UpdatedAt IS NOT NULL",
                new { BatchId = batchId });
        }

        // ── APPROVE BATCH (FIX: backup is now called by the UI layer before this) ──
        /// <summary>
        /// Caller MUST call BackupService.CreateBackup() BEFORE calling this method.
        /// Returns null on success, error string on failure.
        /// </summary>
        public string? ApproveBatch(int batchId)
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;

                var stagedDossiers = conn.Query(@"
                    SELECT * FROM ImportStagingDossiers
                    WHERE ImportBatchId = @BatchId AND Status != 'Rejected'",
                    new { BatchId = batchId }, tx).ToList();

                var stagedRecords = conn.Query(@"
                    SELECT * FROM ImportStagingRecords
                    WHERE ImportBatchId = @BatchId AND Status != 'Rejected'",
                    new { BatchId = batchId }, tx).ToList();

                var recordsByStagingDossier = stagedRecords
                    .GroupBy(r => (int)r.StagingDossierId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var sd in stagedDossiers)
                {
                    int stagingDossierId = (int)sd.StagingDossierId;

                    int? locationId = null;
                    if (sd.HallwayNumber != null && sd.CabinetNumber != null && sd.ShelfNumber != null)
                        locationId = GetOrCreateLocation(conn, tx,
                            (int)sd.HallwayNumber, (int)sd.CabinetNumber, (int)sd.ShelfNumber, now);

                    int existingDossierId = 0;
                    if (sd.DossierNumber != null)
                        existingDossierId = conn.ExecuteScalar<int>(
                            "SELECT COALESCE((SELECT DossierId FROM Dossiers WHERE DossierNumber = @N AND DeletedAt IS NULL), 0)",
                            new { N = (int)sd.DossierNumber }, tx);

                    int dossierId;
                    if (existingDossierId > 0)
                    {
                        dossierId = existingDossierId;
                    }
                    else
                    {
                        if (sd.DossierNumber == null || sd.HijriMonth == null || sd.HijriYear == null)
                            continue;

                        dossierId = conn.ExecuteScalar<int>(@"
                            INSERT INTO Dossiers
                                (DossierNumber, HijriMonth, HijriYear,
                                 ExpectedFileCount, CurrentLocationId,
                                 Status, CreatedByUserId, CreatedAt, ImportBatchId)
                            VALUES
                                (@DNum, @Month, @Year,
                                 @Expected, @LocId,
                                 'Open', @UserId, @Now, @BatchId);
                            SELECT last_insert_rowid();",
                            new
                            {
                                DNum = (int)sd.DossierNumber,
                                Month = (int)sd.HijriMonth,
                                Year = (int)sd.HijriYear,
                                Expected = sd.ExpectedFileCount == null ? (object)DBNull.Value : (int)sd.ExpectedFileCount,
                                LocId = locationId.HasValue ? (object)locationId.Value : DBNull.Value,
                                UserId = userId,
                                Now = now,
                                BatchId = batchId
                            }, tx);
                    }

                    if (!recordsByStagingDossier.TryGetValue(stagingDossierId, out var records))
                        continue;

                    foreach (var rec in records)
                    {
                        if (rec.PersonName == null || string.IsNullOrWhiteSpace((string)rec.PersonName))
                            continue;

                        string pNum = ((string?)rec.PrisonerNumber ?? "").Trim();
                        if (pNum.Length != 10 || !pNum.All(char.IsDigit)) continue;

                        int dupCheck = conn.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM Records WHERE PrisonerNumber = @P AND DeletedAt IS NULL",
                            new { P = pNum }, tx);
                        if (dupCheck > 0) continue;

                        conn.Execute(@"
                            INSERT INTO Records
                                (DossierId, SequenceNumber, PersonName,
                                 PrisonerNumber, Status, CreatedByUserId, CreatedAt, ImportBatchId)
                            VALUES
                                (@DossId, @Seq, @Name,
                                 @PNum, 'Active', @UserId, @Now, @BatchId)",
                            new
                            {
                                DossId = dossierId,
                                Seq = rec.SequenceNumber == null ? 0 : (int)rec.SequenceNumber,
                                Name = ((string)rec.PersonName).Trim(),
                                PNum = pNum,
                                UserId = userId,
                                Now = now,
                                BatchId = batchId
                            }, tx);
                    }
                }

                var batch = conn.QuerySingle(
                    "SELECT * FROM ImportBatches WHERE ImportBatchId = @Id",
                    new { Id = batchId }, tx);

                conn.Execute(@"
                    INSERT INTO AuditLog
                        (UserId, ActionType, EntityType, EntityId, Description, CreatedAt)
                    VALUES (@UserId, @Action, 'ImportBatch', @EntityId, @Desc, @Now)",
                    new
                    {
                        UserId = userId,
                        Action = AuditActions.ExcelImportCompleted,
                        EntityId = batchId,
                        Desc = $"اعتماد استيراد الملف: {batch.FileName} — {batch.TotalRecords} سجل",
                        Now = now
                    }, tx);

                conn.Execute(@"
                    UPDATE ImportBatches
                    SET Status = 'Imported', ApprovedByUserId = @UserId, ApprovedAt = @Now
                    WHERE ImportBatchId = @Id",
                    new { UserId = userId, Now = now, Id = batchId }, tx);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                try { conn.Execute("UPDATE ImportBatches SET Status = 'Failed' WHERE ImportBatchId = @Id", new { Id = batchId }); }
                catch { /* ignore */ }
                return $"خطأ أثناء الاعتماد: {ex.Message}";
            }
        }

        // ── ROLLBACK BATCH (FIX: backup called by UI, rollback warning shown by UI) ──
        /// <summary>
        /// Caller MUST call BackupService.CreateBackup() BEFORE calling this method.
        /// </summary>

        public string? RollbackBatch(int batchId)
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                int userId = UserSession.CurrentUser?.UserId ?? 0;

                // ── 1. Soft-delete all imported records for this batch ────────────
                conn.Execute(@"
            UPDATE Records
            SET DeletedAt       = @Now,
                DeletedByUserId = @UserId,
                Status          = 'Deleted'
            WHERE ImportBatchId = @BatchId
            AND   DeletedAt IS NULL",
                    new { Now = now, UserId = userId, BatchId = batchId }, tx);

                // ── 2. Find every dossier created by this batch ───────────────────
                var importedDossierIds = conn.Query<int>(
                    "SELECT DossierId FROM Dossiers WHERE ImportBatchId = @BatchId",
                    new { BatchId = batchId }, tx).ToList();

                // ── 3. Soft-delete dossiers that have no surviving manual records ─
                //       A dossier that already contained manual records before the
                //       import should NOT be soft-deleted — only its imported records
                //       (handled above) are removed.
                foreach (var did in importedDossierIds)
                {
                    int manualRecords = conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM Records
                WHERE DossierId     = @Did
                AND  (ImportBatchId IS NULL OR ImportBatchId != @BatchId)
                AND   DeletedAt     IS NULL",
                        new { Did = did, BatchId = batchId }, tx);

                    if (manualRecords > 0)
                        continue;   // leave this dossier intact — it has non-import records

                    // Soft-delete the dossier
                    conn.Execute(@"
                UPDATE Dossiers
                SET DeletedAt       = @Now,
                    DeletedByUserId = @UserId,
                    Status          = 'Archived',
                    UpdatedAt       = @Now
                WHERE DossierId = @Did",
                        new { Now = now, UserId = userId, Did = did }, tx);

                    // Per-dossier audit entry — mirrors DossierService.DeleteDossier
                    conn.Execute(@"
                INSERT INTO AuditLog
                    (UserId, ActionType, EntityType, EntityId, Description, CreatedAt)
                VALUES (@UserId, @Action, 'Dossier', @EntityId, @Desc, @Now)",
                        new
                        {
                            UserId = userId,
                            Action = AuditActions.DossierDeleted,
                            EntityId = did,
                            Desc = $"حذف دوسية {did} — تراجع عن استيراد الدفعة رقم {batchId}",
                            Now = now
                        }, tx);
                }

                // ── 4. Mark the batch as rolled back ─────────────────────────────
                conn.Execute(
                    "UPDATE ImportBatches SET Status = 'RolledBack' WHERE ImportBatchId = @Id",
                    new { Id = batchId }, tx);

                // ── 5. Batch-level audit entry ────────────────────────────────────
                conn.Execute(@"
            INSERT INTO AuditLog
                (UserId, ActionType, EntityType, EntityId, Description, CreatedAt)
            VALUES (@UserId, 'ImportRolledBack', 'ImportBatch', @EntityId, @Desc, @Now)",
                    new
                    {
                        UserId = userId,
                        EntityId = batchId,
                        Desc = $"تم التراجع عن استيراد الدفعة رقم {batchId}",
                        Now = now
                    }, tx);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return $"خطأ أثناء التراجع: {ex.Message}";
            }
        }

        // ── GET RECENT BATCHES ────────────────────────────────────────────────
        public List<ImportBatch> GetRecentBatches(int limit = 5)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ImportBatch>(@"
                SELECT * FROM ImportBatches
                ORDER BY CreatedAt DESC LIMIT @Limit",
                new { Limit = limit }).AsList();
        }

        // ── RESOLVE WARNING ───────────────────────────────────────────────────
        public void ResolveWarning(int warningId)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE ImportWarnings
                SET IsResolved = 1, ResolvedByUserId = @UserId, ResolvedAt = @Now
                WHERE WarningId = @Id",
                new
                {
                    UserId = UserSession.CurrentUser?.UserId,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Id = warningId
                });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private static string GetRowText(IXLWorksheet ws, int row)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var cell in ws.Row(row).CellsUsed())
                sb.Append(cell.GetString()).Append(' ');
            return sb.ToString();
        }

        private static int? ParseInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Regex.Replace(s.Trim(), @"[^\d]", "");
            return int.TryParse(s, out int v) ? v : null;
        }

        private static void AddWarning(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            Microsoft.Data.Sqlite.SqliteTransaction tx,
            int batchId, int stagingDossierId, int? stagingRecordId,
            string warningType, string message, string suggestedAction)
        {
            conn.Execute(@"
                INSERT INTO ImportWarnings
                    (ImportBatchId, StagingDossierId, StagingRecordId,
                     WarningType, WarningMessage, SuggestedAction, IsResolved)
                VALUES (@BatchId, @DossId, @RecId, @Type, @Msg, @Action, 0)",
                new
                {
                    BatchId = batchId,
                    DossId = stagingDossierId,
                    RecId = stagingRecordId,
                    Type = warningType,
                    Msg = message,
                    Action = suggestedAction
                }, tx);
        }

        private int GetOrCreateLocation(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            Microsoft.Data.Sqlite.SqliteTransaction tx,
            int hallway, int cabinet, int shelf, string now)
        {
            var existing = conn.ExecuteScalar<int?>(@"
                SELECT LocationId FROM Locations
                WHERE HallwayNumber = @H AND CabinetNumber = @C AND ShelfNumber = @S",
                new { H = hallway, C = cabinet, S = shelf }, tx);

            if (existing.HasValue) return existing.Value;

            return conn.ExecuteScalar<int>(@"
                INSERT INTO Locations
                    (HallwayNumber, CabinetNumber, ShelfNumber, IsActive, CreatedAt)
                VALUES (@H, @C, @S, 1, @Now);
                SELECT last_insert_rowid();",
                new { H = hallway, C = cabinet, S = shelf, Now = now }, tx);
        }

        private static string TranslateWarningType(string t) => t switch
        {
            ImportWarningTypes.MissingDossierMetadata => "بيانات دوسية مفقودة",
            ImportWarningTypes.MissingHeaderRow => "صف ترويسة مفقود",
            ImportWarningTypes.MissingPrisonerNumber => "رقم سجين مفقود",
            ImportWarningTypes.InvalidPrisonerNumber => "رقم سجين غير صحيح",
            ImportWarningTypes.DuplicateInSheet => "رقم سجين مكرر في الشيت",
            ImportWarningTypes.DuplicateInImport => "رقم سجين مكرر في الاستيراد",
            ImportWarningTypes.DuplicateInDatabase => "رقم سجين موجود في قاعدة البيانات",
            ImportWarningTypes.MissingName => "اسم مفقود",
            ImportWarningTypes.InvalidLocation => "موقع غير موجود في النظام",
            _ => t
        };
    }
}