using System.Net.Mail;
using System.Text;

namespace ArcFingerprint.Core;

public enum GreetingStyle { Hello, Hi, GoodMorning, GoodAfternoon, GoodEvening, Howdy }

public sealed record EmailDraftOptions(string Subject, string Body, GreetingStyle Greeting, IReadOnlyList<string> Attachments)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Subject) || Subject.Length > 500 || Subject.Any(char.IsControl))
            throw new InvalidDataException("Enter an email subject without line breaks (up to 500 characters).");
        if (Body.Length > 100_000 || Body.Contains('\0')) throw new InvalidDataException("The email message is too long or contains an unsupported character.");
        if (!Enum.IsDefined(Greeting)) throw new InvalidDataException("Choose a greeting style.");
        foreach (var attachment in Attachments)
            if (!File.Exists(attachment)) throw new FileNotFoundException("An email attachment could not be found.", attachment);
    }
}

public sealed record EmailDraftPreview(string RecipientName, string RecipientEmail, string Subject, string Body,
    string BookFileName, IReadOnlyList<string> ExtraAttachmentNames, string DraftFileName, long EstimatedMessageBytes);

/// <summary>Creates RFC 5322 .eml files locally. It never sends messages.</summary>
public static class EmailDraftGenerator
{
    public static void Write(string path, Recipient recipient, string bookTitle, string edition,
        string bookCopyPath, EmailDraftOptions options)
    {
        options.Validate();
        if (!MailAddress.TryCreate(recipient.Email, out var address) || address.Address != recipient.Email)
            throw new InvalidDataException("The draft recipient email address is invalid.");

        var (subject, text) = Render(recipient, bookTitle, edition, options);
        var attachments = new[] { bookCopyPath }.Concat(options.Attachments).ToArray();
        if (attachments.Any(p => !File.Exists(p))) throw new FileNotFoundException("An email attachment could not be found.");

        string boundary = "----ArcFingerprint_" + Guid.NewGuid().ToString("N");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\r\n" };
        writer.WriteLine("To: <" + recipient.Email + ">");
        writer.WriteLine("Subject: " + EncodeHeader(subject));
        writer.WriteLine("MIME-Version: 1.0");
        writer.WriteLine("X-Unsent: 1");
        writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        writer.WriteLine();
        writer.WriteLine("This is a multi-part message in MIME format.");
        writer.WriteLine("--" + boundary);
        writer.WriteLine("Content-Type: text/plain; charset=utf-8");
        writer.WriteLine("Content-Transfer-Encoding: base64");
        writer.WriteLine();
        WriteBase64(writer, Encoding.UTF8.GetBytes(text));
        foreach (string file in attachments)
        {
            string name = Path.GetFileName(file);
            writer.WriteLine("--" + boundary);
            writer.WriteLine("Content-Type: application/octet-stream; name*=utf-8''" + Uri.EscapeDataString(name));
            writer.WriteLine("Content-Transfer-Encoding: base64");
            writer.WriteLine("Content-Disposition: attachment; filename*=utf-8''" + Uri.EscapeDataString(name));
            writer.WriteLine();
            using var input = File.OpenRead(file);
            byte[] buffer = new byte[57];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                writer.WriteLine(Convert.ToBase64String(buffer, 0, read));
        }
        writer.WriteLine("--" + boundary + "--");
    }

    public static EmailDraftPreview Preview(Recipient recipient, string bookTitle, string edition,
        string bookCopyPath, string draftPath, EmailDraftOptions options)
    {
        options.Validate();
        var (subject, body) = Render(recipient, bookTitle, edition, options);
        return new(recipient.Name, recipient.Email, subject, body, Path.GetFileName(bookCopyPath),
            options.Attachments.Select(n => Path.GetFileName(n)!).ToArray(), Path.GetFileName(draftPath)!, new FileInfo(draftPath).Length);
    }

    private static (string Subject, string Body) Render(Recipient recipient, string bookTitle, string edition, EmailDraftOptions options)
    {
        var firstName = recipient.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? recipient.Name;
        string subject = Replace(options.Subject, recipient, firstName, bookTitle, edition);
        string body = Replace(options.Body, recipient, firstName, bookTitle, edition);
        string greeting = options.Greeting switch
        {
            GreetingStyle.Hi => "Hi", GreetingStyle.GoodMorning => "Good morning", GreetingStyle.GoodAfternoon => "Good afternoon",
            GreetingStyle.GoodEvening => "Good evening", GreetingStyle.Howdy => "Howdy", _ => "Hello"
        };
        return (subject, $"{greeting} {firstName},\r\n\r\n{body}");
    }

    private static string Replace(string value, Recipient r, string firstName, string title, string edition) => value
        .Replace("{firstName}", firstName, StringComparison.OrdinalIgnoreCase)
        .Replace("{name}", r.Name, StringComparison.OrdinalIgnoreCase)
        .Replace("{bookTitle}", title, StringComparison.OrdinalIgnoreCase)
        .Replace("{edition}", edition, StringComparison.OrdinalIgnoreCase)
        .Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    private static string EncodeHeader(string value) => "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    private static void WriteBase64(TextWriter writer, byte[] data)
    {
        string encoded = Convert.ToBase64String(data);
        for (int i = 0; i < encoded.Length; i += 76) writer.WriteLine(encoded.Substring(i, Math.Min(76, encoded.Length - i)));
    }
}
