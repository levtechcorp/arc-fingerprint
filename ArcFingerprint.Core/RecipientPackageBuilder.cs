namespace ArcFingerprint.Core;

/// <summary>Builds a reader-specific folder containing only the files intended for that reader.</summary>
public static class RecipientPackageBuilder
{
    public const string RootFolderName = "Send-to-readers";

    public static string FolderName(int sequence, string recipientName)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*"));
        string safe = new string(recipientName.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        safe = string.Join(' ', safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (safe.Length == 0) safe = "Reader";
        if (safe.Length > 100) safe = safe[..100].TrimEnd(' ', '.');
        string device = safe.Split('.')[0].TrimEnd();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }.Contains(device, StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
        return $"{sequence:000} - {safe}";
    }

    public static string Create(string batchFolder, int sequence, string recipientName, string copyPath, string? emlPath)
    {
        var readerFolder = Path.Combine(batchFolder, RootFolderName, FolderName(sequence, recipientName));
        if (Directory.Exists(readerFolder)) throw new IOException("A send folder already exists for this reader. Check the batch before adding files.");
        Directory.CreateDirectory(readerFolder);
        try
        {
            File.Copy(copyPath, Path.Combine(readerFolder, Path.GetFileName(copyPath)));
            if (emlPath is not null && File.Exists(emlPath)) File.Copy(emlPath, Path.Combine(readerFolder, Path.GetFileName(emlPath)));
            return readerFolder;
        }
        catch { try { Directory.Delete(readerFolder, true); } catch { } throw; }
    }
}
