using ArcFingerprint.Core;

namespace ArcFingerprint.App;

internal sealed class EmailDraftSettingsForm : Form
{
    private readonly CheckBox enabled = new() { Text = "Create one .eml draft file for each recipient", Checked = false, AutoSize = true };
    private readonly TextBox subject = new() { Dock = DockStyle.Fill, Text = "Your {edition} copy of {bookTitle}" };
    private readonly TextBox body = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = "I'm happy to share this copy with you. I hope you enjoy it!" };
    private readonly ComboBox greeting = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ListBox attachments = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    public EmailDraftOptions? ResultOptions { get; private set; }

    public EmailDraftSettingsForm(EmailDraftOptions? current)
    {
        Text = "Email draft files"; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 640); MinimumSize = new Size(560, 560); BackColor = Color.FromArgb(242, 246, 248);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 10 };
        layout.RowStyles.Add(new(SizeType.Absolute, 44)); layout.RowStyles.Add(new(SizeType.Absolute, 38));
        layout.RowStyles.Add(new(SizeType.Absolute, 30)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Absolute, 30)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Percent, 48)); layout.RowStyles.Add(new(SizeType.Absolute, 32));
        layout.RowStyles.Add(new(SizeType.Percent, 52)); layout.RowStyles.Add(new(SizeType.Absolute, 58));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Prepare individual email drafts", Font = new Font("Segoe UI Semibold", 16), Dock = DockStyle.Fill });
        layout.Controls.Add(enabled);
        layout.Controls.Add(new Label { Text = "Greeting", Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) });
        greeting.Items.AddRange(new object[] { "Hello", "Hi", "Good morning", "Good afternoon", "Good evening", "Howdy" });
        greeting.SelectedIndex = (int)(current?.Greeting ?? GreetingStyle.Hello); layout.Controls.Add(greeting);
        layout.Controls.Add(new Label { Text = "Subject · placeholders: {firstName}, {name}, {bookTitle}, {edition}", Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) });
        layout.Controls.Add(subject);
        layout.Controls.Add(body);
        layout.Controls.Add(new Label { Text = "Additional attachments (the reader's book copy is included automatically)", Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0) });
        layout.Controls.Add(attachments);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        bottom.ColumnStyles.Add(new(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new(SizeType.AutoSize));
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var add = new Button { Text = "Add files…", AutoSize = true }; var remove = new Button { Text = "Remove", AutoSize = true };
        left.Controls.AddRange(new Control[] { add, remove });
        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save settings", AutoSize = true, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(30, 111, 126), ForeColor = Color.White };
        var close = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        right.Controls.AddRange(new Control[] { save, close }); bottom.Controls.Add(left); bottom.Controls.Add(right, 1, 0); layout.Controls.Add(bottom);
        AcceptButton = save; CancelButton = close;
        add.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Choose files to attach to each draft", Multiselect = true, Filter = "All files (*.*)|*.*" };
            if (picker.ShowDialog(this) == DialogResult.OK)
                foreach (var path in picker.FileNames) if (!attachments.Items.Contains(path)) attachments.Items.Add(path);
        };
        remove.Click += (_, _) => { var selected = attachments.SelectedItem as string; if (selected is not null) attachments.Items.Remove(selected); };
        if (current is not null)
        {
            enabled.Checked = true; subject.Text = current.Subject; body.Text = current.Body;
            foreach (var item in current.Attachments) attachments.Items.Add(item);
        }
        save.Click += (_, _) =>
        {
            if (!enabled.Checked) { ResultOptions = null; return; }
            var option = new EmailDraftOptions(subject.Text, body.Text, (GreetingStyle)greeting.SelectedIndex, attachments.Items.Cast<string>().ToArray());
            try { option.Validate(); ResultOptions = option; }
            catch (Exception ex) { DialogResult = DialogResult.None; MessageBox.Show(this, ex.Message, "Email drafts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
}
