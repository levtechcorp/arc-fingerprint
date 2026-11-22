using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace ArcFingerprint.Core;

public sealed record AppendBatchResult(string Folder, string FileName, int RecipientCount, string ArcId, bool ReportUpdated);
internal sealed record BatchMapRow(string Id, string Name, string Email, string FileName, string Edition, string CopyHash, string MasterHash, string Format);

/// <summary>Adds a single reader to a previously generated batch after verifying its source and existing copies.</summary>
public static class ExistingBatchAppender
{
    public static AppendBatchResult Add(string mappingPath, string cleanMasterPath, Recipient recipient,
        EmailDraftOptions? emailDraft = null, Func<EmailDraftPreview, bool>? previewEmailDraft = null, string? mergedEdition = null)
    {
        var newReader = Recipients.Validate(new[] { recipient }).Single();
        mappingPath = Path.GetFullPath(mappingPath);
        cleanMasterPath = Path.GetFullPath(cleanMasterPath);
        if (!Path.GetFileName(mappingPath).Equals(RecipientMapping.DefaultFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Choose the batch's {RecipientMapping.DefaultFileName} file.");
        if (!File.Exists(mappingPath)) throw new FileNotFoundException("The selected private mapping file was not found.", mappingPath);

        string initialMappingHash = HashFile(mappingPath);
        var batchFolder = Path.GetDirectoryName(mappingPath)!;
        bool mergedBatch = false;
        try
        {
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(batchFolder, "validation-report.json")));
            mergedBatch = report.RootElement.TryGetProperty("MergedBatch", out var merged) && merged.ValueKind == JsonValueKind.True;
        }
        catch (InvalidDataException) { throw; }
        catch { }
        var savedEmailDraft = EmailDraftSettingsStore.Load(batchFolder);
        var activeEmailDraft = savedEmailDraft ?? emailDraft;
        if (activeEmailDraft is not null) activeEmailDraft.Validate();
        var rows = ReadRows(mappingPath);
        if (rows.Count >= 1000) throw new InvalidDataException("A batch can contain at most 1,000 recipients.");
        if (rows.Any(r => r.Email.Equals(newReader.Email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"{newReader.Email} is already in this batch.");

        string format = rows[0].Format;
        string edition = rows[0].Edition;
        string sourceHash = rows[0].MasterHash;
        if (!mergedBatch && rows.Any(r => r.Format != format || r.Edition != edition || !r.MasterHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The mapping contains mixed formats, editions, or master files, so it cannot be safely extended.");
        if (!mergedBatch && sourceHash.Length == 0) throw new InvalidDataException("This older mapping has no verified clean-master hash. Start a new batch from the clean master to add another reader.");
        foreach (var row in rows)
        {
            var existingPath = Path.GetFullPath(Path.Combine(batchFolder, row.FileName));
            if (!existingPath.StartsWith(batchFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The mapping contains a filename outside its batch folder.");
            if (!File.Exists(existingPath)) throw new FileNotFoundException($"An existing mapped copy is missing: {row.FileName}");
            using var stream = File.OpenRead(existingPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"An existing copy has changed since this batch was created: {row.FileName}. No files were added.");
            if (row.Format == "EPUB")
            {
                var existingBook = EpubBook.Load(existingPath, true);
                if (!existingBook.Fingerprints().Contains(row.Id, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"An existing EPUB's fingerprint does not match the mapping: {row.FileName}.");
            }
            else if (PdfBook.Load(existingPath).FingerprintId != row.Id)
                throw new InvalidDataException($"An existing PDF's fingerprint does not match the mapping: {row.FileName}.");
        }

        string title;
        string extension;
        string checkedMasterHash;
        EpubBook? epub = null;
        PdfBook? pdf = null;
        if (Path.GetExtension(cleanMasterPath).Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            epub = EpubBook.Load(cleanMasterPath);
            title = epub.Title; checkedMasterHash = epub.SourceHash; extension = ".epub"; format = "EPUB";
        }
        else if (Path.GetExtension(cleanMasterPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            pdf = PdfBook.Load(cleanMasterPath);
            if (pdf.FingerprintId is not null) throw new InvalidDataException("Choose the original clean, unmarked PDF master.");
            title = pdf.Title; checkedMasterHash = pdf.SourceHash; extension = ".pdf"; format = "PDF";
        }
        else throw new InvalidDataException("Choose the original clean EPUB or PDF master used in this collection.");
        if (mergedBatch)
        {
            var matchingRows = rows.Where(r => r.Format == format && r.MasterHash.Equals(checkedMasterHash, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matchingRows.Length == 0)
                throw new InvalidDataException("This clean master is not represented in the consolidated mapping. Choose a master used to create one of its source batches.");
            var editions = matchingRows.Select(r => r.Edition).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!string.IsNullOrWhiteSpace(mergedEdition))
            {
                if (!editions.Contains(mergedEdition, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("The selected edition does not exist for this clean master in the consolidated collection.");
                edition = editions.Single(e => e.Equals(mergedEdition, StringComparison.OrdinalIgnoreCase));
            }
            else if (editions.Length == 1) edition = editions[0];
            else throw new InvalidDataException("This clean master appears under more than one edition in the consolidated collection. Choose ARC, Beta, or Alpha before adding the reader.");
            sourceHash = checkedMasterHash;
        }
        else if (!checkedMasterHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This is not the clean master used to create the selected batch. Its file hash does not match the private mapping.");

        var arcIds = rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string arcId;
        do { arcId = "ARC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); } while (!arcIds.Add(arcId));
        int nextSequence = rows.Select(r => Regex.Match(r.FileName, " - (?<number>[0-9]{3,})-[0-9A-F]{8}\\.", RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups["number"].Value)).DefaultIfEmpty(rows.Count).Max() + 1;
        string fileName = BatchGenerator.BuildFileName(title, newReader.Name, edition, nextSequence, arcId, batchFolder, extension);
        string finalCopy = Path.Combine(batchFolder, fileName);
        string finalEml = Path.ChangeExtension(finalCopy, ".eml");
        if (File.Exists(finalCopy) || (activeEmailDraft is not null && File.Exists(finalEml)))
            throw new IOException("A file with the new copy's name already exists in this batch. Rename or move it, then try again.");

        string staging = Path.Combine(batchFolder, ".arc-add-incomplete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool movedCopy = false;
        bool movedEml = false;
        string? packageFolder = null;
        try
        {
            if (savedEmailDraft is null && emailDraft is not null)
                activeEmailDraft = EmailDraftSettingsStore.Save(batchFolder, emailDraft);
            string stagedCopy = Path.Combine(staging, fileName);
            if (epub is not null)
            {
                epub.WriteCopy(stagedCopy, arcId);
                var verified = EpubBook.Load(stagedCopy, true); verified.Verify(arcId);
                if (!verified.SourceHash.Equals(HashFile(stagedCopy), StringComparison.Ordinal))
                    throw new InvalidDataException("The new EPUB copy failed its file integrity check.");
            }
            else
            {
                pdf!.WriteCopy(stagedCopy, arcId, edition, false);
                _ = PdfBook.Verify(stagedCopy, arcId);
            }
            if (activeEmailDraft is not null)
            {
                var stagedEml = Path.ChangeExtension(stagedCopy, ".eml");
                EmailDraftGenerator.Write(stagedEml, newReader, title, edition, stagedCopy, activeEmailDraft);
                if (previewEmailDraft is not null && !previewEmailDraft(EmailDraftGenerator.Preview(newReader, title, edition, stagedCopy, stagedEml, activeEmailDraft)))
                    throw new OperationCanceledException("Email draft review was cancelled.");
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArcId"] = arcId, ["Name"] = newReader.Name, ["Email"] = newReader.Email, ["FileName"] = fileName,
                ["EditionType"] = edition, ["CopySha256"] = HashFile(stagedCopy), ["MasterSha256"] = checkedMasterHash,
                ["CreatedUtc"] = DateTime.UtcNow.ToString("O"), ["Format"] = format
            };
            if (mergedBatch) fields["SourceBatch"] = "Added to consolidated collection";
            var header = ReadHeader(mappingPath);
            var csvRow = string.Join(",", header.Select(h => Recipients.CsvField(fields.GetValueOrDefault(h, ""))));
            var original = File.ReadAllText(mappingPath, Encoding.UTF8);
            if (!HashFile(mappingPath).Equals(initialMappingHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The mapping changed while the batch was being checked. No files were added; reopen the feature and try again.");
            string separator = original.EndsWith("\n", StringComparison.Ordinal) ? "" : "\r\n";
            string tempMap = Path.Combine(staging, RecipientMapping.DefaultFileName);
            File.WriteAllText(tempMap, original + separator + csvRow + "\r\n", new UTF8Encoding(true));

            File.Move(stagedCopy, finalCopy); movedCopy = true;
            if (activeEmailDraft is not null) { File.Move(Path.ChangeExtension(stagedCopy, ".eml"), finalEml); movedEml = true; }
            packageFolder = RecipientPackageBuilder.Create(batchFolder, nextSequence, newReader.Name, finalCopy, activeEmailDraft is null ? null : finalEml);
            File.Replace(tempMap, mappingPath, null);

            bool reportUpdated = TryUpdateReport(batchFolder, rows.Count + 1);
            return new(batchFolder, fileName, rows.Count + 1, arcId, reportUpdated);
        }
        catch
        {
            if (movedEml) try { File.Delete(finalEml); } catch { }
            if (movedCopy) try { File.Delete(finalCopy); } catch { }
            if (packageFolder is not null) try { Directory.Delete(packageFolder, true); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    internal static List<BatchMapRow> ReadRows(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Mapping CSV exceeds 2 MB.");
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("The mapping CSV is empty.");
        if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length) throw new InvalidDataException("The mapping has duplicate column headers.");
        int Col(string name) => Array.FindIndex(header, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        int id = Col("ArcId"), name = Col("Name"), email = Col("Email"), file = Col("FileName"), edition = Col("EditionType"), copy = Col("CopySha256"), master = Col("MasterSha256"), format = Col("Format");
        if (new[] { id, name, email, file, edition, copy, master, format }.Any(i => i < 0))
            throw new InvalidDataException("This mapping lacks the edition, format, or integrity hashes needed to safely extend the batch. Use a mapping created by ARC Fingerprint 1.2.0 or later.");
        var rows = new List<BatchMapRow>();
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields()!;
            if (values.Length != header.Length) throw new InvalidDataException($"Mapping row {rows.Count + 2} has the wrong number of fields.");
            string field(int i) => values[i].Trim();
            if (!Regex.IsMatch(field(id), "\\AARC-[0-9A-F]{32}\\z", RegexOptions.IgnoreCase) ||
                !Regex.IsMatch(field(copy), "\\A[0-9A-F]{64}\\z", RegexOptions.IgnoreCase) ||
                (field(master).Length > 0 && !Regex.IsMatch(field(master), "\\A[0-9A-F]{64}\\z", RegexOptions.IgnoreCase)))
                throw new InvalidDataException($"Mapping row {rows.Count + 2} has an invalid fingerprint or file hash.");
            if (field(file) != Path.GetFileName(field(file)) || field(file).Any(char.IsControl) ||
                new[] { field(name), field(email), field(edition), field(format) }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Mapping row {rows.Count + 2} contains invalid details.");
            rows.Add(new BatchMapRow(field(id).ToUpperInvariant(), field(name), field(email), field(file), field(edition), field(copy).ToUpperInvariant(), field(master).ToUpperInvariant(), field(format).ToUpperInvariant()));
            if (rows.Count > 1000) throw new InvalidDataException("Mapping CSV exceeds 1,000 recipients.");
        }
        if (rows.Count == 0) throw new InvalidDataException("This mapping has no existing recipients.");
        if (rows.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count) throw new InvalidDataException("The mapping has duplicate fingerprints.");
        foreach (var row in rows) _ = Recipients.Validate(new[] { new Recipient(row.Name, row.Email) });
        if (rows.Any(r => r.Format is not ("EPUB" or "PDF")) || rows.Any(r => r.Edition is not ("ARC" or "Beta" or "Alpha")))
            throw new InvalidDataException("The mapping has an unsupported format or edition.");
        return rows;
    }

    private static string[] ReadHeader(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(","); return parser.ReadFields()!;
    }

    private static string HashFile(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    private static bool TryUpdateReport(string folder, int count)
    {
        string path = Path.Combine(folder, "validation-report.json");
        string? temp = null;
        try
        {
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var values = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var updated = new Dictionary<string, object?>();
            foreach (var (key, value) in values) updated[key] = value;
            updated["RecipientCount"] = count;
            updated["LastExtendedUtc"] = DateTime.UtcNow;
            temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
            File.Replace(temp, path, null);
            temp = null;
            return true;
        }
        catch { return false; }
        finally { if (temp is not null) try { File.Delete(temp); } catch { } }
    }
}
