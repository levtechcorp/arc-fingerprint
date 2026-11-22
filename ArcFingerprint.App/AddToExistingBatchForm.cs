using ArcFingerprint.Core;

namespace ArcFingerprint.App;

internal sealed class AddToExistingBatchForm : Form
{
    private readonly TextBox mapping = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox master = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox name = new() { Dock = DockStyle.Fill };
    private readonly TextBox email = new() { Dock = DockStyle.Fill };
    private readonly ComboBox edition = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button add = new() { Text = "Add this reader", AutoSize = true };
    private readonly Label status = new() { Text = "Choose the private mapping and a clean master represented in the batch or consolidated collection.", AutoSize = true, ForeColor = Color.FromArgb(63, 77, 87) };
    private readonly EmailDraftOptions? emailDraft;

    public AddToExistingBatchForm(EmailDraftOptions? emailDraft)
    {
        this.emailDraft = emailDraft;
        Text = "Add a reader to an existing batch"; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(720, 360); MinimumSize = new Size(620, 360);
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(242, 246, 248);
        edition.Items.AddRange(new object[] { "ARC", "Beta", "Alpha" });
        edition.SelectedIndex = -1;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Absolute, 38));
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 44)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Add one reader to a saved batch or consolidated collection", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 16) });
        layout.Controls.Add(FileRow("Private mapping", mapping, "Choose CSV…", ChooseMapping));
        layout.Controls.Add(FileRow("Original clean master", master, "Choose book…", ChooseMaster));
        layout.Controls.Add(FieldRow("Reader name", name));
        layout.Controls.Add(FieldRow("Email address", email));
        layout.Controls.Add(FieldRow("Edition (if needed)", edition));
        layout.Controls.Add(status);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close); actions.Controls.Add(add); layout.Controls.Add(actions);
        CancelButton = close;
        add.Click += Add;
    }

    private Control FileRow(string caption, TextBox text, string buttonText, EventHandler handler)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new(SizeType.Absolute, 160)); row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.Absolute, 130));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); row.Controls.Add(text);
        var button = new Button { Text = buttonText, Dock = DockStyle.Fill }; button.Click += handler; row.Controls.Add(button);
        return row;
    }

    private Control FieldRow(string caption, Control text)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new(SizeType.Absolute, 160)); row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); row.Controls.Add(text); return row;
    }

    private void ChooseMapping(object? sender, EventArgs e)
    {
        using var picker = new OpenFileDialog { Filter = "Private batch mapping (PRIVATE-recipient-mapping.csv)|PRIVATE-recipient-mapping.csv|CSV files (*.csv)|*.csv", Title = "Select the existing batch's private mapping" };
        if (picker.ShowDialog(this) == DialogResult.OK) mapping.Text = picker.FileName;
    }

    private void ChooseMaster(object? sender, EventArgs e)
    {
        using var picker = new OpenFileDialog { Filter = "EPUB or PDF master (*.epub;*.pdf)|*.epub;*.pdf", Title = "Select the original, clean master file" };
        if (picker.ShowDialog(this) == DialogResult.OK) master.Text = picker.FileName;
    }

    private async void Add(object? sender, EventArgs e)
    {
        try
        {
            var reader = Recipients.Validate(new[] { new Recipient(name.Text, email.Text) }).Single();
            if (mapping.Text.Length == 0 || master.Text.Length == 0) throw new InvalidDataException("Choose both the batch mapping and the original clean master book.");
            add.Enabled = false; status.Text = "Checking the saved batch and preparing the new copy…"; UseWaitCursor = true;
            string selectedMapping = mapping.Text, selectedMaster = master.Text;
            string? selectedEdition = edition.SelectedItem?.ToString();
            bool Review(EmailDraftPreview item)
            {
                bool accepted = false;
                Invoke((Action)(() =>
                {
                    using var preview = new EmailDraftPreviewForm(new[] { item });
                    accepted = preview.ShowDialog(this) == DialogResult.OK;
                }));
                return accepted;
            }
            var result = await Task.Run(() => ExistingBatchAppender.Add(selectedMapping, selectedMaster, reader, emailDraft, Review, selectedEdition));
            status.Text = result.ReportUpdated
                ? $"Added {reader.Name}. The batch now has {result.RecipientCount} readers."
                : $"Added {reader.Name}. The batch has {result.RecipientCount} readers; its optional report could not be updated.";
            MessageBox.Show(this, $"Added {reader.Name} to the existing batch.\n\n{result.FileName}\n\nThe private mapping was updated in place.", "Reader added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK; Close();
        }
        catch (OperationCanceledException)
        {
            UseWaitCursor = false; add.Enabled = true; status.Text = "No changes were made; the email draft review was cancelled.";
        }
        catch (Exception ex)
        {
            UseWaitCursor = false; add.Enabled = true; status.Text = "The batch was not changed.";
            MessageBox.Show(this, ex.Message, "Could not add reader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }
}
