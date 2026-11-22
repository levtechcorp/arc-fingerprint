using ArcFingerprint.Core;

namespace ArcFingerprint.App;

public sealed class InspectionForm : Form
{
    private readonly string title;
    private readonly string format;
    private readonly string sourceHash;
    private readonly string copyPath;
    private readonly string[] candidates;
    private readonly IReadOnlyList<string> ids;
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, AccessibleName = "Recipient lookup result" };
    private readonly Button choose = new() { Text = "Choose mapping CSV…", AutoSize = true };
    private readonly Button searchFolder = new() { Text = "Search batch folder…", AutoSize = true };
    public string? MatchedMappingPath { get; private set; }

    public InspectionForm(EpubBook book, string copyPath, IEnumerable<string?> mappingCandidates)
    {
        title = book.Title; format = "EPUB"; sourceHash = book.SourceHash; this.copyPath = copyPath; ids = book.Fingerprints();
        candidates = mappingCandidates.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        InitializeWindow();
    }

    public InspectionForm(PdfBook book, string copyPath, IEnumerable<string?> mappingCandidates)
    {
        title = book.Title; format = "PDF"; sourceHash = book.SourceHash; this.copyPath = copyPath;
        ids = book.Fingerprints;
        candidates = mappingCandidates.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        Text = "Inspect copy — recipient lookup";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(760, 520); MinimumSize = new Size(620, 440);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Text = "Find the reader assigned this copy", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(details);
        layout.Controls.Add(new Label { Text = "The result comes from your private mapping. It identifies the assigned copy, not proof of who shared it.", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { choose, searchFolder, close }); layout.Controls.Add(buttons);
        Controls.Add(layout); CancelButton = close;
        choose.Click += ChooseMapping;
        searchFolder.Click += SearchBatchFolder;
        Shown += async (_, _) => await FindAutomatically();
    }

    private string CopyDetails => $"Book: {title}\r\nFormat: {format}\r\nInspected file: {copyPath}\r\nFingerprint: {string.Join(", ", ids)}";

    private async Task FindAutomatically()
    {
        if (ids.Count != 1)
        {
            choose.Enabled = false;
            searchFolder.Enabled = ids.Count > 0;
            details.Text = (ids.Count == 0 ? "No ARC metadata fingerprint found. It may have been removed."
                : "Multiple fingerprints found. Search your saved batch folders to see whether any map to readers.") + "\r\n\r\n" + CopyDetails;
            return;
        }
        choose.Enabled = false; searchFolder.Enabled = false;
        details.Text = "Looking for the recipient in available mapping files…\r\n\r\n" + CopyDetails;
        try
        {
            foreach (var path in candidates.Where(File.Exists))
            {
                var match = await Task.Run(() => RecipientMapping.Find(path, ids[0]));
                if (IsDisposed) return;
                if (match != null) { ShowMatch(match, path); return; }
            }
            details.Text = "No matching recipient found automatically. Choose the private mapping CSV from the batch this copy came from.\r\n\r\n" + CopyDetails;
        }
        catch (Exception ex) { if (!IsDisposed) details.Text = "Could not read an available mapping: " + ex.Message + "\r\n\r\nChoose another mapping CSV.\r\n\r\n" + CopyDetails; }
        finally { if (!IsDisposed) { choose.Enabled = true; searchFolder.Enabled = ids.Count > 0; } }
    }

    private async void ChooseMapping(object? sender, EventArgs e)
    {
        if (ids.Count != 1) return;
        using var picker = new OpenFileDialog { Filter = "CSV mapping files (*.csv)|*.csv", Title = "Choose the private recipient mapping for this ARC batch" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        choose.Enabled = false; MatchedMappingPath = null;
        details.Text = "Looking up the recipient…\r\n\r\n" + CopyDetails;
        try
        {
            var match = await Task.Run(() => RecipientMapping.Find(picker.FileName, ids[0]));
            if (IsDisposed) return;
            if (match == null) details.Text = "This fingerprint is not in the selected mapping. Choose a mapping from another batch.\r\n\r\n" + CopyDetails + "\r\n\r\nMapping checked: " + picker.FileName;
            else ShowMatch(match, picker.FileName);
        }
        catch (Exception ex) { if (!IsDisposed) details.Text = "Could not use this mapping: " + ex.Message + "\r\n\r\n" + CopyDetails; }
        finally { if (!IsDisposed) choose.Enabled = true; }
    }

    private async void SearchBatchFolder(object? sender, EventArgs e)
    {
        using var picker = new FolderBrowserDialog { Description = "Choose the parent folder containing your ARC Fingerprint batches", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        choose.Enabled = searchFolder.Enabled = false;
        MatchedMappingPath = null;
        details.Text = "Searching local batch mappings…\r\n\r\n" + CopyDetails;
        try
        {
            var result = await Task.Run(() => LeakInspector.Search(copyPath, ids, picker.SelectedPath));
            if (IsDisposed) return;
            if (result.Matches.Count == 0)
            {
                details.Text = $"No reader match found in {result.MappingsScanned:N0} mapping file(s). The fingerprint may have been removed, or the original batch folder may not be included.\r\n\r\n" + CopyDetails;
            }
            else
            {
                MatchedMappingPath = result.Matches[0].MappingPath;
                var report = result.Matches.Select(match =>
                    $"Assigned reader: {match.Recipient.Name}\r\nEmail: {match.Recipient.Email}\r\nFingerprint: {match.ArcId}\r\nOriginal filename: {match.Recipient.FileName}\r\nEvidence: {(match.ExactFileMatch ? "Exact file hash match." : "Fingerprint matched, but file hash differs from the saved copy.") }\r\nMapping: {match.MappingPath}");
                string ambiguity = result.Matches.Count > 1 ? "\r\n\r\nMore than one mapping matched. Treat this as ambiguous and check the listed batch folders." : "";
                details.Text = string.Join("\r\n\r\n------------------------------\r\n\r\n", report) + ambiguity + "\r\n\r\n" + CopyDetails;
            }
            if (result.Warnings.Count > 0)
                details.AppendText("\r\n\r\nSome mappings could not be checked:\r\n" + string.Join("\r\n", result.Warnings.Take(10)));
        }
        catch (Exception ex) { if (!IsDisposed) details.Text = "Could not search batch mappings: " + ex.Message + "\r\n\r\n" + CopyDetails; }
        finally { if (!IsDisposed) { choose.Enabled = ids.Count == 1; searchFolder.Enabled = ids.Count > 0; } }
    }

    private void ShowMatch(MappedRecipient match, string mappingPath)
    {
        MatchedMappingPath = mappingPath;
        string integrity = match.CopySha256.Length == 0 ? "No saved file hash in this mapping."
            : match.CopySha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase) ? $"Exact file match: this {format} matches the saved copy hash."
            : "Fingerprint matched, but this file differs from the original generated copy (its saved hash does not match).";
        details.Text = $"Assigned reader: {match.Name}\r\nEmail: {match.Email}\r\n\r\n{CopyDetails}\r\n\r\nOriginal filename: {match.FileName}\r\nCreated (UTC): {match.CreatedUtc}\r\n\r\n{integrity}\r\n\r\nMapping used: {mappingPath}";
    }
}
