using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcFingerprint.Core;

public sealed record BatchProgress(int Completed, int Total);
public sealed record BatchResult(string Folder, int Count);

public static class BatchGenerator
{
    public static BatchResult Generate(string master, IEnumerable<Recipient> recipients, string outputParent,
        IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var readers = Recipients.Validate(recipients);
        var book = EpubBook.Load(master);
        if (!Directory.Exists(outputParent)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var batchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(Path.GetFullPath(outputParent), ".arc-incomplete-" + batchId);
        var finished = Path.Combine(Path.GetFullPath(outputParent), "ARC-" + batchId);
        Directory.CreateDirectory(staging);
        try
        {
            var map = new StringBuilder("ArcId,Name,Email,FileName,CopySha256,MasterSha256,CreatedUtc\r\n");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < readers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id;
                do { id = "ARC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); } while (!ids.Add(id));
                string file = $"ARC-{i + 1:000}-{id[4..12]}.epub";
                string copyPath = Path.Combine(staging, file);
                book.WriteCopy(copyPath, id);
                var verified = EpubBook.Load(copyPath, true);
                verified.Verify(id);
                var fields = new[] { id, readers[i].Name, readers[i].Email, file, verified.SourceHash, book.SourceHash, DateTime.UtcNow.ToString("O") };
                map.AppendLine(string.Join(",", fields.Select(Recipients.CsvField)));
                progress?.Report(new(i + 1, readers.Count));
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, "PRIVATE-recipient-mapping.csv"), map.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(staging, "validation-report.json"), JsonSerializer.Serialize(new
            {
                Title = book.Title, MasterSha256 = book.SourceHash, RecipientCount = readers.Count,
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
}

