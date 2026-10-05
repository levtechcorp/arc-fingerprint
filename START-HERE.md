# ARC Fingerprint 1.1.1

## Open the app

If you downloaded the ZIP, extract the entire package first. Open **Ready-to-run**, then double-click **ArcFingerprint.exe**. Keep the files in that folder together. Visual Studio does not need to be open.

This release uses the .NET 8 Desktop Runtime already installed on your computer.

## Make reader copies

1. **Choose EPUB…** — select the original master book.
2. **Choose folder…** — choose where to save the batch.
3. Import your reader CSV (columns **Name** and **Email**) or enter readers in the table. The included example contains fictional readers; replace them before making real copies.
4. Choose **ARC**, **Beta**, or **Alpha** under **Copy stage**. Check the filename preview, then click **Generate copies** and **Open last batch**.
5. Use the private mapping to attach the correct EPUB to each reader's email. Keep the mapping private and backed up.

## Identify an existing copy

Click **Inspect a copy…** and select an EPUB. The app finds its reader automatically if the matching **PRIVATE-recipient-mapping.csv** is beside it. Otherwise, click **Choose mapping CSV…** in the result window and select the saved mapping for that batch.

The result includes the reader's name and email, original filename, and whether the inspected EPUB exactly matches the saved file hash. Older copies and mappings made with this app still work.

## What to keep

Filenames include the book title, stage, and recipient name to make email attachments easy to match. Keep the master EPUB and every batch's private mapping. Send only the assigned EPUB to each reader. The app never sends email and leaves the master untouched.

Fingerprint marks can be removed, especially during conversion. A lookup identifies an assigned copy, not proof of who shared it. The app performs structural checks; full EPUBCheck conformance validation is separate.

For technical details and Visual Studio instructions, see **README.md**. Release verification is recorded in **RELEASE-NOTES.md**.
