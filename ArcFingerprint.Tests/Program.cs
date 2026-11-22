using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ArcFingerprint.Core;
using PdfSharp.Pdf;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "ArcFingerprint-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int passed = 0;
void Test(string title, Action action)
{
    try { action(); Console.WriteLine("PASS " + title); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + title + "\n" + ex); Environment.Exit(1); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or OperationCanceledException) { return; }
    throw new Exception("Expected rejection");
}
byte[] ReadEntry(string file, string name)
{
    using var zip = ZipFile.OpenRead(file); using var stream = zip.GetEntry(name)!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
}
string Fixture(string name, string version = "3.0", Action<Dictionary<string, byte[]>>? edit = null, bool utf16 = false)
{
    var xhtml = "<?xml version=\"1.0\" encoding=\"" + (utf16 ? "utf-16" : "utf-8") + "\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapter</title></head><body><p>Words &amp; punctuation — unchanged.</p></body></html>";
    var opf = $"""
    <?xml version="1.0" encoding="utf-8"?>
    <package xmlns="http://www.idpf.org/2007/opf" version="{version}" unique-identifier="book-id">
      <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="book-id">urn:uuid:original-book</dc:identifier><dc:title>Test Book</dc:title><dc:language>en</dc:language></metadata>
      <manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/><item id="image" href="image.png" media-type="image/png"/></manifest>
      <spine toc="ncx"><itemref idref="chapter"/></spine>
    </package>
    """;
    var files = new Dictionary<string, byte[]>
    {
        ["mimetype"] = Encoding.ASCII.GetBytes("application/epub+zip"),
        ["META-INF/container.xml"] = Encoding.UTF8.GetBytes("<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" version=\"1.0\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>"),
        ["OEBPS/content.opf"] = Encoding.UTF8.GetBytes(opf),
        ["OEBPS/chapter.xhtml"] = utf16 ? Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xhtml)).ToArray() : Encoding.UTF8.GetBytes(xhtml),
        ["OEBPS/nav.xhtml"] = Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Contents</title></head><body><nav epub:type=\"toc\"><ol><li><a href=\"chapter.xhtml\">Chapter</a></li></ol></nav></body></html>"),
        ["OEBPS/toc.ncx"] = Encoding.UTF8.GetBytes("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"urn:uuid:original-book\"/></head><docTitle><text>Test Book</text></docTitle><navMap><navPoint id=\"n1\" playOrder=\"1\"><navLabel><text>Chapter</text></navLabel><content src=\"chapter.xhtml\"/></navPoint></navMap></ncx>"),
        ["OEBPS/image.png"] = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")
    };
    edit?.Invoke(files);
    var path = Path.Combine(root, name + ".epub");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (key, value) in files)
    { using var stream = zip.CreateEntry(key, key == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open(); stream.Write(value); }
    return path;
}
string PdfFixture(string name)
{
    var path = Path.Combine(root, name + ".pdf");
    using var document = new PdfDocument();
    document.Info.Title = "PDF Sample Book";
    document.AddPage();
    document.AddPage();
    document.Save(path);
    return path;
}
var master = Fixture("master");
var readers = Enumerable.Range(1, 15).Select(i => new Recipient("Reader " + i, $"reader{i}@example.com")).ToList();
string batch = "";
Test("EPUB 3 master structure", () => Assert(EpubBook.Load(master).DocumentCount == 2));
Test("PDF batch preserves the master and maps metadata fingerprints", () =>
{
    var pdf = PdfFixture("pdf-master");
    var before = SHA256.HashData(File.ReadAllBytes(pdf));
    var pdfReaders = new[] { new Recipient("PDF Reader", "pdfreader@example.com"), new Recipient("PDF Reader Two", "pdfreader2@example.com") };
    var generated = BatchGenerator.Generate(pdf, pdfReaders, root);
    var files = Directory.GetFiles(generated.Folder, "*.pdf");
    Assert(generated.Count == 2 && files.Length == 2, "PDF outputs were not generated.");
    Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pdf))), "The PDF master changed.");
    var mapping = Path.Combine(generated.Folder, RecipientMapping.DefaultFileName);
    foreach (var file in files)
    {
        var copy = PdfBook.Load(file);
        Assert(copy.PageCount == 2 && copy.Title == "PDF Sample Book", "PDF structure or title was not preserved.");
        var fingerprint = copy.FingerprintId ?? throw new Exception("PDF metadata fingerprint was missing.");
        var reader = RecipientMapping.Find(mapping, fingerprint);
        Assert(reader is not null && reader.CopySha256 == copy.SourceHash && Path.GetExtension(reader.FileName) == ".pdf", "PDF fingerprint did not resolve through the private mapping.");
    }
    var emailExtra = Path.Combine(root, "pdf-email-extra.txt"); File.WriteAllText(emailExtra, "extra");
    var emailBatch = BatchGenerator.Generate(pdf, pdfReaders.Take(1), root,
        emailDraft: new EmailDraftOptions("PDF for {firstName}", "Here you go.", GreetingStyle.Hello, new[] { emailExtra }));
    var email = File.ReadAllText(Directory.GetFiles(emailBatch.Folder, "*.eml").Single());
    Assert(email.Contains("To: <pdfreader@example.com>") && email.Contains("application/octet-stream") && email.Contains("filename*=utf-8''PDF%20Sample%20Book"), "PDF email draft or its book attachment was missing.");
});
Test("Optional visible PDF mark preserves pages and fingerprint", () =>
{
    var pdf = PdfFixture("pdf-watermark-master");
    var generated = BatchGenerator.Generate(pdf, new[] { new Recipient("Marked Reader", "marked@example.com") }, root,
        editionKind: EditionKind.Beta, visiblePdfMark: true);
    var copy = PdfBook.Load(Directory.GetFiles(generated.Folder, "*.pdf").Single());
    Assert(copy.PageCount == 2 && copy.FingerprintId is not null, "Visible-mark PDF failed verification.");
    Assert(copy.FingerprintId!.StartsWith("ARC-", StringComparison.Ordinal), "Unexpected PDF fingerprint format.");
});
Test("Optional per-page PDF fingerprints survive copy inspection", () =>
{
    var pdf = PdfFixture("pdf-page-fingerprint-master");
    var generated = BatchGenerator.Generate(pdf, new[] { new Recipient("Page Reader", "page@example.com") }, root, pageLevelPdfFingerprints: true);
    var copy = PdfBook.Load(Directory.GetFiles(generated.Folder, "*.pdf").Single());
    Assert(copy.PagesWithFingerprint == copy.PageCount && copy.Fingerprints.Contains(copy.FingerprintId!, StringComparer.OrdinalIgnoreCase), "Per-page PDF fingerprint was not present on every page.");
});
Test("Optional PDF cover watermark preserves the document", () =>
{
    var pdf = PdfFixture("pdf-cover-watermark-master");
    var generated = BatchGenerator.Generate(pdf, new[] { new Recipient("Cover Reader", "cover@example.com") }, root, pdfCoverMark: true);
    var copy = PdfBook.Load(Directory.GetFiles(generated.Folder, "*.pdf").Single());
    Assert(copy.PageCount == 2 && copy.FingerprintId is not null, "Cover-watermarked PDF did not preserve its pages and fingerprint.");
});
Test("Optional EPUB stylesheet signal adds a recoverable secondary ID", () =>
{
    var epub = Fixture("css-signal-master", edit: files =>
    {
        var opf = Encoding.UTF8.GetString(files["OEBPS/content.opf"]).Replace("</manifest>", "<item id=\"style\" href=\"style.css\" media-type=\"text/css\"/></manifest>");
        files["OEBPS/content.opf"] = Encoding.UTF8.GetBytes(opf);
        files["OEBPS/style.css"] = Encoding.UTF8.GetBytes("body { font-family: serif; }\n");
    });
    var generated = BatchGenerator.Generate(epub, new[] { new Recipient("Style Reader", "style@example.com") }, root, epubStylesheetFingerprint: true);
    var copy = EpubBook.Load(Directory.GetFiles(generated.Folder, "*.epub").Single(), true);
    Assert(copy.Fingerprints().Count == 1 && copy.StylesheetCount == 1, "Stylesheet fingerprint did not add a detectable alternate channel.");
});
Test("Optional local thesaurus variation changes text nodes and remains identifiable", () =>
{
    var epub = Fixture("thesaurus-master", edit: files =>
    {
        files["OEBPS/chapter.xhtml"] = Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>They began to use a small room and quickly began.</p></body></html>");
    });
    var generated = BatchGenerator.Generate(epub, new[] { new Recipient("Words Reader", "words@example.com") }, root, epubThesaurusVariants: true);
    var copyPath = Directory.GetFiles(generated.Folder, "*.epub").Single();
    var copy = EpubBook.Load(copyPath, true);
    Assert(copy.Fingerprints().Count == 1 && copy.StylesheetCount == 0, "Linguistic variation lost the core fingerprint.");
    using var zip = ZipFile.OpenRead(copyPath);
    using var chapter = new StreamReader(zip.GetEntry("OEBPS/chapter.xhtml")!.Open());
    string changed = chapter.ReadToEnd();
    Assert(changed.Contains("started", StringComparison.OrdinalIgnoreCase) || changed.Contains("employ", StringComparison.OrdinalIgnoreCase) || changed.Contains("little", StringComparison.OrdinalIgnoreCase), "The selected thesaurus channel did not alter any supported word.");
});
Test("Email draft files are personalized and attach the matching copy and extras", () =>
{
    var extra = Path.Combine(root, "email-extra.txt");
    File.WriteAllText(extra, "shared attachment");
    var options = new EmailDraftOptions("Your {edition} copy of {bookTitle}", "For {firstName} ({name})", GreetingStyle.Howdy, new[] { extra });
    var drafted = BatchGenerator.Generate(master, new[] { new Recipient("Casey Reader", "casey@example.com") }, root, emailDraft: options);
    var eml = File.ReadAllText(Directory.GetFiles(drafted.Folder, "*.eml").Single());
    Assert(eml.Contains("To: <casey@example.com>") && eml.Contains("X-Unsent: 1"), "Draft header or recipient was missing.");
    Assert(eml.Contains("Subject: =?UTF-8?B?") && eml.Contains("application/octet-stream"), "Subject encoding or MIME attachments were missing.");
    var decodedBodies = eml.Split("\r\n\r\n", StringSplitOptions.None)
        .Where(part => !part.StartsWith("This is", StringComparison.Ordinal))
        .SelectMany(part => part.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        .Where(line => line.Length > 0 && line.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))
        .ToArray();
    Assert(decodedBodies.Any(line => line == Convert.ToBase64String(Encoding.UTF8.GetBytes("Howdy Casey,\r\n\r\nFor Casey (Casey Reader)"))), "Greeting or personalization was missing.");
    var copy = Directory.GetFiles(drafted.Folder, "*.epub").Single();
    var encodedCopy = Convert.ToBase64String(File.ReadAllBytes(copy));
    var flattened = string.Concat(eml.Split("\r\n", StringSplitOptions.None));
    Assert(flattened.Contains(encodedCopy, StringComparison.Ordinal), "The reader's unique book copy was not attached.");
    var saved = EmailDraftSettingsStore.Load(drafted.Folder);
    Assert(saved is not null && saved.Attachments.Count == 1 && File.Exists(saved.Attachments[0]) && Path.GetFullPath(saved.Attachments[0]).StartsWith(Path.GetFullPath(drafted.Folder), StringComparison.OrdinalIgnoreCase), "Email settings and shared attachments were not saved privately with the batch.");
    var sendFolder = Path.Combine(drafted.Folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(1, "Casey Reader"));
    Assert(Directory.GetFiles(sendFolder).Length == 2 && File.Exists(Path.Combine(sendFolder, Path.GetFileName(copy))) && File.Exists(Path.Combine(sendFolder, Path.GetFileName(Directory.GetFiles(drafted.Folder, "*.eml").Single()))), "Reader-specific send folder did not contain exactly the copy and draft.");
    var health = BatchVerifier.Verify(Path.Combine(drafted.Folder, RecipientMapping.DefaultFileName), master);
    Assert(health.IsHealthy && health.ValidCopies == 1 && health.MasterVerified, "A newly generated batch did not pass the complete check.");
});
Test("Email subject header injection is rejected", () =>
{
    var options = new EmailDraftOptions("Safe\r\nBcc: other@example.com", "Body", GreetingStyle.Hello, Array.Empty<string>());
    try { options.Validate(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected email header injection to be rejected.");
});
Test("Digitally signed PDFs are rejected before modification", () =>
{
    var signed = PdfFixture("pdf-signed-marker");
    File.AppendAllText(signed, "\r\n/ByteRange [0 1 2 3]");
    Reject(() => PdfBook.Load(signed));
});
Test("15 unique copies, private mapping, original preserved", () =>
{
    var before = SHA256.HashData(File.ReadAllBytes(master));
    batch = BatchGenerator.Generate(master, readers, root).Folder;
    var copies = Directory.GetFiles(batch, "*.epub"); Assert(copies.Length == 15);
    var ids = copies.SelectMany(p => EpubBook.Load(p, true).Fingerprints()).ToList(); Assert(ids.Count == 15 && ids.Distinct().Count() == 15);
    Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(master))));
    var mapping = Recipients.ReadCsv(Path.Combine(batch, "PRIVATE-recipient-mapping.csv")); Assert(mapping.SequenceEqual(readers));
    Assert(File.Exists(Path.Combine(batch, "validation-report.json")));
});
Test("XHTML original bytes and binary assets preserved; no recipient PII", () =>
{
    foreach (var copy in Directory.GetFiles(batch, "*.epub"))
    {
        Assert(ReadEntry(copy, "OEBPS/chapter.xhtml").AsSpan().StartsWith(ReadEntry(master, "OEBPS/chapter.xhtml")));
        Assert(ReadEntry(copy, "OEBPS/image.png").SequenceEqual(ReadEntry(master, "OEBPS/image.png")));
        using var zip = ZipFile.OpenRead(copy);
        foreach (var e in zip.Entries) Assert(!Encoding.UTF8.GetString(ReadEntry(copy, e.FullName)).Contains("@example.com"));
        var doc = XDocument.Parse(Encoding.UTF8.GetString(ReadEntry(copy, "OEBPS/content.opf")));
        Assert(doc.Descendants().Single(e => (string?)e.Attribute("id") == "book-id").Value == "urn:uuid:original-book");
    }
});
Test("Repeated batches never overwrite", () => Assert(BatchGenerator.Generate(master, readers.Take(1), root).Folder != batch));
Test("Already marked master rejected", () => Reject(() => EpubBook.Load(Directory.GetFiles(batch, "*.epub")[0])));
Test("EPUB 2 works and NCX remains byte-identical", () =>
{
    var v2 = Fixture("epub2", "2.0");
    var folder = BatchGenerator.Generate(v2, readers.Take(1), root).Folder;
    Assert(ReadEntry(Directory.GetFiles(folder, "*.epub")[0], "OEBPS/toc.ncx").SequenceEqual(ReadEntry(v2, "OEBPS/toc.ncx")));
});
Test("UTF-16 XHTML survives generation", () => BatchGenerator.Generate(Fixture("utf16", utf16: true), readers.Take(1), root));
Test("Quoted CSV and Unicode names", () =>
{
    var csv = Path.Combine(root, "recipients.csv"); File.WriteAllText(csv, "Name,Email\r\n\"Döe, Jane\",jane@example.com\r\n");
    Assert(Recipients.ReadCsv(csv).Single().Name == "Döe, Jane");
});
Test("Spreadsheet formula neutralization", () => Assert(Recipients.CsvField("=CMD()") == "\"'=CMD()\"" && Recipients.CsvField("+name") == "\"'+name\""));
Test("Duplicate recipients rejected case-insensitively", () => Reject(() => Recipients.Validate(new[] { new Recipient("A", "a@example.com"), new Recipient("B", "A@example.com") })));
Test("Invalid email rejected", () => Reject(() => Recipients.Validate(new[] { new Recipient("A", "broken") })));
Test("Missing XHTML rejected", () => Reject(() => EpubBook.Load(Fixture("missing", edit: f => f.Remove("OEBPS/chapter.xhtml")))));
Test("Malformed XHTML rejected", () => Reject(() => EpubBook.Load(Fixture("malformed", edit: f => f["OEBPS/chapter.xhtml"] = Encoding.UTF8.GetBytes("<broken>")))));
Test("Traversal archive entry rejected", () => Reject(() => EpubBook.Load(Fixture("traversal", edit: f => f["../outside.txt"] = new byte[] { 1 }))));
Test("Duplicate case-folded entries rejected", () => Reject(() => EpubBook.Load(Fixture("duplicate", edit: f => f["OEBPS/CHAPTER.xhtml"] = f["OEBPS/chapter.xhtml"]))));
Test("Malformed encryption descriptor rejected", () => Reject(() => EpubBook.Load(Fixture("encrypted", edit: f => f["META-INF/encryption.xml"] = Encoding.UTF8.GetBytes("<encryption/>")))));
void AddFont(Dictionary<string, byte[]> files, string algorithm, string reference = "OEBPS/font/Test%20Font.otf")
{
    files["OEBPS/font/Test Font.otf"] = Enumerable.Range(0, 2000).Select(i => (byte)(i % 251)).ToArray();
    files["OEBPS/content.opf"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["OEBPS/content.opf"])
        .Replace("</manifest>", "<item id=\"font\" href=\"font/Test%20Font.otf\" media-type=\"application/vnd.ms-opentype\"/></manifest>"));
    files["META-INF/encryption.xml"] = Encoding.UTF8.GetBytes($"""
    <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
      <enc:EncryptedData><enc:EncryptionMethod Algorithm="{algorithm}"/><enc:CipherData><enc:CipherReference URI="{reference}"/></enc:CipherData></enc:EncryptedData>
    </encryption>
    """);
}
foreach (var (label, algorithm) in new[] { ("IDPF", "http://www.idpf.org/2008/embedding"), ("Adobe", "http://ns.adobe.com/pdf/enc#RC") })
{
    Test(label + " obfuscated font, descriptor and original identifier preserved", () =>
    {
        var fontBook = Fixture(label, edit: f => AddFont(f, algorithm));
        Assert(EpubBook.Load(fontBook).ObfuscatedFontCount == 1);
        var folder = BatchGenerator.Generate(fontBook, readers.Take(2), root).Folder;
        foreach (var copy in Directory.GetFiles(folder, "*.epub"))
        {
            Assert(EpubBook.Load(copy, true).ObfuscatedFontCount == 1);
            foreach (var entry in new[] { "OEBPS/font/Test Font.otf", "META-INF/encryption.xml" })
                Assert(ReadEntry(copy, entry).SequenceEqual(ReadEntry(fontBook, entry)));
            XNamespace opf = "http://www.idpf.org/2007/opf";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            var before = XDocument.Parse(Encoding.UTF8.GetString(ReadEntry(fontBook, "OEBPS/content.opf")));
            var after = XDocument.Parse(Encoding.UTF8.GetString(ReadEntry(copy, "OEBPS/content.opf")));
            Assert((string?)before.Root!.Attribute("unique-identifier") == (string?)after.Root!.Attribute("unique-identifier"));
            Assert(before.Descendants(dc + "identifier").All(id => after.Descendants(dc + "identifier").Any(e => XNode.DeepEquals(id, e))));
        }
    });
}
Test("Real encryption algorithm rejected", () => Reject(() => EpubBook.Load(Fixture("drm", edit: f => AddFont(f, "http://www.w3.org/2001/04/xmlenc#aes128-cbc")))));
Test("Mixed font obfuscation and DRM rejected", () => Reject(() => EpubBook.Load(Fixture("mixed-drm", edit: f =>
{
    AddFont(f, "http://www.idpf.org/2008/embedding");
    var doc = XDocument.Parse(Encoding.UTF8.GetString(f["META-INF/encryption.xml"]));
    var extra = new XElement(doc.Root!.Elements().Single());
    extra.Elements().First().SetAttributeValue("Algorithm", "http://www.w3.org/2001/04/xmlenc#aes128-cbc");
    doc.Root.Add(extra); f["META-INF/encryption.xml"] = Encoding.UTF8.GetBytes(doc.ToString());
}))));
Test("Obfuscation targeting XHTML rejected", () => Reject(() => EpubBook.Load(Fixture("non-font", edit: f => AddFont(f, "http://www.idpf.org/2008/embedding", "OEBPS/chapter.xhtml")))));
Test("Missing obfuscated font rejected", () => Reject(() => EpubBook.Load(Fixture("missing-font", edit: f => { AddFont(f, "http://www.idpf.org/2008/embedding"); f.Remove("OEBPS/font/Test Font.otf"); }))));
Test("Unsafe obfuscation URI rejected", () => Reject(() => EpubBook.Load(Fixture("unsafe-font", edit: f => AddFont(f, "http://www.idpf.org/2008/embedding", "../OEBPS/font/Test%20Font.otf")))));
Test("Duplicate obfuscation references rejected", () => Reject(() => EpubBook.Load(Fixture("duplicate-font", edit: f =>
{
    AddFont(f, "http://www.idpf.org/2008/embedding");
    var doc = XDocument.Parse(Encoding.UTF8.GetString(f["META-INF/encryption.xml"]));
    doc.Root!.Add(new XElement(doc.Root.Elements().Single())); f["META-INF/encryption.xml"] = Encoding.UTF8.GetBytes(doc.ToString());
}))));
Test("Signed archive rejected", () => Reject(() => EpubBook.Load(Fixture("signed", edit: f => f["META-INF/signatures.xml"] = Encoding.UTF8.GetBytes("<signatures/>")))));
Test("Broken spine rejected", () => Reject(() => EpubBook.Load(Fixture("spine", edit: f => f["OEBPS/content.opf"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f["OEBPS/content.opf"]).Replace("idref=\"chapter\"", "idref=\"missing\""))))));
Test("Remote manifest resources rejected", () => Reject(() => EpubBook.Load(Fixture("remote", edit: f => f["OEBPS/content.opf"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f["OEBPS/content.opf"]).Replace("href=\"image.png\"", "href=\"https://example.com/image.png\""))))));
Test("External entity is never resolved", () => Reject(() => EpubBook.Load(Fixture("entity", edit: f => f["OEBPS/chapter.xhtml"] = Encoding.UTF8.GetBytes("<!DOCTYPE html [<!ENTITY ext SYSTEM 'file:///C:/Windows/win.ini'>]><html xmlns=\"http://www.w3.org/1999/xhtml\"><body>&ext;</body></html>")))));
Test("Cancellation removes partial batch", () =>
{
    var before = Directory.GetDirectories(root).Order().ToArray();
    using var cancel = new CancellationTokenSource();
    Reject(() => BatchGenerator.Generate(master, readers, root, new InlineProgress(_ => cancel.Cancel()), cancel.Token));
    Assert(before.SequenceEqual(Directory.GetDirectories(root).Order()));
});
Test("Invalid mimetype header rejected", () =>
{
    var path = Fixture("header"); var bytes = File.ReadAllBytes(path); bytes[8] = 8; File.WriteAllBytes(path, bytes);
    Reject(() => EpubBook.Load(path));
});
Test("Existing generated mapping identifies each of 15 recipients by full ID", () =>
{
    foreach (var copy in Directory.GetFiles(batch, "*.epub"))
    {
        var book = EpubBook.Load(copy, true);
        var match = RecipientMapping.Find(Path.Combine(batch, RecipientMapping.DefaultFileName), book.Fingerprints().Single());
        Assert(match != null && match.FileName == Path.GetFileName(copy) && match.CopySha256 == book.SourceHash);
        Assert(readers.Contains(new Recipient(match!.Name, match.Email)));
    }
});
Test("Leak inspection searches batch folders and distinguishes exact file evidence", () =>
{
    var copy = Directory.GetFiles(batch, "*.epub")[0];
    var book = EpubBook.Load(copy, true);
    var result = LeakInspector.Search(copy, book.Fingerprints(), root);
    Assert(result.MappingsScanned > 0 && result.Matches.Any(m => m.Recipient.FileName == Path.GetFileName(copy) && m.ExactFileMatch), "Search did not find the exact mapped reader copy.");
    var absent = LeakInspector.Search(copy, new[] { "ARC-" + new string('0', 32) }, root);
    Assert(absent.Matches.Count == 0, "Search incorrectly attributed a fingerprint absent from all mappings.");
});
Test("EPUB leak inspection can recover its ID from repeated XHTML signals", () =>
{
    var source = Directory.GetFiles(batch, "*.epub")[0];
    var altered = Path.Combine(root, "xhtml-signal-only.epub"); File.Copy(source, altered);
    string id = EpubBook.Load(source, true).Fingerprints().Single();
    using (var zip = ZipFile.Open(altered, ZipArchiveMode.Update))
    {
        var entry = zip.GetEntry("OEBPS/content.opf")!;
        XDocument package; using (var stream = entry.Open()) package = XDocument.Load(stream);
        package.Root!.Element((XNamespace)"http://www.idpf.org/2007/opf" + "metadata")!.Elements((XNamespace)"http://purl.org/dc/elements/1.1/" + "identifier").Where(x => x.Value.StartsWith("urn:arc-fingerprint:", StringComparison.Ordinal)).Remove();
        entry.Delete();
        var replacement = zip.CreateEntry("OEBPS/content.opf"); using var output = replacement.Open(); package.Save(output);
    }
    var recovered = EpubBook.Load(altered, true);
    Assert(recovered.Fingerprints().Contains(id, StringComparer.OrdinalIgnoreCase), "Repeated XHTML markers did not provide a fallback fingerprint.");
    var search = LeakInspector.Search(altered, recovered.Fingerprints(), batch);
    Assert(search.Matches.Count == 1 && !search.Matches[0].ExactFileMatch, "The recovered fingerprint should report a changed-file match.");
});
Test("Unknown ID never falls back to filename or recipient order", () => Assert(RecipientMapping.Find(Path.Combine(batch, RecipientMapping.DefaultFileName), "ARC-" + new string('0', 32)) == null));
Test("Renamed EPUB still resolves by ID", () =>
{
    var renamed = Path.Combine(root, "renamed.epub"); File.Copy(Directory.GetFiles(batch, "*.epub")[0], renamed);
    var book = EpubBook.Load(renamed, true);
    var match = RecipientMapping.Find(Path.Combine(batch, RecipientMapping.DefaultFileName), book.Fingerprints().Single());
    Assert(match != null && match.CopySha256 == book.SourceHash && match.FileName != Path.GetFileName(renamed));
});
Test("Mapping supports quoted Unicode names and reordered columns", () =>
{
    var csv = Path.Combine(root, "quoted-map.csv"); var id = "ARC-" + new string('A', 32);
    File.WriteAllText(csv, $"Email,FileName,Name,ArcId\r\njane@example.com,copy.epub,\"Döe, Jane\",{id}\r\n", new UTF8Encoding(true));
    var match = RecipientMapping.Find(csv, id); Assert(match?.Name == "Döe, Jane" && match.CopySha256 == "");
});
Test("Duplicate ID after a matched row is rejected", () =>
{
    var csv = Path.Combine(root, "duplicate-map.csv"); var id = "ARC-" + new string('B', 32);
    File.WriteAllText(csv, $"ArcId,Name,Email,FileName\r\n{id},First,first@example.com,first.epub\r\n{id.ToLowerInvariant()},Second,second@example.com,second.epub\r\n");
    Reject(() => RecipientMapping.Find(csv, id));
});
Test("Recipient import CSV is not accepted as a private mapping", () =>
{
    var csv = Path.Combine(root, "import-not-map.csv"); File.WriteAllText(csv, "Name,Email\r\nJane,jane@example.com\r\n");
    Reject(() => RecipientMapping.Find(csv, "ARC-" + new string('A', 32)));
});
Test("Duplicate mapping headers rejected", () =>
{
    var csv = Path.Combine(root, "bad-headers.csv"); File.WriteAllText(csv, "ArcId,Name,Email,FileName,arcid\r\n");
    Reject(() => RecipientMapping.Find(csv, "ARC-" + new string('A', 32)));
});
Test("Malformed mapping and hash rejected", () =>
{
    var csv = Path.Combine(root, "bad-mapping.csv"); var id = "ARC-" + new string('C', 32);
    File.WriteAllText(csv, $"ArcId,Name,Email,FileName,CopySha256\r\n{id},Jane,jane@example.com,copy.epub,not-a-hash\r\n");
    Reject(() => RecipientMapping.Find(csv, id));
    File.WriteAllText(csv, "ArcId,Name,Email,FileName\r\n\"unclosed quote");
    Reject(() => RecipientMapping.Find(csv, id));
});
Test("Existing EPUB batch can be extended and updates its private mapping", () =>
{
    var originalBatch = BatchGenerator.Generate(master, readers.Take(2), root).Folder;
    var mapping = Path.Combine(originalBatch, RecipientMapping.DefaultFileName);
    var originalMap = File.ReadAllBytes(mapping);
    var added = new Recipient("New Reader", "new.reader@example.com");
    var result = ExistingBatchAppender.Add(mapping, master, added);
    Assert(result.RecipientCount == 3 && result.FileName.EndsWith("003-" + result.ArcId[4..12] + ".epub", StringComparison.Ordinal), "New EPUB copy did not continue the existing numbering.");
    Assert(File.Exists(Path.Combine(originalBatch, result.FileName)) && !originalMap.SequenceEqual(File.ReadAllBytes(mapping)), "The existing batch or mapping was not updated.");
    Assert(Recipients.ReadCsv(mapping).Count == 3 && Recipients.ReadCsv(mapping).Last() == added, "New reader was not appended to the recipient map.");
    var copy = EpubBook.Load(Path.Combine(originalBatch, result.FileName), true);
    Assert(copy.Fingerprints().Contains(result.ArcId, StringComparer.OrdinalIgnoreCase), "New EPUB copy did not retain its ID.");
    var match = RecipientMapping.Find(mapping, result.ArcId);
    Assert(match?.Name == added.Name && match.Email == added.Email && match.FileName == result.FileName && match.CopySha256 == copy.SourceHash, "New fingerprint did not resolve to the appended recipient row.");
    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(originalBatch, "validation-report.json")));
    Assert(report.RootElement.GetProperty("RecipientCount").GetInt32() == 3 && result.ReportUpdated, "Batch report count was not refreshed.");
});
Test("Existing batch refuses the wrong master, duplicate reader, and modified copy", () =>
{
    var originalBatch = BatchGenerator.Generate(master, readers.Take(1), root).Folder;
    var mapping = Path.Combine(originalBatch, RecipientMapping.DefaultFileName);
    var before = File.ReadAllBytes(mapping);
    var wrongMaster = Fixture("wrong-existing-master", edit: files => files["OEBPS/chapter.xhtml"] = Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>Different master content.</body></html>"));
    Reject(() => ExistingBatchAppender.Add(mapping, wrongMaster, new Recipient("New", "new@example.com")));
    Reject(() => ExistingBatchAppender.Add(mapping, master, readers[0]));
    Assert(before.SequenceEqual(File.ReadAllBytes(mapping)) && Directory.GetFiles(originalBatch, "*.epub").Length == 1, "A rejected addition changed the batch.");
    File.AppendAllText(Directory.GetFiles(originalBatch, "*.epub").Single(), "tampered");
    Reject(() => ExistingBatchAppender.Add(mapping, master, new Recipient("New", "new@example.com")));
    Assert(before.SequenceEqual(File.ReadAllBytes(mapping)) && Directory.GetFiles(originalBatch, "*.epub").Length == 1, "A modified batch was partially changed.");
    Assert(!BatchVerifier.Verify(mapping).IsHealthy, "The full batch check failed to report a modified copy.");
});
Test("Existing PDF batch can be extended with a verified PDF copy", () =>
{
    var pdf = PdfFixture("append-pdf-master");
    var originalBatch = BatchGenerator.Generate(pdf, new[] { new Recipient("PDF One", "pdf.one@example.com") }, root).Folder;
    var result = ExistingBatchAppender.Add(Path.Combine(originalBatch, RecipientMapping.DefaultFileName), pdf, new Recipient("PDF Two", "pdf.two@example.com"));
    var path = Path.Combine(originalBatch, result.FileName);
    Assert(File.Exists(path) && PdfBook.Verify(path, result.ArcId).FingerprintId == result.ArcId, "Added PDF copy failed validation.");
    Assert(RecipientMapping.Find(Path.Combine(originalBatch, RecipientMapping.DefaultFileName), result.ArcId)?.Name == "PDF Two", "Added PDF was not matched in the existing mapping.");
});
Test("Email review can cancel publication without leaving a partial batch", () =>
{
    var before = Directory.GetDirectories(root, "ARC-*").Length;
    var opts = new EmailDraftOptions("For {firstName}", "Hello {name}", GreetingStyle.Hello, Array.Empty<string>());
    Reject(() => BatchGenerator.Generate(master, new[] { new Recipient("Preview Reader", "preview@example.com") }, root,
        emailDraft: opts, previewEmailDrafts: _ => false));
    Assert(Directory.GetDirectories(root, "ARC-*").Length == before && Directory.GetDirectories(root, ".arc-incomplete-*").Length == 0, "Cancelling preview published a batch or left staging files.");
});
Test("Batch check can recreate missing send folders", () =>
{
    var generated = BatchGenerator.Generate(master, readers.Take(2), root);
    var mapping = Path.Combine(generated.Folder, RecipientMapping.DefaultFileName);
    var target = Path.Combine(generated.Folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(2, "Reader 2"));
    Directory.Delete(target, true);
    Assert(!BatchVerifier.Verify(mapping).IsHealthy, "Batch check missed the absent reader send folder.");
    Assert(BatchMaintenance.CreateMissingSendFolders(mapping) == 1, "Missing send folder was not recreated.");
    Assert(BatchVerifier.Verify(mapping, master).IsHealthy, "Repaired batch failed its full integrity check.");
});
Test("Recipient correction refreshes its draft and undo removal archives files safely", () =>
{
    var extra = Path.Combine(root, "persistent-extra.txt"); File.WriteAllText(extra, "attachment");
    var options = new EmailDraftOptions("Your copy {bookTitle}", "For {firstName}", GreetingStyle.Hi, new[] { extra });
    var generated = BatchGenerator.Generate(master, readers.Take(3), root, emailDraft: options);
    var mapping = Path.Combine(generated.Folder, RecipientMapping.DefaultFileName);
    var secondBook = Directory.GetFiles(generated.Folder, "*Reader 2*.epub").Single();
    var secondId = EpubBook.Load(secondBook, true).Fingerprints().Single();
    EmailDraftPreview? shown = null;
    var corrected = BatchMaintenance.Correct(mapping, secondId, "Reader Two Corrected", "reader.two.corrected@example.com", previewEmailDraft: p => { shown = p; return true; });
    Assert(shown?.RecipientEmail == "reader.two.corrected@example.com" && shown.Subject.Contains("Test Book"), "Corrected draft preview was not personalized.");
    Assert(!File.Exists(secondBook) && File.Exists(Path.Combine(generated.Folder, corrected.FileName)), "Correction did not safely rename the reader copy.");
    var updated = RecipientMapping.Find(mapping, secondId);
    Assert(updated?.Name == "Reader Two Corrected" && updated.Email == "reader.two.corrected@example.com", "Correction did not update private mapping details.");
    var send = Path.Combine(generated.Folder, RecipientPackageBuilder.RootFolderName, RecipientPackageBuilder.FolderName(2, "Reader Two Corrected"));
    Assert(Directory.Exists(send) && BatchVerifier.Verify(mapping, master).IsHealthy, "Correction did not refresh the send folder or batch report.");
    var removed = BatchMaintenance.Remove(mapping, secondId);
    Assert(removed.ArchivedFolder is not null && Directory.Exists(removed.ArchivedFolder), "Undo did not preserve the removed files for recovery.");
    Assert(RecipientMapping.Find(mapping, secondId) is null && Recipients.ReadCsv(mapping).Count == 2, "Removed reader remains in the active mapping.");
    Assert(!File.Exists(Path.Combine(generated.Folder, corrected.FileName)) && !Directory.Exists(send), "Removed reader files remain in the active batch folders.");
    Assert(BatchVerifier.Verify(mapping, master).IsHealthy, "The remaining batch failed verification after removing a reader.");
});
Test("Adding a reader reuses saved email settings without reopening the settings dialog", () =>
{
    var extra = Path.Combine(root, "reopen-settings.txt"); File.WriteAllText(extra, "saved attachment");
    var options = new EmailDraftOptions("For {firstName}: {bookTitle}", "Good to have you, {name}.", GreetingStyle.GoodEvening, new[] { extra });
    var generated = BatchGenerator.Generate(master, new[] { new Recipient("Original Reader", "original@example.com") }, root, emailDraft: options);
    var mapping = Path.Combine(generated.Folder, RecipientMapping.DefaultFileName);
    EmailDraftPreview? shown = null;
    var result = ExistingBatchAppender.Add(mapping, master, new Recipient("Later Reader", "later@example.com"), previewEmailDraft: p => { shown = p; return true; });
    Assert(shown?.RecipientEmail == "later@example.com" && shown.Subject == "For Later: Test Book" && shown.ExtraAttachmentNames.Any(name => name.EndsWith("reopen-settings.txt", StringComparison.OrdinalIgnoreCase)), "Saved template or attachment was not reused after restart.");
    Assert(File.Exists(Path.Combine(generated.Folder, Path.ChangeExtension(result.FileName, ".eml"))) && BatchVerifier.Verify(mapping, master).IsHealthy, "Added recipient draft did not pass the complete batch check.");
});
Test("Batch merge normalizes old and current projects while preserving fingerprints and source files", () =>
{
    var parent = Path.Combine(root, "merge-projects"); var oldFolder = Path.Combine(parent, "old-project"); var newFolder = Path.Combine(parent, "new-project");
    Directory.CreateDirectory(oldFolder); Directory.CreateDirectory(newFolder);
    var oldExtra = Path.Combine(root, "merge-extra.txt"); File.WriteAllText(oldExtra, "Extra attachment");
    var oldBatch = BatchGenerator.Generate(master, new[] { new Recipient("Old Project Reader", "old.project@example.com") }, oldFolder,
        emailDraft: new EmailDraftOptions("Your book", "Thanks for helping.", GreetingStyle.Hello, new[] { oldExtra }));
    var newBatch = BatchGenerator.Generate(master, new[] { new Recipient("New Project Reader", "old.project@example.com"), new Recipient("Another Reader", "another@example.com") }, newFolder);
    string oldMap = Path.Combine(oldBatch.Folder, RecipientMapping.DefaultFileName);
    var oldRow = BatchMaintenance.List(oldMap).Single();
    // Simulate an early-version map that lacks saved copy/master hashes and a Format column.
    File.WriteAllText(oldMap, "ArcId,Name,Email,FileName\r\n" + string.Join(",", new[] { oldRow.ArcId, oldRow.Name, oldRow.Email, oldRow.FileName }.Select(Recipients.CsvField)) + "\r\n", new UTF8Encoding(true));
    string oldMapHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(oldMap)));
    string oldCopy = Path.Combine(oldBatch.Folder, oldRow.FileName);
    string oldHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(oldCopy)));
    string newMap = Path.Combine(newBatch.Folder, RecipientMapping.DefaultFileName);
    string newMapHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(newMap)));
    var sourceRows = BatchMaintenance.List(newMap);
    string[] originalIds = new[] { oldRow.ArcId }.Concat(sourceRows.Select(r => r.ArcId)).OrderBy(x => x).ToArray();
    string removedSend = Path.Combine(oldBatch.Folder, RecipientPackageBuilder.RootFolderName);
    if (Directory.Exists(removedSend)) Directory.Delete(removedSend, true);
    var merged = BatchMerger.Merge(parent, parent);
    Assert(merged.SourceBatchCount == 2 && merged.RecipientCount == 3 && merged.EmailDraftsCopied == 1, "Merge counts were incorrect.");
    string map = Path.Combine(merged.Folder, RecipientMapping.DefaultFileName);
    var mergedRows = BatchMaintenance.List(map);
    Assert(mergedRows.Select(r => r.ArcId).OrderBy(x => x).SequenceEqual(originalIds), "Merge changed or lost a fingerprint ID.");
    Assert(File.ReadAllText(map).Contains("SourceBatch") && File.ReadAllText(map).Contains("\"" + oldRow.ArcId + "\""), "Merge did not normalize the private mapping schema.");
    Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(oldCopy))) == oldHash, "Merge modified an original source copy.");
    Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(oldMap))) == oldMapHash && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(newMap))) == newMapHash, "Merge modified a source mapping.");
    var mergedOld = mergedRows.Single(r => r.ArcId == oldRow.ArcId);
    var mergedEml = Path.Combine(merged.Folder, Path.ChangeExtension(mergedOld.FileName, ".eml"));
    string draftText = File.ReadAllText(mergedEml);
    Assert(draftText.Contains("filename*=utf-8''" + Uri.EscapeDataString(mergedOld.FileName), StringComparison.OrdinalIgnoreCase), "Merged email draft attachment name was not synchronized with the new collection filename.");
    Assert(BatchVerifier.Verify(map).IsHealthy, "Merged copies, mappings, and send folders failed batch validation.");
    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(merged.Folder, "validation-report.json")));
    Assert(report.RootElement.GetProperty("MergedBatch").GetBoolean() && report.RootElement.GetProperty("MasterHashesMissing").GetInt32() == 1 && report.RootElement.GetProperty("EmailAttachmentNamesNormalized").GetInt32() == 1, "Merge report did not record legacy source limitations or draft changes.");
    var remerged = BatchMerger.Merge(parent, parent);
    Assert(remerged.RecipientCount == 3 && remerged.DuplicateRowsSkipped == 3, "Re-merging a collection with its original batches did not safely deduplicate preserved IDs.");
});
Test("Batch merge requires confirmation and records copies with changed hashes", () =>
{
    var parent = Path.Combine(root, "merge-changed-hash");
    Directory.CreateDirectory(parent);
    string folderA = Path.Combine(parent, "a"), folderB = Path.Combine(parent, "b");
    Directory.CreateDirectory(folderA); Directory.CreateDirectory(folderB);
    var a = BatchGenerator.Generate(master, new[] { new Recipient("Changed Reader", "changed@example.com") }, folderA);
    _ = BatchGenerator.Generate(master, new[] { new Recipient("Unchanged Reader", "unchanged@example.com") }, folderB);
    string map = Path.Combine(a.Folder, RecipientMapping.DefaultFileName);
    var row = BatchMaintenance.List(map).Single();
    string copy = Path.Combine(a.Folder, row.FileName);
    string savedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy)));
    using (var archive = System.IO.Compression.ZipFile.Open(copy, System.IO.Compression.ZipArchiveMode.Update))
        archive.CreateEntry("META-INF/merge-test-marker.txt");
    string currentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy)));
    Assert(savedHash != currentHash, "Fixture mutation did not change the EPUB hash.");
    bool rejected = false;
    try { BatchMerger.Merge(parent, parent); } catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "A changed source copy merged without confirmation.");
    var prompted = BatchMerger.Merge(parent, parent, changes => changes.Count == 1 && changes[0].SavedHash == savedHash && changes[0].CurrentHash == currentHash);
    Assert(prompted.HashChanges.Count == 1, "Merge result did not report the changed source copy.");
    string mergedCopy = Path.Combine(prompted.Folder, BatchMaintenance.List(Path.Combine(prompted.Folder, RecipientMapping.DefaultFileName)).Single(r => r.ArcId == row.ArcId).FileName);
    Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mergedCopy))) == currentHash, "Merged copy did not preserve the changed source bytes.");
    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(prompted.Folder, "validation-report.json")));
    Assert(report.RootElement.GetProperty("SourceCopyHashChanges").GetArrayLength() == 1, "Validation report did not disclose the changed hash.");
});
Test("A consolidated collection can be extended with a matching master and selected edition", () =>
{
    var parent = Path.Combine(root, "merge-then-add-reader"); Directory.CreateDirectory(parent);
    string arcFolder = Path.Combine(parent, "arc-source"), betaFolder = Path.Combine(parent, "beta-source");
    Directory.CreateDirectory(arcFolder); Directory.CreateDirectory(betaFolder);
    var arcBatch = BatchGenerator.Generate(master, new[] { new Recipient("ARC Reader", "arc.reader@example.com") }, arcFolder);
    var betaBatch = BatchGenerator.Generate(master, new[] { new Recipient("Beta Reader", "beta.reader@example.com") }, betaFolder, editionKind: EditionKind.Beta);
    var merged = BatchMerger.Merge(parent, parent);
    string map = Path.Combine(merged.Folder, RecipientMapping.DefaultFileName);
    var before = BatchMaintenance.List(map).Select(r => r.ArcId).OrderBy(x => x).ToArray();
    bool requiredEdition = false;
    try { ExistingBatchAppender.Add(map, master, new Recipient("New Beta Reader", "new.beta@example.com")); }
    catch (InvalidDataException ex) { requiredEdition = ex.Message.Contains("more than one edition", StringComparison.OrdinalIgnoreCase); }
    Assert(requiredEdition, "Appending to a multi-edition consolidated collection did not request an edition choice.");
    var added = ExistingBatchAppender.Add(map, master, new Recipient("New Beta Reader", "new.beta@example.com"), mergedEdition: "Beta");
    var after = BatchMaintenance.List(map);
    Assert(added.RecipientCount == 3 && after.Count == 3, "The consolidated collection did not gain exactly one reader.");
    Assert(before.All(id => after.Any(row => row.ArcId == id)), "Adding a reader changed an already-issued ID.");
    var newRow = after.Single(row => row.Email == "new.beta@example.com");
    Assert(newRow.FileName.Contains(" - Beta - ", StringComparison.Ordinal),
        "The new consolidated row was not assigned to the selected edition.");
    Assert(BatchVerifier.Verify(map, master).IsHealthy, "The extended consolidated collection failed its batch check against a represented master.");
    using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(merged.Folder, "validation-report.json")));
    Assert(report.RootElement.GetProperty("RecipientCount").GetInt32() == 3, "The consolidated validation report was not updated.");
});
Console.WriteLine($"{passed} tests passed. Fixtures: {root}");

sealed class InlineProgress(Action<BatchProgress> callback) : IProgress<BatchProgress>
{ public void Report(BatchProgress value) => callback(value); }
