using ArcFingerprint.Core;

namespace ArcFingerprint.App;

internal sealed class ManageExistingBatchForm : Form
{
    private readonly TextBox mapping = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox master = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox correctedName = new() { Dock = DockStyle.Fill };
    private readonly TextBox correctedEmail = new() { Dock = DockStyle.Fill };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Choose an existing batch mapping, then load it to check or manage readers." };
    private readonly Button load = new() { Text = "Load batch", AutoSize = true };
    private readonly Button check = new() { Text = "Check batch", AutoSize = true, Enabled = false };
    private readonly Button sendFolders = new() { Text = "Create missing send folders", AutoSize = true, Enabled = false };
    private readonly Button correct = new() { Text = "Save correction", AutoSize = true, Enabled = false };
    private readonly Button remove = new() { Text = "Undo / remove selected…", AutoSize = true, Enabled = false };
    private readonly EmailDraftOptions? emailDraft;
    private bool busy;

    public ManageExistingBatchForm(EmailDraftOptions? emailDraft)
    {
        this.emailDraft = emailDraft;
        Text = "Check and manage an existing batch"; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1000, 730); MinimumSize = new Size(820, 620); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(242, 246, 248);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 44)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Check copies and recipient matches, fix reader details, or safely undo an addition", Font = new Font("Segoe UI Semibold", 14), Dock = DockStyle.Fill });
        layout.Controls.Add(PathRow("Private mapping", mapping, "Choose CSV…", ChooseMapping));
        layout.Controls.Add(PathRow("Clean master (optional)", master, "Choose book…", ChooseMaster));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        tools.Controls.AddRange(new Control[] { load, check, sendFolders }); layout.Controls.Add(tools);
        grid.Columns.Add("Name", "Reader"); grid.Columns.Add("Email", "Email"); grid.Columns.Add("FileName", "Book copy"); grid.Columns.Add("ArcId", "Fingerprint ID");
        layout.Controls.Add(grid);
        var correction = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        correction.ColumnStyles.Add(new(SizeType.Absolute, 90)); correction.ColumnStyles.Add(new(SizeType.Percent, 50));
        correction.ColumnStyles.Add(new(SizeType.Absolute, 105)); correction.ColumnStyles.Add(new(SizeType.Percent, 50));
        correction.Controls.Add(new Label { Text = "Correct name", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); correction.Controls.Add(correctedName);
        correction.Controls.Add(new Label { Text = "Email", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); correction.Controls.Add(correctedEmail);
        layout.Controls.Add(correction);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close); actions.Controls.Add(remove); actions.Controls.Add(correct); layout.Controls.Add(actions);
        layout.Controls.Add(status); CancelButton = close;
        load.Click += async (_, _) => await LoadBatch();
        check.Click += async (_, _) => await CheckBatch();
        sendFolders.Click += async (_, _) => await CreateSendFolders();
        correct.Click += async (_, _) => await CorrectReader();
        remove.Click += async (_, _) => await RemoveReader();
        grid.SelectionChanged += (_, _) => ShowSelection();
    }

    private Control PathRow(string caption, TextBox text, string buttonText, EventHandler handler)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new(SizeType.Absolute, 165)); row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.Absolute, 130));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); row.Controls.Add(text);
        var button = new Button { Text = buttonText, Dock = DockStyle.Fill }; button.Click += handler; row.Controls.Add(button); return row;
    }

    private void ChooseMapping(object? sender, EventArgs e)
    {
        using var picker = new OpenFileDialog { Filter = "Private batch mapping (PRIVATE-recipient-mapping.csv)|PRIVATE-recipient-mapping.csv|CSV files (*.csv)|*.csv", Title = "Choose the private mapping" };
        if (picker.ShowDialog(this) == DialogResult.OK) { mapping.Text = picker.FileName; grid.Rows.Clear(); UpdateButtons(); }
    }

    private void ChooseMaster(object? sender, EventArgs e)
    {
        using var picker = new OpenFileDialog { Filter = "EPUB or PDF master (*.epub;*.pdf)|*.epub;*.pdf", Title = "Optionally choose the clean master to verify its hash" };
        if (picker.ShowDialog(this) == DialogResult.OK) master.Text = picker.FileName;
    }

    private async Task LoadBatch()
    {
        try
        {
            if (mapping.Text.Length == 0) throw new InvalidDataException("Choose a private mapping first.");
            SetBusy(true); var records = await Task.Run(() => BatchMaintenance.List(mapping.Text));
            grid.Rows.Clear();
            foreach (var record in records) grid.Rows.Add(record.Name, record.Email, record.FileName, record.ArcId);
            status.Text = $"Loaded {records.Count} readers. Select someone below to correct or remove them.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); UpdateButtons(); ShowSelection(); }
    }

    private async Task CheckBatch()
    {
        try
        {
            SetBusy(true); status.Text = "Checking mapped files, fingerprint IDs, drafts, and send folders…";
            var result = await Task.Run(() => BatchVerifier.Verify(mapping.Text, string.IsNullOrWhiteSpace(master.Text) ? null : master.Text));
            string masterLine = string.IsNullOrWhiteSpace(master.Text) ? "Master file: not checked." : result.MasterVerified ? "Master file: matches." : "Master file: mismatch or could not be read.";
            string detail = $"{result.ValidCopies} of {result.RecipientCount} copies passed their fingerprint and file checks.\n{masterLine}";
            if (result.Problems.Count > 0) detail += "\n\nNeeds attention:\n• " + string.Join("\n• ", result.Problems.Take(30));
            else detail += "\n\nEverything in this batch checks out.";
            status.Text = result.IsHealthy ? "Batch check passed." : $"Batch check found {result.Problems.Count} issue(s).";
            MessageBox.Show(this, detail, "Batch check", MessageBoxButtons.OK, result.IsHealthy ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async Task CreateSendFolders()
    {
        try
        {
            SetBusy(true); status.Text = "Checking the book copies and creating any missing reader folders…";
            int created = await Task.Run(() => BatchMaintenance.CreateMissingSendFolders(mapping.Text));
            status.Text = created == 0 ? "All reader send folders already exist." : $"Created {created} reader-specific send folder(s).";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async Task CorrectReader()
    {
        if (SelectedId() is not string id) return;
        try
        {
            var reader = Recipients.Validate(new[] { new Recipient(correctedName.Text, correctedEmail.Text) }).Single();
            SetBusy(true); status.Text = "Verifying the copy and updating the mapping and send folder…";
            bool Review(EmailDraftPreview item)
            {
                bool accepted = false;
                Invoke((Action)(() => { using var preview = new EmailDraftPreviewForm(new[] { item }); accepted = preview.ShowDialog(this) == DialogResult.OK; }));
                return accepted;
            }
            var result = await Task.Run(() => BatchMaintenance.Correct(mapping.Text, id, reader.Name, reader.Email, emailDraft, Review));
            status.Text = $"Corrected {reader.Name}. The copy keeps its fingerprint; email draft and send folder were refreshed.";
            await LoadBatch();
        }
        catch (OperationCanceledException) { status.Text = "Correction cancelled. No batch changes were made."; }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async Task RemoveReader()
    {
        if (SelectedId() is not string id) return;
        string person = Convert.ToString(grid.CurrentRow!.Cells["Name"].Value) ?? "this reader";
        if (MessageBox.Show(this, $"Remove {person} from the active batch? Their book, email, and send folder will be moved under ‘Removed readers’ so you can recover them. Anyone who already received a copy cannot be recalled.", "Undo reader addition", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            SetBusy(true); status.Text = "Moving the reader's files to the recovery folder and updating the private mapping…";
            var result = await Task.Run(() => BatchMaintenance.Remove(mapping.Text, id));
            status.Text = $"Removed {person}. Recovery files are in {result.ArchivedFolder}.";
            await LoadBatch();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private string? SelectedId() => grid.CurrentRow is null ? null : Convert.ToString(grid.CurrentRow.Cells["ArcId"].Value);

    private void ShowSelection()
    {
        if (grid.CurrentRow is null) { correctedName.Clear(); correctedEmail.Clear(); UpdateButtons(); return; }
        correctedName.Text = Convert.ToString(grid.CurrentRow.Cells["Name"].Value) ?? "";
        correctedEmail.Text = Convert.ToString(grid.CurrentRow.Cells["Email"].Value) ?? "";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        load.Enabled = !busy; check.Enabled = !busy && grid.Rows.Count > 0;
        sendFolders.Enabled = !busy && grid.Rows.Count > 0;
        correct.Enabled = !busy && grid.CurrentRow is not null; remove.Enabled = correct.Enabled;
    }

    private void SetBusy(bool value)
    {
        busy = value; mapping.Enabled = !value; master.Enabled = !value; grid.Enabled = !value;
        correctedName.Enabled = !value; correctedEmail.Enabled = !value; UpdateButtons(); UseWaitCursor = value;
    }

    private void ShowError(Exception ex)
    { status.Text = "The batch was not changed."; MessageBox.Show(this, ex.Message, "Batch tools", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
}
