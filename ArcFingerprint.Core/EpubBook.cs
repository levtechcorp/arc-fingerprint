using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArcFingerprint.Core;

public sealed class EpubBook
{
    internal static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    internal static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    internal const string Marker = "ARC-FINGERPRINT:";
    private const long MaxTotal = 256L * 1024 * 1024;
    private readonly Dictionary<string, byte[]> files;
    private readonly string packagePath;
    private readonly List<string> xhtmlPaths;
    public string Title { get; }
    public string SourceHash { get; }
    public int DocumentCount => xhtmlPaths.Count;
    public int ObfuscatedFontCount { get; }

    private EpubBook(Dictionary<string, byte[]> entries, string opf, List<string> documents, string title, string hash, int obfuscatedFonts)
    { files = entries; packagePath = opf; xhtmlPaths = documents; Title = title; SourceHash = hash; ObfuscatedFontCount = obfuscatedFonts; }

    internal static XDocument Xml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null,
            MaxCharactersInDocument = 32_000_000, MaxCharactersFromEntities = 1024
        });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void Require(bool test, string message)
    { if (!test) throw new InvalidDataException(message); }

    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') &&
        !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) &&
        !path.Split('/').Any(p => p is "." or "..");

    private static string Resolve(string basePath, string reference)
    {
        Require(!string.IsNullOrWhiteSpace(reference) && !reference.Contains('\\'), "Invalid EPUB resource reference.");
        var root = new Uri("https://epub.invalid/" + basePath);
        var resolved = new Uri(root, reference);
        Require(resolved.Host == "epub.invalid" && resolved.Scheme == "https", "Remote resources are unsupported. Use a self-contained EPUB.");
        var path = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        Require(SafePath(path), "Unsafe EPUB resource path.");
        return path;
    }

    public static EpubBook Load(string path, bool allowFingerprint = false)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(input.Length <= MaxTotal, "EPUB exceeds the 256 MB input limit.");
        var sourceHash = Convert.ToHexString(SHA256.HashData(input));
        input.Position = 0;
        // Verify the physical first local header, not just central-directory ordering.
        using (var header = new BinaryReader(input, Encoding.UTF8, true))
        {
            Require(input.Length >= 58 && header.ReadUInt32() == 0x04034b50, "Not a supported ZIP EPUB.");
            header.ReadUInt16(); var flags = header.ReadUInt16(); var method = header.ReadUInt16();
            input.Position = 18; var compressed = header.ReadUInt32(); var expanded = header.ReadUInt32();
            var nameLength = header.ReadUInt16(); var extraLength = header.ReadUInt16();
            Require((flags & 9) == 0 && method == 0 && compressed == 20 && expanded == 20 && nameLength == 8 && extraLength == 0,
                "EPUB mimetype must be the first, uncompressed entry with no extra fields. Re-export the master EPUB.");
            Require(Encoding.ASCII.GetString(header.ReadBytes(8)) == "mimetype", "First EPUB entry is not mimetype.");
        }
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
        Require(zip.Entries.Count is > 0 and <= 10000, "EPUB must contain 1–10,000 entries.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            Require(SafePath(entry.FullName) && names.Add(entry.FullName), "Unsafe or duplicate ZIP entry.");
            total += entry.Length;
            Require(entry.Length <= 64L * 1024 * 1024 && total <= MaxTotal, "EPUB expanded size exceeds safety limits (64 MB per file; 256 MB total).");
            using var content = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int count;
            while ((count = content.Read(chunk)) > 0)
            {
                Require(buffer.Length + count <= entry.Length, "ZIP entry has an inconsistent expanded size.");
                buffer.Write(chunk, 0, count);
            }
            Require(buffer.Length == entry.Length, "Truncated ZIP entry.");
            files.Add(entry.FullName, buffer.ToArray());
        }
        Require(files.TryGetValue("mimetype", out var mime) && Encoding.ASCII.GetString(mime) == "application/epub+zip", "Invalid EPUB mimetype.");
        Require(!files.ContainsKey("META-INF/signatures.xml"), "Signed EPUBs cannot be modified without invalidating their signatures.");
        Require(files.ContainsKey("META-INF/container.xml"), "Missing META-INF/container.xml.");
        var container = Xml(files["META-INF/container.xml"]);
        var roots = container.Root?.Element(Container + "rootfiles")?.Elements(Container + "rootfile").ToList();
        Require(container.Root?.Name == Container + "container" && roots?.Count == 1, "Use an EPUB with exactly one rendition/package.");
        string opfPath = (string?)roots![0].Attribute("full-path") ?? "";
        Require(SafePath(opfPath) && files.ContainsKey(opfPath), "Package file is missing or unsafe.");
        var package = Xml(files[opfPath]); var rootElement = package.Root;
        Require(rootElement?.Name == Opf + "package", "Invalid OPF package.");
        var version = (string?)rootElement!.Attribute("version");
        Require(version is "2.0" or "3.0", "Only EPUB 2.0 and 3.x packages are supported.");
        var metadata = rootElement.Element(Opf + "metadata");
        Require(metadata != null, "Missing package metadata.");
        var uid = (string?)rootElement.Attribute("unique-identifier");
        Require(!string.IsNullOrWhiteSpace(uid) && metadata!.Elements(Dc + "identifier").Any(e => (string?)e.Attribute("id") == uid && !string.IsNullOrWhiteSpace(e.Value)), "Missing book unique identifier.");
        Require(metadata!.Element(Dc + "title") != null && metadata.Element(Dc + "language") != null, "Missing title or language metadata.");
        if (!allowFingerprint)
            Require(!metadata.Elements(Dc + "identifier").Any(e => e.Value.StartsWith("urn:arc-fingerprint:", StringComparison.Ordinal)), "This EPUB already has an ARC fingerprint. Select your unmarked master.");
        var manifest = rootElement.Element(Opf + "manifest")?.Elements(Opf + "item").ToList();
        Require(manifest is { Count: > 0 }, "Missing EPUB manifest.");
        int obfuscatedFonts = ValidateFontObfuscation(files, opfPath, manifest!);
        var items = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var documents = new List<string>();
        var manifestPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest!)
        {
            var id = (string?)item.Attribute("id") ?? "";
            Require(id.Length > 0 && items.TryAdd(id, item), "Missing or duplicate manifest ID.");
            var location = Resolve(opfPath, (string?)item.Attribute("href") ?? "");
            Require(files.ContainsKey(location) && manifestPaths.Add(location), $"Missing or duplicate manifest resource: {location}");
            var mediaType = (string?)item.Attribute("media-type");
            Require(!string.IsNullOrWhiteSpace(mediaType), "Missing manifest media type.");
            if (mediaType == "application/xhtml+xml")
            {
                var doc = Xml(files[location]);
                Require(doc.Root?.Name == Html + "html" && doc.Root.Element(Html + "body") != null, $"Invalid XHTML: {location}");
                if (!allowFingerprint) Require(!doc.DescendantNodes().OfType<XComment>().Any(c => c.Value.Contains(Marker)), "Existing ARC comments found. Use the unmarked master.");
                documents.Add(location);
            }
            else if (mediaType is "image/svg+xml" or "application/x-dtbncx+xml") Xml(files[location]);
        }
        var spine = rootElement.Element(Opf + "spine");
        var refs = spine?.Elements(Opf + "itemref").ToList();
        Require(refs is { Count: > 0 } && documents.Count > 0, "Missing reading order or XHTML content.");
        foreach (var item in refs!) Require(items.ContainsKey((string?)item.Attribute("idref") ?? ""), "Spine references a missing manifest item.");
        if (version == "2.0") Require(items.ContainsKey((string?)spine!.Attribute("toc") ?? ""), "EPUB 2 is missing its NCX navigation reference.");
        if (version == "3.0") Require(manifest.Any(i => ((string?)i.Attribute("properties") ?? "").Split(' ').Contains("nav") && (string?)i.Attribute("media-type") == "application/xhtml+xml"), "EPUB 3 is missing its navigation document.");
        return new(files, opfPath, documents, metadata.Element(Dc + "title")!.Value, sourceHash, obfuscatedFonts);
    }

    private static int ValidateFontObfuscation(Dictionary<string, byte[]> files, string opfPath, List<XElement> manifest)
    {
        if (!files.TryGetValue("META-INF/encryption.xml", out var bytes)) return 0;
        XNamespace enc = "http://www.w3.org/2001/04/xmlenc#";
        var encryption = Xml(bytes);
        Require(encryption.Root?.Name == Container + "encryption", "Invalid EPUB encryption descriptor.");
        var entries = encryption.Root!.Elements().ToList();
        Require(entries.Count > 0 && entries.All(e => e.Name == enc + "EncryptedData"),
            "Unsupported EPUB encryption descriptor. Only IDPF or Adobe font obfuscation can be preserved.");
        var fontTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "font/otf", "font/ttf", "font/sfnt", "font/woff", "font/woff2",
            "application/vnd.ms-opentype", "application/font-sfnt", "application/font-woff",
            "application/x-font-opentype", "application/x-font-truetype", "application/x-font-ttf"
        };
        var fontPaths = manifest.Where(i => fontTypes.Contains((string?)i.Attribute("media-type") ?? ""))
            .Select(i => Resolve(opfPath, (string?)i.Attribute("href") ?? "")).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var methods = entry.Elements(enc + "EncryptionMethod").ToList();
            Require(methods.Count == 1, "Missing or ambiguous EPUB encryption method.");
            var algorithm = (string?)methods[0].Attribute("Algorithm");
            Require(algorithm is "http://www.idpf.org/2008/embedding" or "http://ns.adobe.com/pdf/enc#RC",
                "This EPUB uses DRM or an unsupported encryption method. Use a DRM-free master. Standard IDPF and Adobe font obfuscation are supported.");
            var cipherData = entry.Elements(enc + "CipherData").ToList();
            Require(cipherData.Count == 1 && cipherData[0].Elements().Count() == 1 && cipherData[0].Element(enc + "CipherReference") != null,
                "Invalid font obfuscation resource reference.");
            var reference = cipherData[0].Element(enc + "CipherReference")!;
            var uri = (string?)reference.Attribute("URI") ?? "";
            Require(!reference.HasElements && !uri.Contains('?') && !uri.Contains('#'), "Unsupported font obfuscation resource reference.");
            // OCF encryption references are relative to the archive root, not META-INF.
            var path = Uri.UnescapeDataString(uri);
            Require(SafePath(path) && files.ContainsKey(path) && fontPaths.Contains(path) && seen.Add(path),
                "An obfuscation reference is missing, duplicated, unsafe, or does not identify a manifest font.");
        }
        // No deobfuscation is needed: font bytes, encryption.xml, and original identifiers are copied unchanged.
        return seen.Count;
    }

    internal void WriteCopy(string destination, string id)
    {
        var package = Xml(files[packagePath]);
        var metadata = package.Root!.Element(Opf + "metadata")!;
        // Preserve the publication identifier, including NCX references to it.
        metadata.Add(new XElement(Dc + "identifier", "urn:arc-fingerprint:" + id));
        if ((string?)package.Root.Attribute("version") == "3.0")
        {
            var modified = metadata.Elements(Opf + "meta").FirstOrDefault(e => (string?)e.Attribute("property") == "dcterms:modified");
            if (modified == null) { modified = new XElement(Opf + "meta", new XAttribute("property", "dcterms:modified")); metadata.Add(modified); }
            modified.Value = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Put(zip, "mimetype", files["mimetype"], CompressionLevel.NoCompression);
        foreach (var (name, original) in files)
        {
            if (name == "mimetype") continue;
            byte[] content = original;
            if (name == packagePath)
            {
                using var buffer = new MemoryStream();
                using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false })) package.Save(writer);
                content = buffer.ToArray();
            }
            else if (xhtmlPaths.Contains(name)) content = AppendComment(original, id);
            Put(zip, name, content, CompressionLevel.Optimal);
        }
    }

    private static byte[] AppendComment(byte[] original, string id)
    {
        // Appending after the root is legal XML; the original XHTML bytes stay intact.
        Encoding encoding;
        if (original.Length >= 4 && (original[0] == 0 && original[1] == 0 || original[2] == 0 && original[3] == 0))
            throw new InvalidDataException("UTF-32 XHTML is unsupported. Export XHTML as UTF-8 or UTF-16.");
        if (original.Length >= 2 && original[0] == 0xff && original[1] == 0xfe) encoding = Encoding.Unicode;
        else if (original.Length >= 2 && original[0] == 0xfe && original[1] == 0xff) encoding = Encoding.BigEndianUnicode;
        else if (original.Length >= 2 && original[0] == 0 && original[1] == '<') encoding = Encoding.BigEndianUnicode;
        else if (original.Length >= 2 && original[0] == '<' && original[1] == 0) encoding = Encoding.Unicode;
        else
        {
            var declared = Xml(original).Declaration?.Encoding;
            if (declared != null && !new[] { "utf-8", "us-ascii", "iso-8859-1", "windows-1252" }.Contains(declared.ToLowerInvariant()))
                throw new InvalidDataException($"Unsupported XHTML encoding: {declared}. Re-export as UTF-8.");
            encoding = Encoding.UTF8; // Comment uses ASCII characters only.
        }
        return original.Concat(encoding.GetBytes("\n<!-- " + Marker + " " + id + " -->\n")).ToArray();
    }

    private static void Put(ZipArchive zip, string name, byte[] bytes, CompressionLevel level)
    { using var target = zip.CreateEntry(name, level).Open(); target.Write(bytes); }

    public IReadOnlyList<string> Fingerprints() => Xml(files[packagePath]).Root!.Element(Opf + "metadata")!.Elements(Dc + "identifier")
        .Select(e => e.Value).Where(v => v.StartsWith("urn:arc-fingerprint:", StringComparison.Ordinal)).Select(v => v[20..]).ToList();

    internal void Verify(string id)
    {
        Require(Fingerprints().SequenceEqual(new[] { id }), "Output metadata fingerprint verification failed.");
        foreach (var path in xhtmlPaths)
            Require(Xml(files[path]).Nodes().OfType<XComment>().Any(c => c.Value.Trim() == Marker + " " + id), "Output XHTML fingerprint verification failed.");
    }
}

