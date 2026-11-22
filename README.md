# ARC Fingerprint

**Make a personal EPUB or PDF copy for every reader—without sending your manuscript to a service.**

ARC Fingerprint is a simple Windows app for authors and small publishing teams. Add your readers, choose ARC, Beta, or Alpha, and create individually fingerprinted files ready to email.

[**Download for Windows**](https://github.com/levtechcorp/arc-fingerprint/releases/latest) · [View all releases](https://github.com/levtechcorp/arc-fingerprint/releases)

## How it works

1. Choose your master EPUB or PDF.
2. Add readers by importing a CSV or typing names and email addresses.
3. Choose **ARC**, **Beta**, or **Alpha** and generate the copies.
4. Email each reader their matching copy, or turn on **Email drafts** to create a separate personalized `.eml` file for each reader.

Need to add someone later? Choose **Add to existing batch** and select its private mapping plus the original clean master file. ARC Fingerprint checks the master and existing copies before appending the new reader and copy. This also works with a consolidated collection: choose a clean master already represented in it, then choose ARC, Beta, or Alpha if that master appears under more than one edition. Existing fingerprint IDs stay unchanged. Use **Manage existing batch** to check all files, correct a reader's details, recoverably remove a reader, or recreate missing send folders.

Have several project folders for the same book? Choose **Merge projects** and select their parent folder. The app creates a new consolidated collection with one private recipient list and current send folders. It preserves the existing fingerprint IDs and ebook bytes, upgrades older mappings where possible, and leaves the source folders untouched. When the same ID is present more than once with identical reader and copy data, it keeps one entry; conflicting records stop the merge for review. If a copy's current hash differs from its saved mapping, the app checks its embedded ID and file structure, then asks before including it; the merged report records both hashes. Older mappings without a master hash remain marked as unverifiable against the original master. Existing `.eml` drafts are carried over, and the app updates their attached-book filename when it renames a copy. It does not combine the source batches' email templates.

Files are named so they're easy to pick out of your batch, for example:

**Island Fortune - Beta - Jane Reader - 001-A1B2C3D4.pdf**

## Everything you need, in one batch

- A uniquely fingerprinted EPUB or PDF for each reader.
- A private recipient list that matches each copy to its reader.
- A quick inspection tool to look up who a copy was prepared for.
- Search a folder of past batches to identify a suspected leaked EPUB or PDF, with an exact-file hash check to distinguish an unchanged copy from a modified one.
- Optional fingerprint channels include per-page PDF IDs for excerpt lookups, visible PDF page or cover marks, an EPUB stylesheet signal with tiny spacing variation, repeated EPUB IDs for recovery, and reviewed local thesaurus substitutions.
- Optional individual email drafts with a greeting, personalized subject and message, the matching book attached, and any extra files you choose.
- Add a new reader to an existing batch while preserving its existing copies and recipient mapping.
- A review of each email's recipient, message, exact book filename, attachments, and draft size before the batch is finalized.
- A **Send-to-readers** folder for every reader, containing only their book copy and email draft when drafts are enabled.
- Saved email wording and private copies of shared attachments, so drafts can be made later when adding a reader.
- Batch integrity checks for mapped copies, fingerprints, email drafts, and send folders.
- Safe name or email corrections; removing a reader moves their files to **Removed readers** for recovery.
- Merge separate project batches into a consolidated collection without changing already-issued IDs or ebook bytes.

The app keeps your book and reader list on your computer. It does not upload your EPUB, connect to an account, or send email. Email drafts are ordinary `.eml` files; mail apps vary in how they open or import them, so review each message before sending. Your original master file stays unchanged.

**Keep the whole batch folder private.** It contains the recipient mapping, saved email text, and any shared attachment copies. Send readers files from their individual **Send-to-readers** folders, not the whole batch folder.

## Get started

Download the Windows ZIP above, extract it, and open **ArcFingerprint.exe**. Visual Studio is not needed. If Windows asks for it, install the **.NET 8 Desktop Runtime**; it is not included in the ZIP.

You can enter readers one at a time or import a CSV with **Name** and **Email** columns. [Download the example CSV](recipients-example.csv).

## A note about fingerprints

Each EPUB gets a unique ID in its package metadata and can repeat it in XHTML comments for recovery. Each PDF gets a unique ID in its document metadata; optional page-level IDs help recognize excerpts. Additional EPUB stylesheet and thesaurus channels are opt-in. The thesaurus can change wording and does not understand context, so review those copies before sending. Optional visible marks can go on every PDF page or only its first page. The **Inspect a copy** tool searches private mappings and reports whether the file hash still matches.

Fingerprints can be removed, and conversion or editing may strip them. A match points to a copy you prepared; it does not prove who shared it. ARC Fingerprint is a tracking aid, not DRM.

The app supports standard EPUB 2/3 books and ordinary PDFs. Leak lookup depends on fingerprints and private mappings still being present; conversion or editing may remove them. A match identifies the copy that was prepared, not who shared it. DRM/encrypted files, signed EPUBs or PDFs, multi-rendition EPUBs, and PDFs with interactive forms are not supported. PDF/A conformance and the visual layout of PDFs are not validated; check generated copies before sending them.

PDF generation uses [PDFsharp](https://www.nuget.org/packages/PDFsharp/), an MIT-licensed .NET library. It runs locally with the app; no PDF service or account is used.

## For developers

The source code and Visual Studio solution are in this repository. To build the app, see [START-HERE.md](START-HERE.md). Visual Studio restores the PDFsharp dependency from NuGet when building.

