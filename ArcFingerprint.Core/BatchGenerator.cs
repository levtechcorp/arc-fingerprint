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
        EditionKind editionKind = EditionKind.Arc)
    {
        if (!Enum.IsDefined(editionKind)) throw new ArgumentOutOfRangeException(nameof(editionKind));
        var readers = Recipients.Validate(recipients);
        var book = EpubBook.Load(master);
        if (!Directory.Exists(outputParent)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var batchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(Path.GetFullPath(outputParent), ".arc-incomplete-" + batchId);
        var editionLabel = editionKind switch { EditionKind.Beta => "Beta", EditionKind.Alpha => "Alpha", _ => "ARC" };
        var finished = Path.Combine(Path.GetFullPath(outputParent), editionLabel + "-" + batchId);
        Directory.CreateDirectory(staging);
        try
        {
            var map = new StringBuilder("ArcId,Name,Email,FileName,EditionType,CopySha256,MasterSha256,CreatedUtc\r\n");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < readers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id;
                do { id = "ARC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); } while (!ids.Add(id));
                string file = BuildFileName(book.Title, readers[i].Name, editionLabel, i + 1, id, staging);
                string copyPath = Path.Combine(staging, file);
                book.WriteCopy(copyPath, id);
                var verified = EpubBook.Load(copyPath, true);
                verified.Verify(id);
                var fields = new[] { id, readers[i].Name, readers[i].Email, file, editionLabel, verified.SourceHash, book.SourceHash, DateTime.UtcNow.ToString("O") };
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
                Fingerprint = "Additional dc:identifier plus XML comment in every manifest XHTML document. Original publication identifier preserved."
            }, new JsonSerializerOptions { WriteIndented = true }));
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

    private static string BuildFileName(string title, string recipient, string edition, int number, string id, string directory)
    {
        // Keep filenames readable while staying below common Windows path limits.
        const string suffix = " - 000-XXXXXXXX.epub";
        int available = Math.Min(180, 245 - directory.Length - 1 - suffix.Length - edition.Length - 3);
        if (available < 24) throw new PathTooLongException("The selected output folder path is too long. Choose a folder closer to the drive root.");
        int each = Math.Max(12, available / 2);
        var safeTitle = SafePart(title, each);
        var safeRecipient = SafePart(recipient, each);
        return $"{safeTitle} - {edition} - {safeRecipient} - {number:000}-{id[4..12]}.epub";
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
