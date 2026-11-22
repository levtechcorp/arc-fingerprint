# ARC Fingerprint 1.1.1

## Open the app

If you downloaded the ZIP, extract the entire package first. Open **Ready-to-run**, then double-click **ArcFingerprint.exe**. Keep the files in that folder together. Visual Studio does not need to be open.

This release uses the .NET 8 Desktop Runtime already installed on your computer.

## Make reader copies

1. **Choose file…** — select the original EPUB or PDF master book.
2. **Choose folder…** — choose where to save the batch.
3. Import your reader CSV (columns **Name** and **Email**) or enter readers in the table. The included example contains fictional readers; replace them before making real copies.
4. Choose **ARC**, **Beta**, or **Alpha** under **Copy stage**. Check the filename preview, then click **Generate copies** and **Open last batch**.
5. Use the private mapping to attach the correct file to each reader's email. Keep the mapping private and backed up.

For PDF masters, the optional **Visible mark on PDF pages** checkbox adds a small fingerprint footer to every page. It is off by default. PDF IDs are also stored in document metadata so **Inspect a copy…** can look them up in the mapping.

## Identify an existing copy

Click **Inspect a copy…** and select an EPUB or PDF. The app finds its reader automatically if the matching **PRIVATE-recipient-mapping.csv** is beside it. Otherwise, click **Choose mapping CSV…** in the result window and select the saved mapping for that batch.

The result includes the reader's name and email, original filename, and whether the inspected file exactly matches the saved file hash. Older EPUB copies and mappings made with this app still work.

## What to keep

Filenames include the book title, stage, and recipient name to make email attachments easy to match. Keep the master and every batch's private mapping. Send only the assigned copy to each reader. The app never sends email and leaves the master untouched.

Fingerprints can be removed, especially during conversion. A lookup identifies an assigned copy, not proof of who shared it. PDF support rejects encrypted/password-protected files, digitally signed documents, and PDFs with interactive forms. PDF/A conformance and visual layout are not validated; open generated copies and review them before sending. EPUB structure checks are not a substitute for full EPUBCheck conformance validation.

For technical details and Visual Studio instructions, see **README.md**. Release verification is recorded in **RELEASE-NOTES.md**.

## Build from Visual Studio

Open **ArcFingerprint.sln**, set **ArcFingerprint.App** as the startup project, and press **F5**. The solution targets .NET 8; Visual Studio restores PDFsharp from NuGet during the build. The test suite uses synthetic EPUB and PDF files and never needs a reader's manuscript.
