using System.Security.Cryptography;

namespace ArcFingerprint.Core;

public sealed record LeakMatch(string ArcId, MappedRecipient Recipient, string MappingPath, bool ExactFileMatch);
public sealed record LeakInspectionResult(string SearchFolder, int MappingsScanned,
    IReadOnlyList<LeakMatch> Matches, IReadOnlyList<string> Warnings);

/// <summary>Searches local batch mapping files for IDs embedded in a suspected EPUB or PDF.</summary>
public static class LeakInspector
{
    private const int MaximumMappings = 10_000;

    public static LeakInspectionResult Search(string suspectedCopyPath, IEnumerable<string> fingerprints, string searchFolder)
    {
        suspectedCopyPath = Path.GetFullPath(suspectedCopyPath);
        searchFolder = Path.GetFullPath(searchFolder);
        if (!File.Exists(suspectedCopyPath)) throw new FileNotFoundException("The suspected book file was not found.", suspectedCopyPath);
        if (!Directory.Exists(searchFolder)) throw new DirectoryNotFoundException("Choose a folder containing your ARC batch folders.");

        var ids = fingerprints.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var stream = File.OpenRead(suspectedCopyPath);
        string copyHash = Convert.ToHexString(SHA256.HashData(stream));
        var matches = new List<LeakMatch>();
        var warnings = new List<string>();
        int scanned = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        foreach (var mapping in Directory.EnumerateFiles(searchFolder, RecipientMapping.DefaultFileName, options))
        {
            if (++scanned > MaximumMappings)
                throw new InvalidDataException($"Search stopped after {MaximumMappings:N0} mapping files. Choose a narrower folder.");
            try
            {
                foreach (var (id, recipient) in RecipientMapping.FindMany(mapping, ids))
                    matches.Add(new(id, recipient, mapping, recipient.CopySha256.Length > 0 &&
                        recipient.CopySha256.Equals(copyHash, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { warnings.Add($"Could not check {mapping}: {ex.Message}"); }
        }
        return new(searchFolder, scanned, matches, warnings);
    }
}
