using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcFingerprint.Core;

public sealed record BatchProgress(int Completed, int Total);
public sealed record BatchResult(string Folder, int Count);
public enum EditionKind { Arc, Beta, Alpha }

public static class BatchGenerator
{
    public static BatchResult Generate(string master, IEnumerable<Recipient> recipients, string outputParent,
        IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default,
        EditionKind editionKind = EditionKind.Arc, bool visiblePdfMark = false, EmailDraftOptions? emailDraft = null,
        Func<IReadOnlyList<EmailDraftPreview>, bool>? previewEmailDrafts = null, bool pageLevelPdfFingerprints = false,
        bool epubStylesheetFingerprint = false, bool pdfCoverMark = false, bool epubThesaurusVariants = false,
        bool repeatXhtmlFingerprint = true)
    {
        if (!Enum.IsDefined(editionKind)) throw new ArgumentOutOfRangeException(nameof(editionKind));
        if (Path.GetExtension(master).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return PdfBatchGenerator.Generate(master, recipients, outputParent, progress, cancellationToken, editionKind, visiblePdfMark, emailDraft, previewEmailDrafts, pageLevelPdfFingerprints, pdfCoverMark);
        if (!Path.GetExtension(master).Equals(".epub", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an EPUB or PDF master file.");
        var readers = Recipients.Validate(recipients);
        emailDraft?.Validate();
        var book = EpubBook.Load(master);
        if (!Directory.Exists(outputParent)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var batchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(Path.GetFullPath(outputParent), ".arc-incomplete-" + batchId);
        var editionLabel = editionKind switch { EditionKind.Beta => "Beta", EditionKind.Alpha => "Alpha", _ => "ARC" };
        var finished = Path.Combine(Path.GetFullPath(outputParent), editionLabel + "-" + batchId);
        Directory.CreateDirectory(staging);
        try
        {
            var batchEmailDraft = emailDraft is null ? null : EmailDraftSettingsStore.Save(staging, emailDraft);
            var map = new StringBuilder("ArcId,Name,Email,FileName,EditionType,CopySha256,MasterSha256,CreatedUtc,Format\r\n");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var emailPreviews = new List<EmailDraftPreview>();
            for (int i = 0; i < readers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id;
                do { id = "ARC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); } while (!ids.Add(id));
                string file = BuildFileName(book.Title, readers[i].Name, editionLabel, i + 1, id, staging, ".epub");
                string copyPath = Path.Combine(staging, file);
                book.WriteCopy(copyPath, id, epubStylesheetFingerprint, epubThesaurusVariants, repeatXhtmlFingerprint);
                var verified = EpubBook.Load(copyPath, true);
                verified.Verify(id, repeatXhtmlFingerprint);
                if (batchEmailDraft is not null)
                {
                    var emlPath = Path.ChangeExtension(copyPath, ".eml");
                    EmailDraftGenerator.Write(emlPath, readers[i], book.Title, editionLabel, copyPath, batchEmailDraft);
                    emailPreviews.Add(EmailDraftGenerator.Preview(readers[i], book.Title, editionLabel, copyPath, emlPath, batchEmailDraft));
                }
                RecipientPackageBuilder.Create(staging, i + 1, readers[i].Name, copyPath, batchEmailDraft is null ? null : Path.ChangeExtension(copyPath, ".eml"));
                var fields = new[] { id, readers[i].Name, readers[i].Email, file, editionLabel, verified.SourceHash, book.SourceHash, DateTime.UtcNow.ToString("O"), "EPUB" };
                map.AppendLine(string.Join(",", fields.Select(Recipients.CsvField)));
                progress?.Report(new(i + 1, readers.Count));
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, "PRIVATE-recipient-mapping.csv"), map.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(staging, "validation-report.json"), JsonSerializer.Serialize(new
            {
                Title = book.Title, EditionType = editionLabel, MasterSha256 = book.SourceHash, RecipientCount = readers.Count,
                XhtmlDocumentsPerCopy = book.DocumentCount, CompletedUtc = DateTime.UtcNow,
                ObfuscatedFontsPreserved = book.ObfuscatedFontCount,
                Validation = "Built-in structural and fingerprint checks passed for master and every copy. Not a full EPUBCheck conformance report.",
                Fingerprint = new
                {
                    MetadataId = true,
                    RepeatedXhtmlId = repeatXhtmlFingerprint,
                    StylesheetSignal = epubStylesheetFingerprint,
                    ThesaurusTextVariation = epubThesaurusVariants,
                    Description = "Additional publication metadata ID with optional repeated XHTML, CSS, and text variation signals. Original publication identifier preserved."
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (batchEmailDraft is not null && previewEmailDrafts is not null && !previewEmailDrafts(emailPreviews))
                throw new OperationCanceledException("Email draft review was cancelled.");
            Directory.Move(staging, finished);
            return new(finished, readers.Count);
        }
        catch
        {
            // Only this run's freshly created staging directory is eligible for cleanup.
            try { Directory.Delete(staging, true); } catch { /* Preserve original error; an incomplete folder may remain. */ }
            throw;
        }
    }

    internal static string BuildFileName(string title, string recipient, string edition, int number, string id, string directory, string extension)
    {
        // Keep filenames readable while staying below common Windows path limits.
        string suffix = " - 000-XXXXXXXX" + extension;
        int available = Math.Min(180, 245 - directory.Length - 1 - suffix.Length - edition.Length - 3);
        if (available < 24) throw new PathTooLongException("The selected output folder path is too long. Choose a folder closer to the drive root.");
        int each = Math.Max(12, available / 2);
        var safeTitle = SafePart(title, each);
        var safeRecipient = SafePart(recipient, each);
        return $"{safeTitle} - {edition} - {safeRecipient} - {number:000}-{id[4..12]}{extension}";
    }

    private static string SafePart(string value, int maxLength)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*"));
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (cleaned.Length == 0) cleaned = "Untitled";
        var deviceName = cleaned.Split('.')[0].TrimEnd();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(deviceName, StringComparer.OrdinalIgnoreCase)) cleaned = "_" + cleaned;
        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd(' ', '.') : cleaned;
    }
}
