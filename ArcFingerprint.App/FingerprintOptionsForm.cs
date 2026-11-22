namespace ArcFingerprint.App;

public sealed record FingerprintOptions(bool PdfFooter, bool PdfPageIds, bool PdfCoverMark, bool EpubStylesheet, bool EpubThesaurus, bool EpubRedundancy = true);

public sealed class FingerprintOptionsForm : Form
{
    private readonly CheckBox pdfFooter = new() { Text = "Visible mark on every PDF page" };
    private readonly CheckBox pdfPages = new() { Text = "Page-level PDF fingerprints (helps identify excerpts)" };
    private readonly CheckBox pdfCover = new() { Text = "Visible mark on the first PDF page" };
    private readonly CheckBox epubCss = new() { Text = "EPUB stylesheet signal with tiny spacing variation" };
    private readonly CheckBox epubWords = new() { Text = "Linguistic variations from the built-in thesaurus" };
    private readonly CheckBox epubRedundancy = new() { Text = "Repeat the ID in each XHTML document (recovery signal)", Checked = true };
    public FingerprintOptions Result { get; private set; }

    public FingerprintOptionsForm(FingerprintOptions current, bool isPdf, bool hasStylesheet)
    {
        Result = current;
        Text = "Optional fingerprint channels"; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(680, 470); MinimumSize = new Size(640, 430); StartPosition = FormStartPosition.CenterParent;
        pdfFooter.Checked = current.PdfFooter; pdfPages.Checked = current.PdfPageIds; pdfCover.Checked = current.PdfCoverMark;
        epubCss.Checked = current.EpubStylesheet; epubWords.Checked = current.EpubThesaurus; epubRedundancy.Checked = current.EpubRedundancy;
        pdfFooter.Enabled = pdfPages.Enabled = pdfCover.Enabled = isPdf;
        epubCss.Enabled = isStylesheetEnabled(isPdf, hasStylesheet);
        epubWords.Enabled = epubRedundancy.Enabled = !isPdf;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new(SizeType.Absolute, 35));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { Text = "Choose extra signals for this batch", Font = new Font(Font.FontFamily, 16, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(pdfFooter); layout.Controls.Add(pdfPages); layout.Controls.Add(pdfCover); layout.Controls.Add(epubCss); layout.Controls.Add(epubWords); layout.Controls.Add(epubRedundancy);
        layout.Controls.Add(new Label { Text = "The local thesaurus can change wording. It uses begin→start, began→started, end→finish, help→assist, show→reveal, use→employ, small→little, large→big, quickly→rapidly, quiet→silent, happy→glad, and angry→upset. The app does not analyze surrounding meaning; review copies before sending. Other signals may be stripped by conversions.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(65, 76, 83), Padding = new Padding(0, 6, 0, 0) });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save choices", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { save, cancel }); layout.Controls.Add(buttons);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        save.Click += (_, _) => Result = new(pdfFooter.Checked, pdfPages.Checked, pdfCover.Checked, epubCss.Checked, epubWords.Checked, epubRedundancy.Checked);
    }

    private static bool isStylesheetEnabled(bool isPdf, bool hasStylesheet) => !isPdf && hasStylesheet;
}
