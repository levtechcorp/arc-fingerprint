# ARC Fingerprint 1.1.0

Released October 4, 2026.

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

- The source builds with .NET 8 Windows Forms without third-party package dependencies.
- The 1.1.0 Windows x64 ZIP is portable and does not require Visual Studio. It requires the .NET 8 Desktop Runtime, which is not bundled.
- 38 automated core tests from the 1.0.0 release cover generation, mappings, font preservation, validation failures, and cancellation.
- EPUBCheck and ebook-reader visual validation were not performed for this release.

The structural checker is not a complete EPUB conformance checker. Fingerprints are removable. DRM, unsupported encryption, signed archives, and multiple-rendition EPUBs are unsupported. Existing generated EPUBs and mapping CSVs remain compatible.
