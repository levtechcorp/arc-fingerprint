# ARC Fingerprint 2.1.2

## Added

- Optional one-to-one `.eml` email draft files, each addressed to its reader and containing that reader's matching EPUB or PDF copy.
- Custom greeting, subject and message text with `{firstName}`, `{name}`, `{bookTitle}`, and `{edition}` placeholders.
- Optional shared attachments added to each personalized draft. Drafts are created locally; the app never sends email.
- Draft messages are validated for safe headers and included only when enabled.
- Add a reader to an existing EPUB or PDF batch using its private mapping and original clean master; the source hash and existing mapped copies are checked before the batch is extended.
- Existing recipient rows are preserved, the new copy uses the next sequence number, and the mapping and batch report are updated.
- Email templates and private copies of shared attachments are saved with the batch for use after restarting the app.
- Review every recipient, subject, message, attached book filename, shared attachment, and message size before a batch is finalized; cancelling review discards the staged batch.
- Create reader-specific send folders containing only that reader's book copy and email draft.
- Check a complete batch against its private mapping, file hashes, embedded fingerprints, saved email settings, and send folders; missing send folders can be recreated.
- Correct a reader's name or email and regenerate their matching email draft without changing the book fingerprint.
- Remove a reader from the active mapping and move their book, draft, and send folder under `Removed readers` for recovery.
- Search a chosen folder of batch mappings for fingerprints in a suspected EPUB or PDF, including multi-ID EPUBs; show all matching readers and whether the leaked file hash exactly matches a generated copy.
- Optional PDF page-level identifiers, first-page visible mark, repeatable XHTML recovery IDs, EPUB stylesheet micro-typography signal, and local thesaurus wording variations.
- Merge multiple projects into a fresh normalized collection, preserving issued ARC IDs and book bytes while rebuilding mapping rows, hashes, reports, and send folders; supports older mapping schemas and flags unavailable master hashes.
- If a saved copy hash no longer matches the current EPUB/PDF, validate its embedded fingerprint and structure, then ask before proceeding; the report records both hashes and the current bytes are preserved.
- Add a new reader directly to a consolidated collection by choosing a represented clean master; select an edition if that master appears in multiple editions. Existing copy IDs remain unchanged, and the merged report is updated.

## Verification

- 59 automated tests pass, including adding a reader to a consolidated multi-edition collection, changed-hash merge confirmation and reporting, legacy/current project merging, ID and byte preservation, duplicate-safe re-merging, optional PDF/EPUB channels, thesaurus wording changes, XHTML fallback identification, leak lookup and exact-hash evidence, draft review cancellation, saved settings, send folders, batch checks, correction/removal, and extending EPUB/PDF batches safely.

---

# ARC Fingerprint 1.2.0 (in development)

## Added

- PDF masters can now be fingerprinted in the same recipient batches as EPUBs.
- PDF copies store a per-reader ID in document metadata and are supported by copy inspection and private mapping lookup.
- Optional small footer fingerprint on every PDF page; disabled by default.
- PDF files are reopened and their IDs, page counts, and hashes are checked before a batch is published.
- Encrypted/password-protected PDFs, digitally signed PDFs, and PDFs with interactive forms are declined to avoid unsupported or invalidated files.
- PDFsharp 6.2.4 is used under its MIT license.

## Verification so far

- Release solution build succeeds with no compiler warnings.
- 41 automated tests pass, including PDF copy generation, unchanged masters, ID-to-reader mapping, optional page marks, and rejection of signed PDFs.
- PDF/A conformance and visual appearance are not validated automatically; review generated PDFs before distribution.

---

# ARC Fingerprint 1.1.1

Released October 5, 2026.

## Included

- Windows desktop interface with CSV import and editable reader list.
- ARC, Beta, and Alpha copy-stage selection with a live filename preview.
- Email-friendly EPUB filenames with sanitized book and recipient names; no email addresses in filenames.
- Refreshed desktop layout, typography, and recipient table styling.
- Fixed the bottom status and validation text being clipped at common display scales.
- Random per-copy IDs in EPUB metadata and hidden XHTML comments.
- Private recipient mappings with file hashes and batch reports.
- Copy stage recorded in each mapping and validation report.
- Recipient lookup by full ID, automatic nearby-mapping detection, and manual mapping selection.
- File-hash comparison, including identification of renamed copies.
- EPUB 2/3 structural safeguards, protected master files, and batch cancellation/cleanup.
- Preservation of standard IDPF and Adobe obfuscated fonts.
- Visual Studio solution, source code, example CSV, test suite, and optional EPUBCheck helper.

## Verification

- 38 automated core tests pass, covering generation, mappings, font preservation, validation failures, and cancellation.
- Recipient-result logic checked for displayed name, email, mapping source, and file-match status.
- Main window launch and generation previously checked through the interface.
- Supplied manuscript successfully processed: 77 XHTML documents; all 12 obfuscated fonts and the original publication identifiers preserved; master unchanged.
- Release build targets .NET 8 Windows Forms without third-party package dependencies.

Full EPUBCheck and ebook-reader visual validation were not performed. The structural checker is not a complete EPUB conformance checker. Fingerprints are removable. DRM, unsupported encryption, signed archives, and multiple-rendition EPUBs are unsupported.

## Updating from the development build

Close the old ARC Fingerprint window before opening the final executable. Existing generated EPUBs and mapping CSVs remain compatible. Visual Studio users can reopen the solution or rebuild it; select **ArcFingerprint.App** as the startup project.
