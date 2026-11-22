using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcFingerprint.Core;

internal static class PdfBatchGenerator
{
    public static BatchResult Generate(string master, IEnumerable<Recipient> recipients, string outputParent,
        IProgress<BatchProgress>? progress, CancellationToken cancellationToken, EditionKind editionKind, bool visibleMark, EmailDraftOptions? emailDraft,
        Func<IReadOnlyList<EmailDraftPreview>, bool>? previewEmailDrafts, bool pageLevelFingerprints, bool coverMark)
    {
        var readers = Recipients.Validate(recipients);
        emailDraft?.Validate();
        var book = PdfBook.Load(master);
        if (book.FingerprintId is not null) throw new InvalidDataException("This PDF already contains an ARC Fingerprint ID. Choose the clean, unmarked master PDF.");
        if (!Directory.Exists(outputParent)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var batchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = System.IO.Path.Combine(System.IO.Path.GetFullPath(outputParent), ".arc-incomplete-" + batchId);
        var editionLabel = editionKind switch { EditionKind.Beta => "Beta", EditionKind.Alpha => "Alpha", _ => "ARC" };
        var finished = System.IO.Path.Combine(System.IO.Path.GetFullPath(outputParent), editionLabel + "-" + batchId);
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
                string file = BatchGenerator.BuildFileName(book.Title, readers[i].Name, editionLabel, i + 1, id, staging, ".pdf");
                string copyPath = System.IO.Path.Combine(staging, file);
                book.WriteCopy(copyPath, id, editionLabel, visibleMark, pageLevelFingerprints, coverMark);
                var verified = PdfBook.Verify(copyPath, id, pageLevelFingerprints);
                if (batchEmailDraft is not null)
                {
                    var emlPath = System.IO.Path.ChangeExtension(copyPath, ".eml");
                    EmailDraftGenerator.Write(emlPath, readers[i], book.Title, editionLabel, copyPath, batchEmailDraft);
                    emailPreviews.Add(EmailDraftGenerator.Preview(readers[i], book.Title, editionLabel, copyPath, emlPath, batchEmailDraft));
                }
                RecipientPackageBuilder.Create(staging, i + 1, readers[i].Name, copyPath, batchEmailDraft is null ? null : System.IO.Path.ChangeExtension(copyPath, ".eml"));
                var fields = new[] { id, readers[i].Name, readers[i].Email, file, editionLabel, verified.SourceHash, book.SourceHash, DateTime.UtcNow.ToString("O"), "PDF" };
                map.AppendLine(string.Join(",", fields.Select(Recipients.CsvField)));
                progress?.Report(new(i + 1, readers.Count));
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(System.IO.Path.Combine(staging, RecipientMapping.DefaultFileName), map.ToString(), new UTF8Encoding(true));
            File.WriteAllText(System.IO.Path.Combine(staging, "validation-report.json"), JsonSerializer.Serialize(new
            {
                Title = book.Title, Format = "PDF", EditionType = editionLabel, MasterSha256 = book.SourceHash,
                MasterPageCount = book.PageCount, RecipientCount = readers.Count, VisiblePageMark = visibleMark,
                PageLevelFingerprint = pageLevelFingerprints, VisibleCoverMark = coverMark,
                CompletedUtc = DateTime.UtcNow,
                Validation = "PDF opened and each saved copy reopened; document metadata fingerprint and page count were checked. This is not a full PDF conformance or visual-layout validation.",
                Fingerprint = "ARC ID stored in PDF document information metadata" + (pageLevelFingerprints ? " and on each page for excerpt identification" : "") + (visibleMark ? "; optional visible footer on every page." : coverMark ? "; optional visible cover-page mark." : ".")
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (batchEmailDraft is not null && previewEmailDrafts is not null && !previewEmailDrafts(emailPreviews))
                throw new OperationCanceledException("Email draft review was cancelled.");
            Directory.Move(staging, finished);
            return new(finished, readers.Count);
        }
        catch
        {
            try { Directory.Delete(staging, true); } catch { /* Preserve the original failure if cleanup fails. */ }
            throw;
        }
    }
}
