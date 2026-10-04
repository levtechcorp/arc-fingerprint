using System.Diagnostics;
using ArcFingerprint.Core;

namespace ArcFingerprint.App;

public sealed class MainForm : Form
{
    private readonly TextBox master = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Master EPUB" };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Output folder" };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 32,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = "Recipients: name and email", MultiSelect = false
    };
    private readonly Label status = new() { AutoSize = true, Text = "Choose a master EPUB and add your readers.", Dock = DockStyle.Fill };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly Button generate = new() { Text = "Generate ARC copies", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button openFolder = new() { Text = "Open last batch", AutoSize = true, Enabled = false };
    private readonly List<Control> editable = new();
    private CancellationTokenSource? cancellation;
    private string? lastBatch;
    private string? lastMapping;

    public MainForm()
    {
        Text = "ARC Fingerprint";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(880, 720);
        MinimumSize = new Size(760, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 251);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 11 };
        layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 64));
        layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 28));
        layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.RowStyles.Add(new(SizeType.Absolute, 28));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Personal ARC copies, made locally", Font = new Font(Font.FontFamily, 19, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "One master EPUB. One private mapping. A unique hidden ID for each reader.\nYour book and recipient details stay on this computer; the app does not send email.", Dock = DockStyle.Fill });
        layout.Controls.Add(PathRow("Master EPUB", master, "Choose EPUB…", ChooseMaster));
        layout.Controls.Add(PathRow("Save batches in", output, "Choose folder…", ChooseOutput));
        var importRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var import = new Button { Text = "Import recipient CSV…", AutoSize = true };
        import.Click += Import;
        var inspect = new Button { Text = "Inspect a copy…", AutoSize = true };
        inspect.Click += Inspect;
        importRow.Controls.AddRange(new Control[] { import, inspect, new Label { Text = "CSV columns: Name, Email", AutoSize = true, Padding = new Padding(8) } });
        editable.AddRange(new Control[] { import, inspect, grid });
        layout.Controls.Add(importRow);
        grid.Columns.Add("Name", "Reader name");
        grid.Columns.Add("Email", "Email address");
        layout.Controls.Add(grid);
        layout.Controls.Add(new Label
        {
            Text = "Fingerprints are removable and may disappear during conversion. A matching ID identifies a copy, not proof of who shared it.\nOnly EPUBs go to readers. Keep PRIVATE-recipient-mapping.csv to yourself.",
            Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), ForeColor = Color.FromArgb(75, 85, 100)
        });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.AddRange(new Control[] { generate, cancel, openFolder });
        layout.Controls.Add(actions);
        layout.Controls.Add(progress);
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "Checks: EPUB structure + fingerprints. Full EPUBCheck validation is separate (see README).", AutoSize = true, ForeColor = Color.DimGray });
        generate.Click += Generate;
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "Cancelling after the current copy…"; };
        openFolder.Click += (_, _) => { if (lastBatch != null) Process.Start(new ProcessStartInfo(lastBatch) { UseShellExecute = true }); };
        FormClosing += (_, e) =>
        {
            if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling safely. Close the window when the batch stops."; }
        };
    }

    private Control PathRow(string caption, TextBox value, string action, EventHandler handler)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new(SizeType.Absolute, 125)); row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.Absolute, 150));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); row.Controls.Add(value);
        var button = new Button { Text = action, Dock = DockStyle.Top, AutoSize = true };
        button.Click += handler; row.Controls.Add(button); editable.Add(button);
        return row;
    }

    private async void ChooseMaster(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "EPUB books (*.epub)|*.epub", Title = "Choose the unmarked master EPUB" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true); status.Text = "Checking the master EPUB…";
        try
        {
            var book = await Task.Run(() => EpubBook.Load(dialog.FileName));
            master.Text = dialog.FileName;
            status.Text = $"Ready: {book.Title} · {book.DocumentCount} XHTML documents. Structural checks passed.";
            if (book.ObfuscatedFontCount > 0) status.Text += $" {book.ObfuscatedFontCount} obfuscated fonts will be preserved.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private void ChooseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose where to save ARC batch folders", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath;
    }

    private void Import(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Import readers (replaces the current list)" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var readers = Recipients.ReadCsv(dialog.FileName);
            grid.Rows.Clear();
            foreach (var r in readers) grid.Rows.Add(r.Name, r.Email);
            status.Text = $"Imported {readers.Count} readers. You can edit the list before generating.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Inspect(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "EPUB books (*.epub)|*.epub", Title = "Choose an ARC copy to identify its recipient" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            var book = await Task.Run(() => EpubBook.Load(dialog.FileName, true));
            using var inspection = new InspectionForm(book, dialog.FileName, new[]
            {
                Path.Combine(Path.GetDirectoryName(dialog.FileName)!, RecipientMapping.DefaultFileName),
                lastMapping,
                lastBatch == null ? null : Path.Combine(lastBatch, RecipientMapping.DefaultFileName)
            });
            UseWaitCursor = false;
            inspection.ShowDialog(this);
            if (inspection.MatchedMappingPath != null) lastMapping = inspection.MatchedMappingPath;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Generate(object? sender, EventArgs e)
    {
        try
        {
            grid.EndEdit();
            var readers = Recipients.Validate(grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => new Recipient(Convert.ToString(r.Cells[0].Value) ?? "", Convert.ToString(r.Cells[1].Value) ?? "")));
            if (master.Text.Length == 0 || output.Text.Length == 0) throw new InvalidDataException("Choose the master EPUB and an output folder first.");
            var masterPath = master.Text; var outputPath = output.Text;
            cancellation = new CancellationTokenSource();
            SetBusy(true); cancel.Enabled = true; progress.Value = 0;
            var activeCancellation = cancellation;
            var reporter = new Progress<BatchProgress>(p =>
            {
                // A queued progress callback must not overwrite the final result or a later batch.
                if (cancellation != activeCancellation || activeCancellation.IsCancellationRequested) return;
                progress.Value = p.Completed * 100 / p.Total;
                status.Text = $"Verified {p.Completed} of {p.Total} copies…";
            });
            var token = cancellation.Token;
            var result = await Task.Run(() => BatchGenerator.Generate(masterPath, readers, outputPath, reporter, token));
            lastBatch = result.Folder;
            status.Text = $"Created and verified {result.Count} copies. Open last batch to find the EPUBs and private mapping.";
        }
        catch (OperationCanceledException) { progress.Value = 0; status.Text = "Batch cancelled. No completed batch was published."; }
        catch (Exception ex) { ShowError(ex); }
        finally { cancellation?.Dispose(); cancellation = null; SetBusy(false); cancel.Enabled = false; }
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in editable) control.Enabled = !busy;
        generate.Enabled = !busy; openFolder.Enabled = !busy && lastBatch != null;
        UseWaitCursor = busy;
    }

    private void ShowError(Exception ex)
    {
        status.Text = "Could not complete the operation. See the message for details.";
        MessageBox.Show(this, ex.Message, "ARC Fingerprint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

