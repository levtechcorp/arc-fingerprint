using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace ArcFingerprint.Core;

public sealed record BatchRecipientInfo(string ArcId, string Name, string Email, string FileName);
public sealed record BatchMaintenanceResult(string Folder, string FileName, int RecipientCount, string? ArchivedFolder, bool ReportUpdated);

/// <summary>Safe corrections and recoverable removal for recipients already present in a batch.</summary>
public static class BatchMaintenance
{
    private sealed record Snapshot(string[] Header, List<string[]> Rows, int Id, int Name, int Email, int FileName);

    public static IReadOnlyList<BatchRecipientInfo> List(string mappingPath) => ExistingBatchAppender.ReadRows(mappingPath)
        .Select(r => new BatchRecipientInfo(r.Id, r.Name, r.Email, r.FileName)).ToArray();

    public static int CreateMissingSendFolders(string mappingPath)
    {
        mappingPath = Path.GetFullPath(mappingPath);
        var rows = ExistingBatchAppender.ReadRows(mappingPath);
        string folder = Path.GetDirectoryName(mappingPath)!;
        int created = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            string copy = Path.Combine(folder, row.FileName); VerifyCopy(row, copy);
            int sequence = ParseSequence(row.FileName, i + 1);
            string send = Path.Combine(folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(sequence, row.Name));
            if (Directory.Exists(send)) continue;
            string eml = Path.ChangeExtension(copy, ".eml");
            RecipientPackageBuilder.Create(folder, sequence, row.Name, copy, File.Exists(eml) ? eml : null);
            created++;
        }
        return created;
    }

    public static BatchMaintenanceResult Correct(string mappingPath, string arcId, string newName, string newEmail,
        EmailDraftOptions? emailDraft = null, Func<EmailDraftPreview, bool>? previewEmailDraft = null)
    {
        mappingPath = Path.GetFullPath(mappingPath);
        var snapshot = ReadSnapshot(mappingPath);
        var idIndex = snapshot.Rows.FindIndex(r => r[snapshot.Id].Equals(arcId, StringComparison.OrdinalIgnoreCase));
        if (idIndex < 0) throw new InvalidDataException("That reader is not in the selected batch.");
        var oldRow = snapshot.Rows[idIndex];
        var updatedReader = Recipients.Validate(new[] { new Recipient(newName, newEmail) }).Single();
        if (!updatedReader.Email.Equals(oldRow[snapshot.Email], StringComparison.OrdinalIgnoreCase) &&
            snapshot.Rows.Where((_, i) => i != idIndex).Any(r => r[snapshot.Email].Equals(updatedReader.Email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("That email address already appears on another copy in this collection.");
        var batchRows = ExistingBatchAppender.ReadRows(mappingPath);
        var target = batchRows.Single(r => r.Id.Equals(arcId, StringComparison.OrdinalIgnoreCase));
        var folder = Path.GetDirectoryName(mappingPath)!;
        string oldCopy = Path.Combine(folder, target.FileName);
        VerifyCopy(target, oldCopy);
        string oldEml = Path.ChangeExtension(oldCopy, ".eml");
        var persisted = EmailDraftSettingsStore.Load(folder);
        var activeEmail = persisted ?? emailDraft;
        if (activeEmail is not null) activeEmail.Validate();
        if (File.Exists(oldEml) && activeEmail is null)
            throw new InvalidDataException("This batch has an email draft but no saved template. Open Email drafts and save its template before correcting recipient details.");
        if (activeEmail is null && File.Exists(oldEml)) throw new InvalidDataException("Cannot safely update the old email draft without its saved template.");

        var sequence = ParseSequence(target.FileName, idIndex + 1);
        string extension = target.Format == "PDF" ? ".pdf" : ".epub";
        string title = target.Format == "PDF" ? PdfBook.Load(oldCopy).Title : EpubBook.Load(oldCopy, true).Title;
        string newFileName = BatchGenerator.BuildFileName(title, updatedReader.Name, target.Edition, sequence, target.Id, folder, extension);
        string newCopy = Path.Combine(folder, newFileName);
        string newEml = Path.ChangeExtension(newCopy, ".eml");
        if (!oldCopy.Equals(newCopy, StringComparison.OrdinalIgnoreCase) && File.Exists(newCopy)) throw new IOException("The corrected filename already exists in this batch.");

        oldRow[snapshot.Name] = updatedReader.Name;
        oldRow[snapshot.Email] = updatedReader.Email;
        oldRow[snapshot.FileName] = newFileName;
        string stagedRoot = Path.Combine(folder, ".arc-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagedRoot);
        string? oldSend = null;
        string? newSend = null;
        string? backupCopy = null;
        string? backupEml = null;
        string? backupSend = null;
        bool mapCommitted = false;
        try
        {
            if (persisted is null && emailDraft is not null) activeEmail = EmailDraftSettingsStore.Save(folder, emailDraft);
            string stagedCopy = Path.Combine(stagedRoot, newFileName); File.Copy(oldCopy, stagedCopy);
            string? stagedEml = null;
            if (activeEmail is not null)
            {
                stagedEml = Path.ChangeExtension(stagedCopy, ".eml");
                EmailDraftGenerator.Write(stagedEml, updatedReader, title, target.Edition, stagedCopy, activeEmail);
                if (previewEmailDraft is not null && !previewEmailDraft(EmailDraftGenerator.Preview(updatedReader, title, target.Edition, stagedCopy, stagedEml, activeEmail)))
                    throw new OperationCanceledException("Email draft review was cancelled.");
            }
            oldSend = Path.Combine(folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(sequence, target.Name));
            var sendRoot = Path.Combine(stagedRoot, RecipientPackageBuilder.RootFolderName);
            newSend = Path.Combine(folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(sequence, updatedReader.Name));
            RecipientPackageBuilder.Create(stagedRoot, sequence, updatedReader.Name, stagedCopy, stagedEml);
            string tempMap = Path.Combine(stagedRoot, RecipientMapping.DefaultFileName);
            File.WriteAllText(tempMap, Serialize(snapshot), new UTF8Encoding(true));

            backupCopy = Path.Combine(stagedRoot, "old-copy" + extension);
            File.Move(oldCopy, backupCopy);
            if (File.Exists(oldEml)) { backupEml = Path.Combine(stagedRoot, "old-draft.eml"); File.Move(oldEml, backupEml); }
            if (Directory.Exists(oldSend)) { backupSend = Path.Combine(stagedRoot, "old-send-folder"); Directory.Move(oldSend, backupSend); }
            File.Move(stagedCopy, newCopy);
            if (stagedEml is not null) File.Move(stagedEml, newEml);
            Directory.CreateDirectory(Path.GetDirectoryName(newSend)!);
            Directory.Move(Path.Combine(sendRoot, RecipientPackageBuilder.FolderName(sequence, updatedReader.Name)), newSend);
            File.Replace(tempMap, mappingPath, null); mapCommitted = true;
            bool reportUpdated = UpdateReport(folder, snapshot.Rows.Count);
            return new(folder, newFileName, snapshot.Rows.Count, null, reportUpdated);
        }
        catch
        {
            if (!mapCommitted)
            {
                if (Directory.Exists(newSend)) try { Directory.Delete(newSend, true); } catch { }
                if (File.Exists(newCopy)) try { File.Delete(newCopy); } catch { }
                if (File.Exists(newEml)) try { File.Delete(newEml); } catch { }
                if (backupCopy is not null && File.Exists(backupCopy)) try { File.Move(backupCopy, oldCopy); } catch { }
                if (backupEml is not null && File.Exists(backupEml)) try { File.Move(backupEml, oldEml); } catch { }
                if (backupSend is not null && Directory.Exists(backupSend) && oldSend is not null) try { Directory.Move(backupSend, oldSend); } catch { }
            }
            throw;
        }
        finally { try { Directory.Delete(stagedRoot, true); } catch { } }
    }

    public static BatchMaintenanceResult Remove(string mappingPath, string arcId)
    {
        mappingPath = Path.GetFullPath(mappingPath);
        var snapshot = ReadSnapshot(mappingPath);
        int index = snapshot.Rows.FindIndex(r => r[snapshot.Id].Equals(arcId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidDataException("That reader is not in the selected batch.");
        if (snapshot.Rows.Count == 1) throw new InvalidDataException("This is the last reader in the batch. Keep one active mapping entry; archive the whole batch if you no longer need it.");
        var batchRows = ExistingBatchAppender.ReadRows(mappingPath);
        var target = batchRows.Single(r => r.Id.Equals(arcId, StringComparison.OrdinalIgnoreCase));
        var folder = Path.GetDirectoryName(mappingPath)!;
        string copy = Path.Combine(folder, target.FileName); VerifyCopy(target, copy);
        string eml = Path.ChangeExtension(copy, ".eml");
        int sequence = ParseSequence(target.FileName, index + 1);
        string send = Path.Combine(folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(sequence, target.Name));
        string archiveRoot = Path.Combine(folder, "Removed readers");
        Directory.CreateDirectory(archiveRoot);
        string archive = Path.Combine(archiveRoot, RecipientPackageBuilder.FolderName(sequence, target.Name) + " - " + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(archive);
        var staging = Path.Combine(folder, ".arc-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        snapshot.Rows.RemoveAt(index);
        string tempMap = Path.Combine(staging, RecipientMapping.DefaultFileName);
        File.WriteAllText(tempMap, Serialize(snapshot), new UTF8Encoding(true));
        bool movedCopy = false, movedEml = false, movedSend = false, mapCommitted = false;
        string archiveCopy = Path.Combine(archive, Path.GetFileName(copy));
        string archiveEml = Path.Combine(archive, Path.GetFileName(eml));
        string archiveSend = Path.Combine(archive, "Send-folder");
        try
        {
            File.Move(copy, archiveCopy); movedCopy = true;
            if (File.Exists(eml)) { File.Move(eml, archiveEml); movedEml = true; }
            if (Directory.Exists(send)) { Directory.Move(send, archiveSend); movedSend = true; }
            File.Replace(tempMap, mappingPath, null); mapCommitted = true;
            bool reportUpdated = UpdateReport(folder, snapshot.Rows.Count);
            return new(folder, target.FileName, snapshot.Rows.Count, archive, reportUpdated);
        }
        catch
        {
            if (!mapCommitted)
            {
                if (movedSend && Directory.Exists(archiveSend)) try { Directory.Move(archiveSend, send); } catch { }
                if (movedEml && File.Exists(archiveEml)) try { File.Move(archiveEml, eml); } catch { }
                if (movedCopy && File.Exists(archiveCopy)) try { File.Move(archiveCopy, copy); } catch { }
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
            if (!mapCommitted) try { Directory.Delete(archive, true); } catch { }
        }
    }

    private static Snapshot ReadSnapshot(string path)
    {
        _ = ExistingBatchAppender.ReadRows(path);
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()!;
        int Col(string key) => Array.FindIndex(headers, h => h.Equals(key, StringComparison.OrdinalIgnoreCase));
        int id = Col("ArcId"), name = Col("Name"), email = Col("Email"), file = Col("FileName");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return new(headers, rows, id, name, email, file);
    }

    private static string Serialize(Snapshot snapshot) => string.Join(",", snapshot.Header.Select(Recipients.CsvField)) + "\r\n" +
        string.Join("\r\n", snapshot.Rows.Select(r => string.Join(",", r.Select(Recipients.CsvField)))) + "\r\n";

    private static int ParseSequence(string fileName, int fallback)
    {
        var m = Regex.Match(fileName, " - (?<number>[0-9]{3,})-[0-9A-F]{8}\\.", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups["number"].Value, out int number) ? number : fallback;
    }

    private static void VerifyCopy(BatchMapRow row, string copy)
    {
        if (!File.Exists(copy)) throw new FileNotFoundException("The mapped reader copy is missing.", copy);
        using var stream = File.OpenRead(copy);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(row.CopyHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The copy has changed since this batch was created. No changes were made.");
        if (row.Format == "EPUB" && !EpubBook.Load(copy, true).Fingerprints().Contains(row.Id, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The copy's embedded ID does not match the private mapping.");
        if (row.Format == "PDF" && PdfBook.Load(copy).FingerprintId != row.Id)
            throw new InvalidDataException("The copy's embedded ID does not match the private mapping.");
    }

    private static bool UpdateReport(string folder, int count)
    {
        string path = Path.Combine(folder, "validation-report.json");
        string? temp = null;
        try
        {
            if (!File.Exists(path)) return false;
            var values = JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var data = values.ToDictionary(k => k.Key, v => (object?)v.Value);
            data["RecipientCount"] = count; data["LastEditedUtc"] = DateTime.UtcNow;
            temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Replace(temp, path, null); temp = null; return true;
        }
        catch { return false; }
        finally { if (temp is not null) try { File.Delete(temp); } catch { } }
    }
}
