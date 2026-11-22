using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ArcFingerprint.Core;

/// <summary>Reads and fingerprints ordinary, unencrypted PDF files.</summary>
public sealed class PdfBook
{
    private const long MaxPdfBytes = 512L * 1024 * 1024;
    private const string IdElement = "/ArcFingerprintId";
    private const string PageIdElement = "/ArcFingerprintPageId";
    private static readonly Regex ArcIdPattern = new("\\AARC-[0-9A-F]{32}\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string Path { get; }
    public string Title { get; }
    public string SourceHash { get; }
    public int PageCount { get; }
    public string? FingerprintId { get; }
    public IReadOnlyList<string> Fingerprints { get; }
    public int PagesWithFingerprint { get; }

    private PdfBook(string path, string title, string hash, int pages, string? id, IReadOnlyList<string> fingerprints, int pagesWithFingerprint)
    { Path = path; Title = title; SourceHash = hash; PageCount = pages; FingerprintId = id; Fingerprints = fingerprints; PagesWithFingerprint = pagesWithFingerprint; }

    public static PdfBook Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists) throw new FileNotFoundException("Choose an existing PDF file.", fullPath);
        if (file.Length is < 8 or > MaxPdfBytes) throw new InvalidDataException("PDF files must be between 8 bytes and 512 MB.");
        using (var header = File.OpenRead(fullPath))
        {
            Span<byte> signature = stackalloc byte[5];
            if (header.Read(signature) != signature.Length || !signature.SequenceEqual("%PDF-"u8))
                throw new InvalidDataException("This file does not have a valid PDF header.");
        }

        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.AsSpan().IndexOf("/ByteRange"u8) >= 0)
            throw new InvalidDataException("Digitally signed PDFs are not supported because adding a fingerprint would invalidate the signature.");
        if (bytes.AsSpan().IndexOf("/AcroForm"u8) >= 0)
            throw new InvalidDataException("PDFs with interactive forms are not supported. PDFsharp has limited AcroForm support, so the app declines them to avoid changing form behavior.");

        try
        {
            using var document = PdfReader.Open(fullPath, PdfDocumentOpenMode.Modify);
            if (document.Pages.Count == 0) throw new InvalidDataException("The PDF has no pages.");
            var id = ReadId(document);
            var pageIds = new List<string>();
            foreach (PdfPage page in document.Pages) pageIds.AddRange(ReadPageId(page));
            var ids = pageIds.Concat(id is null ? Array.Empty<string>() : new[] { id })
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var title = string.IsNullOrWhiteSpace(document.Info.Title)
                ? System.IO.Path.GetFileNameWithoutExtension(fullPath)
                : document.Info.Title.Trim();
            if (title.Length > 512) title = title[..512];
            return new(fullPath, title, Convert.ToHexString(SHA256.HashData(bytes)), document.Pages.Count, id, ids, pageIds.Count);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new InvalidDataException("This PDF could not be safely opened for fingerprinting. Password-protected, damaged, or unsupported PDFs are not accepted.", ex);
        }
    }

    public void WriteCopy(string destination, string id, string edition, bool visibleMark, bool pageLevelFingerprint = false, bool coverMark = false)
    {
        if (!ArcIdPattern.IsMatch(id)) throw new ArgumentException("Invalid fingerprint ID.", nameof(id));
        try
        {
            using var document = PdfReader.Open(Path, PdfDocumentOpenMode.Modify);
            if (document.Pages.Count != PageCount) throw new InvalidDataException("The PDF page count changed after the master was checked.");
            if (ReadId(document) is not null) throw new InvalidDataException("The PDF already contains an ARC Fingerprint ID. Start from an unmarked master.");

            document.Info.Elements.SetString(IdElement, id);
            var keywordMarker = "ARC-Fingerprint:" + id;
            document.Info.Keywords = string.IsNullOrWhiteSpace(document.Info.Keywords)
                ? keywordMarker : document.Info.Keywords + "; " + keywordMarker;

            if (visibleMark || coverMark)
            {
                // Use PDFsharp's Windows font resolver and put a small, legible mark in the footer of every page.
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
                var font = new XFont("Arial", 8, XFontStyleEx.Regular);
                var brush = new XSolidBrush(XColor.FromArgb(150, 75, 85, 95));
                string label = $"{edition} reader copy · {id}";
                for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
                {
                    if (!visibleMark && (!coverMark || pageIndex != 0)) continue;
                    var page = document.Pages[pageIndex];
                    using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    var width = page.Width.Point;
                    var height = page.Height.Point;
                    if (width < 100 || height < 100) throw new InvalidDataException("A PDF page is too small for the visible fingerprint mark.");
                    graphics.DrawString(label, font, brush,
                        new XRect(24, height - 23, Math.Max(40, width - 48), 12), XStringFormats.BottomRight);
                }
            }

            if (pageLevelFingerprint)
                foreach (var page in document.Pages) page.Elements.SetString(PageIdElement, id);

            document.Save(destination);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new InvalidDataException("PDFsharp could not save this PDF copy. It may be encrypted, damaged, signed, or use an unsupported PDF feature.", ex);
        }
    }

    public static PdfBook Verify(string path, string expectedId, bool requirePageIds = false)
    {
        var copy = Load(path);
        if (!string.Equals(copy.FingerprintId, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved PDF copy did not retain its fingerprint ID.");
        if (requirePageIds && (copy.PagesWithFingerprint != copy.PageCount || !copy.Fingerprints.Contains(expectedId, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("The saved PDF copy did not retain its page-level fingerprints.");
        return copy;
    }

    private static string? ReadId(PdfDocument document)
    {
        var id = document.Info.Elements.GetString(IdElement);
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (!ArcIdPattern.IsMatch(id)) throw new InvalidDataException("The PDF contains a malformed ARC Fingerprint ID.");
        return id.ToUpperInvariant();
    }

    private static IEnumerable<string> ReadPageId(PdfPage page)
    {
        var id = page.Elements.GetString(PageIdElement);
        if (string.IsNullOrWhiteSpace(id)) yield break;
        id = id.Trim();
        if (!ArcIdPattern.IsMatch(id)) throw new InvalidDataException("The PDF contains a malformed page fingerprint ID.");
        yield return id.ToUpperInvariant();
    }
}
