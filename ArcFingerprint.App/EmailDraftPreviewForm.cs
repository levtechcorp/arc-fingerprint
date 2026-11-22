using ArcFingerprint.Core;

namespace ArcFingerprint.App;

internal sealed class EmailDraftPreviewForm : Form
{
    private readonly IReadOnlyList<EmailDraftPreview> previews;
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly TextBox body = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label attachments = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label largeWarning = new() { Dock = DockStyle.Fill, ForeColor = Color.DarkRed, AutoSize = true };

    public EmailDraftPreviewForm(IReadOnlyList<EmailDraftPreview> previews)
    {
        this.previews = previews;
        Text = "Review individual email drafts"; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(980, 680); MinimumSize = new Size(780, 560); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 36));
        layout.RowStyles.Add(new(SizeType.Percent, 55)); layout.RowStyles.Add(new(SizeType.Absolute, 30));
        layout.RowStyles.Add(new(SizeType.Absolute, 34)); layout.RowStyles.Add(new(SizeType.Percent, 45));
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Review every recipient before the batch is finalized", Font = new Font("Segoe UI Semibold", 15), Dock = DockStyle.Fill });
        layout.Controls.Add(largeWarning);
        grid.Columns.Add("Recipient", "Recipient"); grid.Columns.Add("Email", "To"); grid.Columns.Add("Subject", "Subject");
        grid.Columns.Add("Book", "Attached book copy"); grid.Columns.Add("Size", "Draft size");
        foreach (var item in previews)
        {
            string size = $"{item.EstimatedMessageBytes / (1024d * 1024d):0.0} MB";
            grid.Rows.Add(item.RecipientName, item.RecipientEmail, item.Subject, item.BookFileName, size);
            if (item.EstimatedMessageBytes > 20L * 1024 * 1024)
                largeWarning.Text = "One or more drafts exceed 20 MB. Email attachment limits vary; check your provider before sending.";
        }
        grid.SelectionChanged += (_, _) => ShowSelected();
        layout.Controls.Add(grid);
        layout.Controls.Add(new Label { Text = "Message preview", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill });
        layout.Controls.Add(attachments);
        layout.Controls.Add(body);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Go back", AutoSize = true, DialogResult = DialogResult.Cancel };
        var approve = new Button { Text = "Finish batch", AutoSize = true, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(30, 111, 126), ForeColor = Color.White };
        actions.Controls.AddRange(new Control[] { cancel, approve }); layout.Controls.Add(actions);
        AcceptButton = approve; CancelButton = cancel;
        if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (grid.CurrentRow?.Index is not int index || index < 0 || index >= previews.Count) return;
        var item = previews[index];
        attachments.Text = "Attachments: " + string.Join(", ", new[] { item.BookFileName }.Concat(item.ExtraAttachmentNames));
        body.Text = $"To: {item.RecipientEmail}\r\nSubject: {item.Subject}\r\n\r\n{item.Body}";
    }
}
