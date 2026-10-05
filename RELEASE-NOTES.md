# ARC Fingerprint 1.1.0

Feature update in review.

## Included

- Windows desktop interface with CSV import and editable reader list.
- ARC, Beta, and Alpha copy-stage selection with a live filename preview.
- Email-friendly EPUB filenames with sanitized book and recipient names; no email addresses in filenames.
- Refreshed desktop layout, typography, and recipient table styling.
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
