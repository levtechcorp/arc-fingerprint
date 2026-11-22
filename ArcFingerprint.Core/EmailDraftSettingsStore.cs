using System.Security.Cryptography;
using System.Text.Json;

namespace ArcFingerprint.Core;

/// <summary>Stores the user's email template and private attachment copies alongside a batch.</summary>
public static class EmailDraftSettingsStore
{
    public const string SettingsFileName = "PRIVATE-email-settings.json";
    private const string AssetsDirectoryName = ".private-email-assets";
    private sealed record SavedSettings(string Subject, string Body, GreetingStyle Greeting, string[] Attachments);

    public static EmailDraftOptions Save(string batchFolder, EmailDraftOptions options)
    {
        options.Validate();
        batchFolder = Path.GetFullPath(batchFolder);
        string assets = Path.Combine(batchFolder, AssetsDirectoryName);
        Directory.CreateDirectory(assets);
        var savedNames = new List<string>();
        foreach (string source in options.Attachments)
        {
            string basename = Path.GetFileName(source);
            string hash;
            using (var input = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(input))[..12];
            string safe = string.Join('_', basename.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string targetName = hash + "-" + safe;
            string target = Path.Combine(assets, targetName);
            if (!File.Exists(target)) File.Copy(source, target);
            savedNames.Add(targetName);
        }
        var data = new SavedSettings(options.Subject, options.Body, options.Greeting, savedNames.ToArray());
        string temp = Path.Combine(batchFolder, ".email-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        string path = Path.Combine(batchFolder, SettingsFileName);
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return options with { Attachments = savedNames.Select(n => Path.Combine(assets, n)).ToArray() };
    }

    public static EmailDraftOptions? Load(string batchFolder)
    {
        string path = Path.Combine(Path.GetFullPath(batchFolder), SettingsFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Saved email settings are empty.");
            var assets = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, AssetsDirectoryName));
            var paths = saved.Attachments.Select(name =>
            {
                if (name != Path.GetFileName(name)) throw new InvalidDataException("Saved email attachment path is invalid.");
                var full = Path.GetFullPath(Path.Combine(assets, name));
                if (!full.StartsWith(assets.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    throw new FileNotFoundException("A saved email attachment is missing. Open Email drafts to select it again.", full);
                return full;
            }).ToArray();
            var result = new EmailDraftOptions(saved.Subject, saved.Body, saved.Greeting, paths);
            result.Validate(); return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("Saved email settings could not be read.", ex); }
    }
}
