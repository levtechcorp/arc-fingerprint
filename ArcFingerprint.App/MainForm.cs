using System.Diagnostics;
using ArcFingerprint.Core;

namespace ArcFingerprint.App;

public sealed class MainForm : Form
{
    private readonly TextBox master = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Master EPUB or PDF" };
    private readonly Button fingerprintOptionsButton = new() { Text = "Fingerprint options…", AutoSize = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Output folder" };
    private readonly ComboBox edition = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, AccessibleName = "Copy type" };
    private readonly Label filePreview = new() { AutoSize = true, ForeColor = Color.FromArgb(42, 95, 112) };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 32,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = "Recipients: name and email", MultiSelect = false
    };
    private readonly Label status = new() { AutoSize = false, Text = "Choose an EPUB or PDF master and add your readers.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly Button generate = new() { Text = "Generate copies", AutoSize = true, BackColor = Color.FromArgb(30, 111, 126), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button openFolder = new() { Text = "Open last batch", AutoSize = true, Enabled = false };
    private readonly Button emailSettings = new() { Text = "Email drafts: off…", AutoSize = true };
    private readonly Button manageBatch = new() { Text = "Manage existing batch…", AutoSize = true };
    private readonly Button mergeBatches = new() { Text = "Merge projects…", AutoSize = true };
    private readonly List<Control> editable = new();
    private CancellationTokenSource? cancellation;
    private string? lastBatch;
    private string? lastMapping;
    private string? selectedBookTitle;
    private EmailDraftOptions? emailDraftOptions;
    private FingerprintOptions fingerprintOptions = new(false, false, false, false, false);

    public MainForm()
    {
        Text = "ARC Fingerprint";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(980, 790);
        MinimumSize = new Size(830, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(242, 246, 248);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 20), ColumnCount = 1, RowCount = 14 };
        layout.RowStyles.Add(new(SizeType.Absolute, 62));
        layout.RowStyles.Add(new(SizeType.Absolute, 50));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 56));
        layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 58));
        layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.RowStyles.Add(new(SizeType.Absolute, 30));
        layout.RowStyles.Add(new(SizeType.Absolute, 50));
        layout.RowStyles.Add(new(SizeType.Absolute, 24));
        layout.RowStyles.Add(new(SizeType.Absolute, 30));
        layout.RowStyles.Add(new(SizeType.Absolute, 24));
        Controls.Add(layout);
        var hero = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 48, 64), Padding = new Padding(18, 8, 12, 4) };
        hero.Controls.Add(new Label { Text = "ARC FINGERPRINT", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 18), AutoSize = true, Location = new Point(16, 3) });
        hero.Controls.Add(new Label { Text = "Private, personalized EPUB and PDF copies", ForeColor = Color.FromArgb(193, 216, 224), Font = new Font("Segoe UI", 10), AutoSize = true, Location = new Point(19, 34) });
        layout.Controls.Add(hero);
        layout.Controls.Add(new Label { Text = "Prepare reader copies with a unique ID. Everything runs on this computer; the app never sends email.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(63, 77, 87), Padding = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(PathRow("Master file", master, "Choose file…", ChooseMaster));
        layout.Controls.Add(PathRow("Save batches in", output, "Choose folder…", ChooseOutput));
        var editionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        editionRow.Controls.Add(new Label { Text = "COPY STAGE", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(69, 84, 94), Padding = new Padding(0, 6, 12, 0) });
        edition.Items.AddRange(new object[] { "ARC · Advance review copy", "Beta · Beta reader copy", "Alpha · Early draft copy" }); edition.SelectedIndex = 0;
        editionRow.Controls.Add(edition); editionRow.Controls.Add(fingerprintOptionsButton);
        fingerprintOptionsButton.Enabled = false; editable.Add(fingerprintOptionsButton);
        layout.Controls.Add(editionRow); editable.Add(edition);
        var importRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var import = new Button { Text = "Import recipient CSV…", AutoSize = true };
        import.Click += Import;
        var inspect = new Button { Text = "Inspect a copy…", AutoSize = true };
        inspect.Click += Inspect;
        var addToBatch = new Button { Text = "Add to existing batch…", AutoSize = true };
        addToBatch.Click += AddToExistingBatch;
        importRow.Controls.AddRange(new Control[] { import, inspect, addToBatch, new Label { Text = "CSV columns: Name, Email", AutoSize = true, Padding = new Padding(8) } });
        editable.AddRange(new Control[] { import, inspect, addToBatch, grid, mergeBatches });
        layout.Controls.Add(importRow);
        grid.Columns.Add("Name", "Reader name");
        grid.Columns.Add("Email", "Email address");
        layout.Controls.Add(grid);
        layout.Controls.Add(new Label
        {
            Text = "FILENAME PREVIEW",
            Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(69, 84, 94)
        });
        layout.Controls.Add(filePreview);
        var privacyNote = new Label { Text = "Names appear in filenames for convenient emailing. Fingerprints can be removed; keep PRIVATE-recipient-mapping.csv private. A match identifies a copy, not proof of who shared it.", Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), ForeColor = Color.FromArgb(75, 85, 100) };
        layout.Controls.Add(privacyNote);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.AddRange(new Control[] { generate, cancel, openFolder, emailSettings, manageBatch, mergeBatches });
        layout.Controls.Add(actions);
        layout.Controls.Add(progress);
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "EPUB structure and fingerprints are checked. PDF copies are reopened and checked; marks can be removed.", AutoSize = true, ForeColor = Color.DimGray });
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 238, 241); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(34, 54, 66);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10); grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 10); grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(209, 233, 236);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(24, 48, 64); grid.RowTemplate.Height = 30; grid.BorderStyle = BorderStyle.None;
        generate.Click += Generate;
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "Cancelling after the current copy…"; };
        openFolder.Click += (_, _) => { if (lastBatch != null) Process.Start(new ProcessStartInfo(lastBatch) { UseShellExecute = true }); };
        emailSettings.Click += (_, _) =>
        {
            using var dialog = new EmailDraftSettingsForm(emailDraftOptions);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                emailDraftOptions = dialog.ResultOptions;
                emailSettings.Text = emailDraftOptions is null ? "Email drafts: off…" : "Email drafts: on…";
                status.Text = emailDraftOptions is null ? "Email draft files are off." : "Email drafts are on. One .eml file will be created for each reader copy.";
            }
        };
        manageBatch.Click += (_, _) =>
        {
            using var dialog = new ManageExistingBatchForm(emailDraftOptions);
            dialog.ShowDialog(this);
        };
        mergeBatches.Click += (_, _) =>
        {
            using var dialog = new MergeBatchesForm();
            dialog.ShowDialog(this);
        };
        fingerprintOptionsButton.Click += (_, _) =>
        {
            bool isPdf = System.IO.Path.GetExtension(master.Text).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            bool hasCss = false;
            if (!isPdf) { try { hasCss = EpubBook.Load(master.Text).StylesheetCount > 0; } catch { } }
            using var dialog = new FingerprintOptionsForm(fingerprintOptions, isPdf, hasCss);
            if (dialog.ShowDialog(this) == DialogResult.OK) fingerprintOptions = dialog.Result;
        };
        edition.SelectedIndexChanged += (_, _) => UpdatePreview();
        grid.CellValueChanged += (_, _) => UpdatePreview(); grid.RowsAdded += (_, _) => UpdatePreview(); grid.RowsRemoved += (_, _) => UpdatePreview();
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
        using var dialog = new OpenFileDialog { Filter = "Books (*.epub;*.pdf)|*.epub;*.pdf|EPUB books (*.epub)|*.epub|PDF documents (*.pdf)|*.pdf", Title = "Choose the unmarked master book" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true); status.Text = "Checking the selected master file…";
        try
        {
            bool isPdf = System.IO.Path.GetExtension(dialog.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            string title;
            string description;
            if (isPdf)
            {
                var book = await Task.Run(() => PdfBook.Load(dialog.FileName));
                title = book.Title;
                description = $"{book.PageCount} PDF pages. PDF checks passed.";
            }
            else
            {
                var book = await Task.Run(() => EpubBook.Load(dialog.FileName));
                title = book.Title;
                description = $"{book.DocumentCount} XHTML documents. EPUB checks passed.";
                if (book.ObfuscatedFontCount > 0) description += $" {book.ObfuscatedFontCount} obfuscated fonts will be preserved.";
            }
            master.Text = dialog.FileName;
            if (isPdf) fingerprintOptions = fingerprintOptions with { EpubStylesheet = false, EpubThesaurus = false };
            else fingerprintOptions = fingerprintOptions with { PdfFooter = false, PdfPageIds = false, PdfCoverMark = false };
            fingerprintOptionsButton.Enabled = true;
            selectedBookTitle = title;
            UpdatePreview(title);
            status.Text = $"Ready: {title} · {description}";
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
            UpdatePreview();
            status.Text = $"Imported {readers.Count} readers. You can edit the list before generating.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AddToExistingBatch(object? sender, EventArgs e)
    {
        using var dialog = new AddToExistingBatchForm(emailDraftOptions);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            status.Text = "Reader added to the existing batch. Open its folder to find the new copy and updated private mapping.";
    }

    private async void Inspect(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "EPUB and PDF copies (*.epub;*.pdf)|*.epub;*.pdf", Title = "Choose a copy to identify its recipient" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            bool isPdf = System.IO.Path.GetExtension(dialog.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            InspectionForm inspection;
            if (isPdf)
            {
                var book = await Task.Run(() => PdfBook.Load(dialog.FileName));
                inspection = new InspectionForm(book, dialog.FileName, new[]
                {
                    Path.Combine(Path.GetDirectoryName(dialog.FileName)!, RecipientMapping.DefaultFileName),
                    lastMapping, lastBatch == null ? null : Path.Combine(lastBatch, RecipientMapping.DefaultFileName)
                });
            }
            else
            {
                var book = await Task.Run(() => EpubBook.Load(dialog.FileName, true));
                inspection = new InspectionForm(book, dialog.FileName, new[]
                {
                    Path.Combine(Path.GetDirectoryName(dialog.FileName)!, RecipientMapping.DefaultFileName),
                    lastMapping, lastBatch == null ? null : Path.Combine(lastBatch, RecipientMapping.DefaultFileName)
                });
            }
            using (inspection)
            {
                UseWaitCursor = false;
                inspection.ShowDialog(this);
                if (inspection.MatchedMappingPath != null) lastMapping = inspection.MatchedMappingPath;
            }
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
            if (master.Text.Length == 0 || output.Text.Length == 0) throw new InvalidDataException("Choose a master EPUB or PDF and an output folder first.");
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
            var selectedEdition = (EditionKind)Math.Max(0, edition.SelectedIndex);
            var selectedFingerprintOptions = fingerprintOptions;
            var selectedEmailDraftOptions = emailDraftOptions;
            Func<IReadOnlyList<EmailDraftPreview>, bool>? previewDrafts = selectedEmailDraftOptions is null ? null : previews =>
            {
                bool accepted = false;
                Invoke((Action)(() =>
                {
                    using var preview = new EmailDraftPreviewForm(previews);
                    accepted = preview.ShowDialog(this) == DialogResult.OK;
                }));
                return accepted;
            };
            var result = await Task.Run(() => BatchGenerator.Generate(masterPath, readers, outputPath, reporter, token, selectedEdition, selectedFingerprintOptions.PdfFooter, selectedEmailDraftOptions, previewDrafts, selectedFingerprintOptions.PdfPageIds, selectedFingerprintOptions.EpubStylesheet, selectedFingerprintOptions.PdfCoverMark, selectedFingerprintOptions.EpubThesaurus, selectedFingerprintOptions.EpubRedundancy));
            lastBatch = result.Folder;
            status.Text = selectedEmailDraftOptions is null
                ? $"Created and checked {result.Count} copies. Open last batch to find the files and private mapping."
                : $"Created {result.Count} reviewed book copies and email drafts. Open the Send-to-readers folders for individual deliveries.";
        }
        catch (OperationCanceledException) { progress.Value = 0; status.Text = "Batch cancelled. No completed batch was published."; }
        catch (Exception ex) { ShowError(ex); }
        finally { cancellation?.Dispose(); cancellation = null; SetBusy(false); cancel.Enabled = false; }
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in editable) control.Enabled = !busy;
        fingerprintOptionsButton.Enabled = !busy && File.Exists(master.Text);
        generate.Enabled = !busy; openFolder.Enabled = !busy && lastBatch != null;
        UseWaitCursor = busy;
    }

    private void UpdatePreview(string? knownTitle = null)
    {
        var title = knownTitle ?? selectedBookTitle;
        if (title == null && File.Exists(master.Text))
        {
            try { title = Path.GetFileNameWithoutExtension(master.Text); } catch { }
        }
        var recipient = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && !string.IsNullOrWhiteSpace(Convert.ToString(r.Cells[0].Value)));
        var name = recipient == null ? "Recipient Name" : Convert.ToString(recipient.Cells[0].Value)!.Trim();
        var kind = edition.SelectedIndex switch { 1 => "Beta", 2 => "Alpha", _ => "ARC" };
        var extension = System.IO.Path.GetExtension(master.Text).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".epub";
        filePreview.Text = $"{title ?? "Book Title"} - {kind} - {name} - 001-XXXXXXXX{extension}";
    }

    private void ShowError(Exception ex)
    {
        status.Text = "Could not complete the operation. See the message for details.";
        MessageBox.Show(this, ex.Message, "ARC Fingerprint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
