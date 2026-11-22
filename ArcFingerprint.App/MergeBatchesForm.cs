using System.Diagnostics;
using ArcFingerprint.Core;

namespace ArcFingerprint.App;

internal sealed class MergeBatchesForm : Form
{
    private readonly TextBox sourceRoot = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox outputRoot = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly ListBox batches = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Choose the folder that contains your separate project folders." };
    private readonly Button scan = new() { Text = "Choose projects folder…", AutoSize = true };
    private readonly Button merge = new() { Text = "Merge into new collection", AutoSize = true, Enabled = false };
    private readonly Button open = new() { Text = "Open merged collection", AutoSize = true, Enabled = false };
    private readonly Button chooseOutput = new() { Text = "Choose folder…", AutoSize = true };
    private string? mergedFolder;
    private int sourceCount;

    public MergeBatchesForm()
    {
        Text = "Merge ARC Fingerprint projects"; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(850, 600); MinimumSize = new Size(720, 500); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(242, 246, 248);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Absolute, 50));
        layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 30));
        layout.RowStyles.Add(new(SizeType.Absolute, 62)); layout.RowStyles.Add(new(SizeType.Absolute, 52)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "Combine project batches into one searchable collection", Font = new Font("Segoe UI Semibold", 16), Dock = DockStyle.Fill });
        layout.Controls.Add(PathRow("Projects folder", sourceRoot, scan));
        layout.Controls.Add(new Label { Text = "Discovered source batches (the originals will not be modified):", Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(batches);
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "The merge copies book files byte-for-byte, keeps every ARC ID, normalizes the private mapping, and creates current Send-to-readers folders. Existing email drafts are copied; per-batch email templates are not combined.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(62, 76, 87), Padding = new Padding(0, 6, 0, 0) });
        layout.Controls.Add(PathRow("Save collection in", outputRoot, chooseOutput));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close); actions.Controls.Add(open); actions.Controls.Add(merge); layout.Controls.Add(actions);
        Controls.Add(layout); CancelButton = close;
        scan.Click += (_, _) => ChooseProjects();
        chooseOutput.Click += (_, _) => ChooseOutput();
        merge.Click += async (_, _) => await Merge();
        open.Click += (_, _) => { if (mergedFolder is not null) Process.Start(new ProcessStartInfo(mergedFolder) { UseShellExecute = true }); };
    }

    private Control PathRow(string label, TextBox value, Control action)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new(SizeType.Absolute, 150)); row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.Absolute, 180));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        row.Controls.Add(value); row.Controls.Add(action); return row;
    }

    private void ChooseProjects()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose the parent folder that contains your separate ARC project batch folders", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var found = BatchMerger.Discover(picker.SelectedPath);
            sourceRoot.Text = picker.SelectedPath; outputRoot.Text = picker.SelectedPath;
            sourceCount = found.Count;
            batches.Items.Clear();
            foreach (var map in found) batches.Items.Add(Path.GetDirectoryName(map) ?? map);
            merge.Enabled = found.Count >= 2;
            open.Enabled = false; mergedFolder = null;
            status.Text = found.Count switch { 0 => "No project mappings were found here.", 1 => "Found one batch. Choose a folder containing at least two projects to merge.", _ => $"Found {found.Count} source batches. The output will be a new folder in the selected location." };
            batches.Items.Add("Status: " + status.Text);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ChooseOutput()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose where to create the new merged collection", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) == DialogResult.OK) outputRoot.Text = picker.SelectedPath;
    }

    private async Task Merge()
    {
        if (sourceRoot.Text.Length == 0) return;
        var answer = MessageBox.Show(this,
            "Create a new merged collection from every batch listed above? Source projects will remain unchanged. ARC IDs and ebook bytes will be preserved. Older mappings may not contain a verifiable original-master hash.",
            "Merge project batches", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try
        {
            scan.Enabled = chooseOutput.Enabled = merge.Enabled = open.Enabled = false; UseWaitCursor = true;
            status.Text = "Checking IDs, mappings, copy hashes, and ebook fingerprints, then building the merged collection…";
            var result = await Task.Run(() => BatchMerger.Merge(sourceRoot.Text, outputRoot.Text, changes =>
            {
                bool accepted = false;
                Invoke(() =>
                {
                    string details = string.Join("\r\n", changes.Take(12).Select(c => $"{c.FileName} ({c.SourceBatch})"));
                    if (changes.Count > 12) details += $"\r\n…and {changes.Count - 12} more.";
                    accepted = MessageBox.Show(this,
                        $"{changes.Count} book copy/copies differ from the hashes saved in their source mappings. Their embedded fingerprint IDs and EPUB/PDF structure still validate.\r\n\r\n" +
                        "If you continue, the merged collection will preserve the current files byte-for-byte and record the current hashes. This does not prove the book contents are unchanged from the copies originally sent. The original batches will remain untouched.\r\n\r\n" + details + "\r\n\r\nInclude these changed copies?",
                        "Changed source copies detected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                });
                return accepted;
            }));
            mergedFolder = result.Folder; open.Enabled = true;
            status.Text = $"Merged {result.RecipientCount} unique copies from {result.SourceBatchCount} projects. Skipped {result.DuplicateRowsSkipped} exact duplicate row(s); copied {result.EmailDraftsCopied} existing email draft(s).";
            string changes = result.HashChanges.Count == 0 ? "" : $"\r\n\r\nIncluded {result.HashChanges.Count} changed copy/copies; see validation-report.json for saved and current hashes.";
            MessageBox.Show(this, status.Text + "\r\n\r\nThe new collection is here:\r\n" + result.Folder + "\r\n\r\nAll original project folders remain unchanged." + changes, "Merge complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { scan.Enabled = chooseOutput.Enabled = true; merge.Enabled = sourceCount >= 2; open.Enabled = mergedFolder is not null; UseWaitCursor = false; }
    }

    private void ShowError(Exception ex)
    {
        status.Text = "Merge could not be completed. No source batches were changed.";
        MessageBox.Show(this, ex.Message, "ARC Fingerprint merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
