using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArcFingerprint.Core;

/// <summary>Applies optional, deterministic thesaurus substitutions to XHTML text nodes only.</summary>
public static class ThesaurusVariants
{
    // Small local word list, intentionally limited to common terms. Synonyms can shift nuance; this is an optional ARC-copy variation, not an editorial proofing tool.
    private static readonly (string Word, string Alternative)[] Pairs =
    {
        ("begin", "start"), ("began", "started"), ("end", "finish"), ("help", "assist"),
        ("show", "reveal"), ("use", "employ"), ("small", "little"), ("large", "big"),
        ("quickly", "rapidly"), ("quiet", "silent"), ("happy", "glad"), ("angry", "upset")
    };

    public static (byte[] Content, int Replacements) Apply(byte[] original, string id)
    {
        Encoding encoding;
        int bom = 0;
        if (original.Length >= 2 && original[0] == 0xff && original[1] == 0xfe) { encoding = Encoding.Unicode; bom = 2; }
        else if (original.Length >= 2 && original[0] == 0xfe && original[1] == 0xff) { encoding = Encoding.BigEndianUnicode; bom = 2; }
        else { encoding = new UTF8Encoding(false, true); if (original.Length >= 3 && original[0] == 0xef && original[1] == 0xbb && original[2] == 0xbf) bom = 3; }
        string xml;
        try { xml = encoding.GetString(original, bom, original.Length - bom); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Thesaurus variations support UTF-8 or UTF-16 XHTML. Re-export this document in one of those encodings.", ex); }
        XDocument document;
        try { document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch (System.Xml.XmlException ex) { throw new InvalidDataException("An XHTML document could not be read for the optional thesaurus variation.", ex); }

        int replacementCount = 0, pairIndex = 0;
        bool forcedOne = false;
        foreach (var node in document.DescendantNodes().OfType<XText>())
        {
            string text = node.Value;
            foreach (var (word, alternative) in Pairs)
            {
                bool useAlternative = ((Convert.ToInt32(id[4 + pairIndex % 28].ToString(), 16) >> (pairIndex % 4)) & 1) != 0;
                pairIndex++;
                if (!useAlternative && forcedOne) continue;
                var regex = new Regex($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                text = regex.Replace(text, match =>
                {
                    if (!useAlternative && forcedOne) return match.Value;
                    forcedOne = true;
                    replacementCount++;
                    string chosen = useAlternative || !forcedOne ? alternative : alternative;
                    return PreserveCase(match.Value, chosen);
                });
            }
            node.Value = text;
        }
        using var output = new MemoryStream();
        using (var writer = System.Xml.XmlWriter.Create(output, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = document.Declaration is null }))
            document.Save(writer);
        return (output.ToArray(), replacementCount);
    }

    private static string PreserveCase(string original, string replacement) =>
        original.All(char.IsUpper) ? replacement.ToUpperInvariant() : char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}
