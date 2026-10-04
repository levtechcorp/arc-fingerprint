using System.Net.Mail;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace ArcFingerprint.Core;

public sealed record Recipient(string Name, string Email);

public static class Recipients
{
    public static List<Recipient> ReadCsv(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Recipient CSV exceeds 2 MB.");
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("The CSV is empty.");
        int name = Array.FindIndex(headers, h => h.Equals("Name", StringComparison.OrdinalIgnoreCase));
        int email = Array.FindIndex(headers, h => h.Equals("Email", StringComparison.OrdinalIgnoreCase));
        if (name < 0 || email < 0 || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("Use a CSV with unique column headers including Name and Email.");
        var result = new List<Recipient>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            if (row.Length != headers.Length) throw new InvalidDataException($"CSV row {result.Count + 2} has the wrong number of fields.");
            result.Add(new(row[name], row[email]));
            if (result.Count > 1000) throw new InvalidDataException("Use at most 1,000 recipients per batch.");
        }
        return Validate(result);
    }

    public static List<Recipient> Validate(IEnumerable<Recipient> source)
    {
        var result = source.Select(r => new Recipient(r.Name.Trim(), r.Email.Trim())).ToList();
        if (result.Count is < 1 or > 1000) throw new InvalidDataException("Add between 1 and 1,000 recipients.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in result)
        {
            if (r.Name.Length is < 1 or > 200 || r.Email.Length > 254 || r.Name.Any(char.IsControl) || r.Email.Any(char.IsControl))
                throw new InvalidDataException("Each recipient needs a name and email without line breaks or control characters.");
            if (!MailAddress.TryCreate(r.Email, out var address) || address.Address != r.Email)
                throw new InvalidDataException($"Invalid email for {r.Name}.");
            if (!seen.Add(r.Email)) throw new InvalidDataException($"Duplicate email: {r.Email}");
        }
        return result;
    }

    // Quote fields and neutralize spreadsheet formulas in private recipient data.
    public static string CsvField(string value)
    {
        if (value.TrimStart().StartsWithAny('=', '+', '-', '@') || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool StartsWithAny(this string value, params char[] characters) => value.Length > 0 && characters.Contains(value[0]);
}

