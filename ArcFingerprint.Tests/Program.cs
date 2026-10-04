using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ArcFingerprint.Core;

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
var master = Fixture("master");
var readers = Enumerable.Range(1, 15).Select(i => new Recipient("Reader " + i, $"reader{i}@example.com")).ToList();
string batch = "";
Test("EPUB 3 master structure", () => Assert(EpubBook.Load(master).DocumentCount == 2));
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
Console.WriteLine($"{passed} tests passed. Fixtures: {root}");

sealed class InlineProgress(Action<BatchProgress> callback) : IProgress<BatchProgress>
{ public void Report(BatchProgress value) => callback(value); }

