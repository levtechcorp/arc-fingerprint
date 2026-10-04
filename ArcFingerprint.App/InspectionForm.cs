using ArcFingerprint.Core;

namespace ArcFingerprint.App;

public sealed class InspectionForm : Form
{
    private readonly EpubBook book;
    private readonly string copyPath;
    private readonly string[] candidates;
    private readonly IReadOnlyList<string> ids;
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, AccessibleName = "Recipient lookup result" };
    private readonly Button choose = new() { Text = "Choose mapping CSV…", AutoSize = true };
    public string? MatchedMappingPath { get; private set; }

    public InspectionForm(EpubBook book, string copyPath, IEnumerable<string?> mappingCandidates)
    {
        this.book = book; this.copyPath = copyPath; ids = book.Fingerprints();
        candidates = mappingCandidates.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Text = "Inspect ARC copy — recipient lookup";
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
        buttons.Controls.AddRange(new Control[] { choose, close }); layout.Controls.Add(buttons);
        Controls.Add(layout); CancelButton = close;
        choose.Click += ChooseMapping;
        Shown += async (_, _) => await FindAutomatically();
    }

    private string CopyDetails => $"Book: {book.Title}\r\nInspected file: {copyPath}\r\nFingerprint: {string.Join(", ", ids)}";

    private async Task FindAutomatically()
    {
        if (ids.Count != 1)
        {
            choose.Enabled = false;
            details.Text = (ids.Count == 0 ? "No ARC metadata fingerprint found. It may have been removed."
                : "Multiple metadata fingerprints found. A single recipient cannot be identified reliably.") + "\r\n\r\n" + CopyDetails;
            return;
        }
        choose.Enabled = false;
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
        finally { if (!IsDisposed) choose.Enabled = true; }
    }

    private async void ChooseMapping(object? sender, EventArgs e)
    {
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

    private void ShowMatch(MappedRecipient match, string mappingPath)
    {
        MatchedMappingPath = mappingPath;
        string integrity = match.CopySha256.Length == 0 ? "No saved file hash in this mapping."
            : match.CopySha256.Equals(book.SourceHash, StringComparison.OrdinalIgnoreCase) ? "Exact file match: this EPUB matches the saved copy hash."
            : "Fingerprint matched, but this file differs from the original generated copy (its saved hash does not match).";
        details.Text = $"Assigned reader: {match.Name}\r\nEmail: {match.Email}\r\n\r\n{CopyDetails}\r\n\r\nOriginal filename: {match.FileName}\r\nCreated (UTC): {match.CreatedUtc}\r\n\r\n{integrity}\r\n\r\nMapping used: {mappingPath}";
    }
}

