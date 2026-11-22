using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArcFingerprint.Core;

public sealed record BatchCheckResult(int RecipientCount, int ValidCopies, IReadOnlyList<string> Problems, bool MasterVerified)
{
    public bool IsHealthy => Problems.Count == 0;
}

/// <summary>Checks that a batch mapping, its source, copies, drafts, and send folders agree.</summary>
public static class BatchVerifier
{
    public static BatchCheckResult Verify(string mappingPath, string? cleanMasterPath = null)
    {
        mappingPath = Path.GetFullPath(mappingPath);
        if (!Path.GetFileName(mappingPath).Equals(RecipientMapping.DefaultFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Choose the batch's {RecipientMapping.DefaultFileName} file.");
        var folder = Path.GetDirectoryName(mappingPath) ?? throw new InvalidDataException("The mapping folder could not be found.");
        var rows = ExistingBatchAppender.ReadRows(mappingPath);
        var problems = new List<string>();
        string masterHash = rows[0].MasterHash;
        bool masterVerified = false;
        bool mergedBatch = false;
        try
        {
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "validation-report.json")));
            mergedBatch = report.RootElement.TryGetProperty("MergedBatch", out var merged) && merged.ValueKind == JsonValueKind.True;
        }
        catch { }
        if (!string.IsNullOrWhiteSpace(cleanMasterPath))
        {
            try
            {
                string extension = Path.GetExtension(cleanMasterPath);
                string selectedFormat = extension.Equals(".epub", StringComparison.OrdinalIgnoreCase) ? "EPUB" : extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "";
                if (selectedFormat.Length == 0) throw new InvalidDataException("Choose an EPUB or PDF master.");
                string actual = selectedFormat == "EPUB" ? EpubBook.Load(cleanMasterPath).SourceHash : PdfBook.Load(cleanMasterPath).SourceHash;
                if (mergedBatch)
                {
                    masterVerified = rows.Any(r => r.Format == selectedFormat && r.MasterHash.Length > 0 && r.MasterHash.Equals(actual, StringComparison.OrdinalIgnoreCase));
                    if (!masterVerified) problems.Add("The selected clean master does not match any source project in this consolidated collection.");
                }
                else
                {
                    masterVerified = rows.All(r => r.Format == selectedFormat) && actual.Equals(masterHash, StringComparison.OrdinalIgnoreCase);
                    if (!masterVerified) problems.Add("The selected master file does not match this batch.");
                }
            }
            catch (Exception ex) { problems.Add("Master check: " + ex.Message); }
        }

        int valid = 0;
        var settingsExists = File.Exists(Path.Combine(folder, EmailDraftSettingsStore.SettingsFileName));
        EmailDraftOptions? savedSettings = null;
        if (settingsExists)
        {
            try { savedSettings = EmailDraftSettingsStore.Load(folder); }
            catch (Exception ex) { problems.Add("Saved email settings: " + ex.Message); }
        }
        string reportPath = Path.Combine(folder, "validation-report.json");
        if (File.Exists(reportPath))
        {
            try
            {
                using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath));
                if (report.RootElement.TryGetProperty("RecipientCount", out var count) && count.GetInt32() != rows.Count)
                    problems.Add("The batch report recipient count does not match the private mapping.");
            }
            catch (Exception ex) { problems.Add("Batch report: " + ex.Message); }
        }
        foreach (var row in rows)
        {
            var match = Regex.Match(row.FileName, " - (?<number>[0-9]{3,})-[0-9A-F]{8}\\.", RegexOptions.IgnoreCase);
            int sequence = match.Success ? int.Parse(match.Groups["number"].Value) : rows.IndexOf(row) + 1;
            var copyPath = Path.Combine(folder, row.FileName);
            try
            {
                if (!File.Exists(copyPath)) throw new FileNotFoundException("Copy is missing: " + row.FileName);
                if (!HashFile(copyPath).Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Copy hash differs from the private mapping: " + row.FileName);
                if (row.Format == "EPUB")
                {
                    var book = EpubBook.Load(copyPath, true);
                    if (!book.Fingerprints().Contains(row.Id, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Embedded ID does not match the mapping: " + row.FileName);
                }
                else if (PdfBook.Load(copyPath).FingerprintId != row.Id)
                    throw new InvalidDataException("Embedded ID does not match the mapping: " + row.FileName);
                valid++;
            }
            catch (Exception ex) { problems.Add(ex.Message); }

            string eml = Path.ChangeExtension(copyPath, ".eml");
            if (savedSettings is not null && !File.Exists(eml)) problems.Add("Email draft is missing for " + row.Name + ".");
            if (File.Exists(eml))
            {
                try
                {
                    var info = new FileInfo(eml);
                    string header = string.Join("\n", File.ReadLines(eml).Take(16));
                    if (!header.Contains("To: <" + row.Email + ">", StringComparison.OrdinalIgnoreCase)) problems.Add("Email draft recipient does not match the mapping for " + row.Name + ".");
                    if (info.Length > 20L * 1024 * 1024) problems.Add("Email draft for " + row.Name + " is over 20 MB; check the recipient's provider attachment limit.");
                }
                catch (Exception ex) { problems.Add("Email draft check for " + row.Name + ": " + ex.Message); }
            }

            string sendFolder = Path.Combine(folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(sequence, row.Name));
            if (!Directory.Exists(sendFolder)) problems.Add("Send-to-readers folder is missing for " + row.Name + ".");
            else
            {
                string packagedCopy = Path.Combine(sendFolder, row.FileName);
                if (!File.Exists(packagedCopy) || !HashFile(packagedCopy).Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase))
                    problems.Add("The send folder's book copy is missing or changed for " + row.Name + ".");
                if (File.Exists(eml))
                {
                    string packagedEml = Path.Combine(sendFolder, Path.GetFileName(eml));
                    if (!File.Exists(packagedEml) || !HashFile(packagedEml).Equals(HashFile(eml), StringComparison.OrdinalIgnoreCase))
                        problems.Add("The send folder's email draft is missing or changed for " + row.Name + ".");
                }
                var expectedCount = File.Exists(eml) ? 2 : 1;
                if (Directory.GetFiles(sendFolder).Length != expectedCount) problems.Add("The send folder has extra or unexpected files for " + row.Name + ".");
            }
        }
        return new(rows.Count, valid, problems, masterVerified);
    }

    private static string HashFile(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
