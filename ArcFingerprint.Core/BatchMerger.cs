using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace ArcFingerprint.Core;

public sealed record BatchMergeResult(string Folder, int RecipientCount, int SourceBatchCount, int DuplicateRowsSkipped,
    int EmailDraftsCopied, IReadOnlyList<string> SourceFolders, IReadOnlyList<BatchMergeHashChange> HashChanges);
public sealed record BatchMergeHashChange(string SourceBatch, string FileName, string SavedHash, string CurrentHash);

/// <summary>Combines existing batches into a new normalized collection without rewriting source books or IDs.</summary>
public static class BatchMerger
{
    private static readonly Regex IdPattern = new("\\AARC-[0-9A-F]{32}\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FileSequencePattern = new(" - (?<number>[0-9]{3,})-[0-9A-F]{8}\\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record InputRow(string Id, string Name, string Email, string FileName, string Edition, string CopyHash,
        string MasterHash, string CreatedUtc, string Format, string SourceBatch, string SourceFolder, string MappingHash, string SavedCopyHash);

    public static IReadOnlyList<string> Discover(string parentFolder)
    {
        parentFolder = Path.GetFullPath(parentFolder);
        if (!Directory.Exists(parentFolder)) throw new DirectoryNotFoundException("Choose the parent folder containing your batch folders.");
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        return Directory.EnumerateFiles(parentFolder, RecipientMapping.DefaultFileName, options)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static BatchMergeResult Merge(string parentFolder, string outputParent,
        Func<IReadOnlyList<BatchMergeHashChange>, bool>? confirmChangedCopies = null)
    {
        parentFolder = Path.GetFullPath(parentFolder);
        outputParent = Path.GetFullPath(outputParent);
        if (!Directory.Exists(parentFolder)) throw new DirectoryNotFoundException("Choose the parent folder containing your source batch folders.");
        if (!Directory.Exists(outputParent)) throw new DirectoryNotFoundException("Choose an existing folder for the merged batch.");
        if (IsWithinAny(outputParent, Discover(parentFolder).Select(p => Path.GetDirectoryName(p)!)))
            throw new InvalidDataException("Save the merged collection beside the source batches, not inside one of them.");

        var mappings = Discover(parentFolder);
        if (mappings.Count < 2) throw new InvalidDataException("Choose a parent folder with at least two separate ARC Fingerprint batches.");
        var allRows = new List<InputRow>();
        var sourceMapHashes = mappings.ToDictionary(path => path, HashFile, StringComparer.OrdinalIgnoreCase);
        var sourceFolders = new List<string>();
        var seenIds = new Dictionary<string, InputRow>(StringComparer.OrdinalIgnoreCase);
        int skippedDuplicates = 0;
        foreach (var mapping in mappings)
        {
            string folder = Path.GetDirectoryName(mapping)!;
            sourceFolders.Add(folder);
            string mapHash = sourceMapHashes[mapping];
            var rows = ReadLegacyRows(mapping, mapHash);
            foreach (var row in rows)
            {
                if (seenIds.TryGetValue(row.Id, out var prior))
                {
                    bool identical = prior.CopyHash.Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase) &&
                        prior.Email.Equals(row.Email, StringComparison.OrdinalIgnoreCase) && prior.Name.Equals(row.Name, StringComparison.Ordinal) &&
                        prior.Edition.Equals(row.Edition, StringComparison.OrdinalIgnoreCase) && prior.Format.Equals(row.Format, StringComparison.OrdinalIgnoreCase);
                    if (!identical) throw new InvalidDataException($"Fingerprint {row.Id} appears in more than one source with conflicting recipient or file data. Nothing was merged.");
                    skippedDuplicates++;
                    continue;
                }
                seenIds.Add(row.Id, row);
                allRows.Add(row);
                if (allRows.Count > 1000) throw new InvalidDataException("A merged collection can contain at most 1,000 distinct fingerprints.");
            }
        }
        if (allRows.Count == 0) throw new InvalidDataException("The selected folders contain no mapped book copies.");

        var hashChanges = allRows.Where(r => r.SavedCopyHash.Length > 0 && !r.SavedCopyHash.Equals(r.CopyHash, StringComparison.OrdinalIgnoreCase))
            .Select(r => new BatchMergeHashChange(r.SourceBatch, r.FileName, r.SavedCopyHash, r.CopyHash)).ToArray();
        if (hashChanges.Length > 0 && (confirmChangedCopies is null || !confirmChangedCopies(hashChanges)))
            throw new InvalidDataException($"Merge cancelled because {hashChanges.Length} mapped book copy/copies differ from their saved hashes. No source batches were changed.");

        var batchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(outputParent, ".arc-merge-incomplete-" + Guid.NewGuid().ToString("N"));
        string finished = Path.Combine(outputParent, "Merged-" + batchId);
        Directory.CreateDirectory(staging);
        int emails = 0, adjustedEmailNames = 0;
        try
        {
            var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mappingText = new StringBuilder("ArcId,Name,Email,FileName,EditionType,CopySha256,MasterSha256,CreatedUtc,Format,SourceBatch\r\n");
            for (int i = 0; i < allRows.Count; i++)
            {
                var row = allRows[i];
                string sourceCopy = SafeCopyPath(row.SourceFolder, row.FileName);
                if (!File.Exists(sourceCopy)) throw new FileNotFoundException($"Mapped copy is missing from {row.SourceBatch}: {row.FileName}", sourceCopy);
                string actualHash = HashFile(sourceCopy);
                if (!actualHash.Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"A source copy changed during merge preflight: {row.FileName}. Nothing was published.");
                ValidateEmbeddedId(sourceCopy, row);
                string title = row.Format == "EPUB" ? EpubBook.Load(sourceCopy, true).Title : PdfBook.Load(sourceCopy).Title;
                string outputName = BatchGenerator.BuildFileName(title, row.Name, row.Edition, i + 1, row.Id, staging, row.Format == "PDF" ? ".pdf" : ".epub");
                if (!outputNames.Add(outputName)) throw new InvalidDataException("Two source entries would have the same Windows filename after merge. Rename the conflicting reader name in its source batch and retry.");
                string outputCopy = Path.Combine(staging, outputName);
                File.Copy(sourceCopy, outputCopy);
                if (!HashFile(outputCopy).Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A copied book failed its byte-for-byte integrity check. Nothing was published.");

                string sourceEml = Path.ChangeExtension(sourceCopy, ".eml");
                string? outputEml = null;
                if (File.Exists(sourceEml))
                {
                    VerifyEmailDraft(sourceEml, row.Email, row.SourceBatch);
                    outputEml = Path.ChangeExtension(outputCopy, ".eml");
                    string draft = File.ReadAllText(sourceEml, Encoding.UTF8);
                    string oldEncodedName = Uri.EscapeDataString(row.FileName);
                    string newEncodedName = Uri.EscapeDataString(outputName);
                    string renamedDraft = draft
                        .Replace("filename*=utf-8''" + oldEncodedName, "filename*=utf-8''" + newEncodedName, StringComparison.OrdinalIgnoreCase)
                        .Replace("name*=utf-8''" + oldEncodedName, "name*=utf-8''" + newEncodedName, StringComparison.OrdinalIgnoreCase);
                    if (!renamedDraft.Equals(draft, StringComparison.Ordinal)) adjustedEmailNames++;
                    File.WriteAllText(outputEml, renamedDraft, new UTF8Encoding(false));
                    emails++;
                }
                RecipientPackageBuilder.Create(staging, i + 1, row.Name, outputCopy, outputEml);
                var values = new[] { row.Id, row.Name, row.Email, outputName, row.Edition, row.CopyHash, row.MasterHash,
                    row.CreatedUtc, row.Format, row.SourceBatch };
                mappingText.AppendLine(string.Join(",", values.Select(Recipients.CsvField)));
            }

            foreach (var mapping in mappings)
                if (!HashFile(mapping).Equals(sourceMapHashes[mapping], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A source mapping changed during merge. Nothing was published; rerun the merge.");

            File.WriteAllText(Path.Combine(staging, RecipientMapping.DefaultFileName), mappingText.ToString(), new UTF8Encoding(true));
            var report = new
            {
                Title = "Merged ARC Fingerprint collection", Format = "Mixed or multiple source projects", RecipientCount = allRows.Count,
                MergedBatch = true, SchemaVersion = 2, SourceBatchCount = sourceFolders.Count, SourceFolders = sourceFolders,
                DuplicateRowsSkipped = skippedDuplicates, EmailDraftsCopied = emails, EmailAttachmentNamesNormalized = adjustedEmailNames,
                OriginalArcIdsPreserved = true, EbookFileBytesPreserved = true,
                SourceCopyHashChanges = hashChanges,
                NormalizedStructure = "Current private mapping, validation report, and individual Send-to-readers folders.",
                EmailTemplatesMerged = false, MasterHashesMissing = allRows.Count(r => r.MasterHash.Length == 0),
                MasterHashes = allRows.Select(r => r.MasterHash).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                CompletedUtc = DateTime.UtcNow
            };
            File.WriteAllText(Path.Combine(staging, "validation-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(staging, finished);
            return new(finished, allRows.Count, sourceFolders.Count, skippedDuplicates, emails, sourceFolders, hashChanges);
        }
        catch
        {
            try { Directory.Delete(staging, true); } catch { }
            throw;
        }
    }

    private static List<InputRow> ReadLegacyRows(string mapping, string mapHash)
    {
        if (new FileInfo(mapping).Length > 2_000_000) throw new InvalidDataException($"Mapping is too large to safely import: {mapping}");
        using var parser = new TextFieldParser(mapping, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException($"Empty mapping: {mapping}");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length) throw new InvalidDataException($"Duplicate mapping headers: {mapping}");
        int Col(params string[] names) => Array.FindIndex(headers, h => names.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));
        int id = Col("ArcId", "ArcID", "FingerprintId", "Fingerprint ID", "Fingerprint");
        int name = Col("Name", "RecipientName", "Recipient Name", "Reader", "ReaderName");
        int email = Col("Email", "EmailAddress", "Email Address", "RecipientEmail");
        int file = Col("FileName", "Filename", "File Name", "BookFile", "CopyFile");
        if (new[] { id, name, email, file }.Any(i => i < 0)) throw new InvalidDataException($"Mapping lacks an ID, reader, email, or filename column: {mapping}");
        int edition = Col("EditionType", "Edition", "Stage");
        int copyHash = Col("CopySha256", "CopyHash", "FileSha256", "FileHash");
        int masterHash = Col("MasterSha256", "MasterHash", "SourceSha256");
        int created = Col("CreatedUtc", "Created", "CreatedAt");
        int format = Col("Format", "FileFormat");
        int sourceBatch = Col("SourceBatch", "SourceProject");
        string folder = Path.GetDirectoryName(Path.GetFullPath(mapping))!;
        string sourceName = Path.GetFileName(folder);
        var rows = new List<InputRow>();
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields()!;
            if (values.Length != headers.Length) throw new InvalidDataException($"Malformed row {rows.Count + 2} in {mapping}");
            string Get(int column) => column < 0 ? "" : values[column].Trim();
            string arcId = Get(id).ToUpperInvariant(), readerName = Get(name), readerEmail = Get(email), fileName = Get(file);
            if (!IdPattern.IsMatch(arcId) || !localIds.Add(arcId)) throw new InvalidDataException($"Invalid or repeated fingerprint ID in {mapping}: {arcId}");
            _ = Recipients.Validate(new[] { new Recipient(readerName, readerEmail) });
            if (fileName.Length == 0 || fileName != Path.GetFileName(fileName) || fileName.Any(char.IsControl))
                throw new InvalidDataException($"Unsafe or missing mapped filename in {mapping}: {fileName}");
            string fullCopy = SafeCopyPath(folder, fileName);
            if (!File.Exists(fullCopy)) throw new FileNotFoundException($"Mapped copy is missing in source project {sourceName}: {fileName}", fullCopy);
            string ext = Path.GetExtension(fileName);
            string detectedFormat = ext.Equals(".epub", StringComparison.OrdinalIgnoreCase) ? "EPUB" : ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "";
            if (detectedFormat.Length == 0) throw new InvalidDataException($"Unsupported mapped copy format: {fileName}");
            string formatValue = Get(format).ToUpperInvariant();
            if (formatValue.Length > 0 && formatValue != detectedFormat) throw new InvalidDataException($"Format column disagrees with file extension for {fileName}");
            string editionValue = Get(edition);
            if (editionValue.Length == 0)
            {
                var stageMatch = Regex.Match(fileName, " - (?<edition>ARC|Beta|Alpha) - ", RegexOptions.IgnoreCase);
                editionValue = stageMatch.Success ? stageMatch.Groups["edition"].Value : "ARC";
            }
            editionValue = editionValue.Equals("Beta", StringComparison.OrdinalIgnoreCase) ? "Beta" : editionValue.Equals("Alpha", StringComparison.OrdinalIgnoreCase) ? "Alpha" : editionValue.Equals("ARC", StringComparison.OrdinalIgnoreCase) ? "ARC" : throw new InvalidDataException($"Unsupported edition in mapping row for {fileName}");
            string actualCopyHash = HashFile(fullCopy);
            string savedCopyHash = Get(copyHash);
            if (savedCopyHash.Length > 0 && !Regex.IsMatch(savedCopyHash, "\\A[0-9A-F]{64}\\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException($"Invalid saved copy hash for {fileName}.");
            string sourceMasterHash = Get(masterHash);
            if (sourceMasterHash.Length > 0 && !Regex.IsMatch(sourceMasterHash, "\\A[0-9A-F]{64}\\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException($"Invalid source master hash for {fileName}");
            string createdUtc = Get(created);
            if (createdUtc.Length == 0) createdUtc = File.GetLastWriteTimeUtc(fullCopy).ToString("O");
            ValidateEmbeddedId(fullCopy, detectedFormat, arcId, fileName);
            string sourceLabel = Get(sourceBatch);
            if (sourceLabel.Length == 0) sourceLabel = sourceName;
            if (sourceLabel.Length > 512 || sourceLabel.Any(char.IsControl)) sourceLabel = sourceName;
            rows.Add(new(arcId, readerName, readerEmail, fileName, editionValue, actualCopyHash.ToUpperInvariant(), sourceMasterHash.ToUpperInvariant(),
                createdUtc, detectedFormat, sourceLabel, folder, mapHash, savedCopyHash.ToUpperInvariant()));
            if (rows.Count > 1000) throw new InvalidDataException($"Source batch exceeds 1,000 mapped copies: {sourceName}");
        }
        if (rows.Count == 0) throw new InvalidDataException($"Mapping contains no recipients: {mapping}");
        return rows;
    }

    private static bool IsWithinAny(string path, IEnumerable<string> folders)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return folders.Any(folder => full.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeCopyPath(string folder, string fileName)
    {
        if (fileName != Path.GetFileName(fileName)) throw new InvalidDataException("Mapped filenames must not include folder paths.");
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(folder, fileName));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A mapped copy path points outside its source batch.");
        return path;
    }

    private static void ValidateEmbeddedId(string path, InputRow row) => ValidateEmbeddedId(path, row.Format, row.Id, row.FileName);
    private static void ValidateEmbeddedId(string path, string format, string id, string fileName)
    {
        if (format == "EPUB")
        {
            if (!EpubBook.Load(path, true).Fingerprints().Contains(id, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Embedded EPUB fingerprint does not match the mapping: {fileName}");
        }
        else if (!PdfBook.Load(path).Fingerprints.Contains(id, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Embedded PDF fingerprint does not match the mapping: {fileName}");
    }

    private static void VerifyEmailDraft(string path, string email, string sourceName)
    {
        if (new FileInfo(path).Length > 100L * 1024 * 1024) throw new InvalidDataException($"Email draft is unexpectedly large in {sourceName}: {Path.GetFileName(path)}");
        string headers = string.Join("\n", File.ReadLines(path).Take(20));
        if (!headers.Contains("To: <" + email + ">", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Email draft recipient does not match the mapping in {sourceName}: {Path.GetFileName(path)}");
    }

    private static string HashFile(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
