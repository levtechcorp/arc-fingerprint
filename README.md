# ARC Fingerprint 1.0.0

For everyday use, download the current Windows ZIP from the repository's **Releases** page. For source builds, start with [START-HERE.md](START-HERE.md); Visual Studio is only needed to edit or rebuild the app.

A small, local Windows desktop app for making individually fingerprinted EPUB advance reader copies. The source is a Visual Studio solution using C# / .NET 8 Windows Forms. There are no third-party package dependencies, accounts, subscriptions, network calls, or email sending in the app.

## Start the app

From a Windows release ZIP, open `Ready-to-run/ArcFingerprint.exe` and keep the other files in that folder beside it. This build requires the .NET 8 Desktop Runtime.

1. Choose an **unmarked master EPUB**. The app checks its structure before accepting it.
2. Choose a folder for your batches. A new uniquely named subfolder is created for every run.
3. Import a UTF-8 CSV with `Name,Email` headers, or type names and emails into the table. An example CSV is included. Import replaces the current list. Select a row and press Delete to remove a reader.
4. Click **Generate ARC copies**. Every output is reopened and checked before the completed batch becomes available.
5. Click **Open last batch**. Use `PRIVATE-recipient-mapping.csv` to match each reader to their file. Email only the appropriate EPUB, never the mapping or whole batch folder.

The original master is opened read-only and never changed. Recipient names and emails are stored only in the private mapping, not embedded into the EPUB. Use a local, non-synced output folder if you also want to avoid your own cloud-sync software copying the files. The mapping is plain text, not encrypted; keep a backup somewhere private.

## What each batch contains

- One EPUB per reader, with a random 128-bit `ARC-…` ID.
- `PRIVATE-recipient-mapping.csv`: ID, reader name, email, filename, output SHA-256, master SHA-256, and UTC creation time. Potential spreadsheet formulas are prefixed with an apostrophe for safe opening in Excel.
- `validation-report.json`: the master hash, counts, time, and description of the checks performed.

Filenames use sequence numbers and part of the random ID. No names or email addresses appear in filenames. Each rerun creates new IDs and a new mapping; retain every mapping you need.

## Fingerprints and limitations

Each copy receives an extra `dc:identifier` value in the OPF metadata and an XML comment after the root element of every manifest XHTML document. Comments are invisible during normal reading. Original XHTML bytes, images, styles, navigation files, and the book's primary identifier are preserved; the OPF is reserialized, and EPUB 3 modification metadata is updated. Visible wording is not changed.

These marks are **removable**. Conversions or sanitizers may strip both metadata and comments. This is traceability, not DRM or protection against piracy. A matching ID associates a file with a distributed copy; it does not establish who uploaded it or rule out forwarding or an account compromise.

**Inspect a copy** now looks up the metadata ID in your private mapping and shows the assigned reader's name and email, original filename, creation time, mapping source, and whether the file's SHA-256 matches the saved copy. It tries PRIVATE-recipient-mapping.csv beside the EPUB, then the last successfully used mapping and the latest generated batch from the current session. If no match is found, use **Choose mapping CSV…** in the result window. You can also choose a different mapping after a match. Lookups use the complete ID, so renaming the EPUB does not break identification. Wrong-batch files show no match; duplicate IDs or malformed mappings are rejected. Nothing is uploaded, and mapping locations are remembered only until the app closes. Existing generated mappings work without regenerating your copies. Inspection uses the same structural checks and is intended for intact EPUBs. If metadata is gone, inspect the XHTML comments manually in an EPUB editor and search for `ARC-FINGERPRINT:`. A missing ID is inconclusive. The app does not perform forensic recovery or automatically accuse a recipient.

## Validation safeguards

The app verifies the first, uncompressed `mimetype` entry; container/package availability; one rendition; required basic metadata; manifest files and IDs; spine references; EPUB 2 navigation reference or EPUB 3 navigation declaration; and XML well-formedness for XHTML, SVG, and NCX resources. XML network resolution is disabled. It rejects unsafe or duplicate archive paths and already fingerprinted masters.

Limits: 256 MB compressed input and expanded archive; 64 MB per entry; 10,000 archive entries; 1,000 recipients per batch. Only EPUB 2.0 and 3.x packages with XHTML content and local manifest resources are supported. Standard IDPF and Adobe font obfuscation are supported: font bytes, encryption.xml, and original book identifiers are preserved unchanged. The app verifies that every obfuscation reference points to an existing manifest font. DRM, unknown encryption methods, digitally signed EPUBs, and multiple-rendition EPUBs are rejected.

Every copy is structurally checked again, and its metadata ID and every XHTML comment are verified. Generation uses a private staging folder and publishes the batch only after all copies and the mapping are complete. Cancellation or failure attempts to remove the staging folder. A power loss, forced termination, or cleanup failure can leave an `.arc-incomplete-…` folder; do not distribute it.

**This is not full EPUB conformance validation.** It does not validate every link, CSS rule, accessibility requirement, schema constraint, or reading-system rendering. Built-in checks do not imply that EPUBCheck passed. Open a generated copy in your ebook reader before distribution.

For full conformance checking, install the official [W3C EPUBCheck](https://www.w3.org/publishing/epubcheck/docs/installation/) distribution and Java, then run the included optional helper:

```powershell
.\Validate-WithEpubCheck.ps1 -EpubCheckJar 'C:\Tools\epubcheck\epubcheck.jar' -EpubPath 'C:\Books\ARC-batch'
```

It checks every EPUB directly inside the selected folder (or a single EPUB), writes separate logs into a new report folder, and returns a failure if a checker invocation returns a nonzero status. Review warnings as well. EPUBCheck and Java are not bundled or downloaded by this app. See [official running instructions](https://www.w3.org/publishing/epubcheck/docs/running/). The archive-writing rules are based on the [W3C EPUB specification](https://www.w3.org/TR/epub-33/).

## Open in Visual Studio Community

Open `ArcFingerprint.sln`, set **ArcFingerprint.App** as the startup project, and press F5. Use Visual Studio with the **.NET desktop development** workload and .NET 8 targeting support. The solution was built on this machine with SDK 10.0.302 targeting .NET 8.

Projects:

- `ArcFingerprint.App`: Windows Forms interface.
- `ArcFingerprint.Core`: recipient parsing, EPUB checks, fingerprinting, and transactional batch generation.
- `ArcFingerprint.Tests`: a dependency-free console test suite using synthetic EPUBs.

From the solution folder:

```powershell
dotnet build ArcFingerprint.sln -c Release
dotnet run --project ArcFingerprint.Tests -c Release
dotnet publish ArcFingerprint.App -c Release --self-contained false -o Ready-to-run
```

The tests cover a 15-reader batch, unique IDs, unchanged source and content bytes, private mappings, duplicate recipients, EPUB 2/3, UTF-16, malformed XML, entity resolution, unsafe paths, cancellation cleanup, and rejected unsupported formats. Test fixtures go in a fresh temporary directory by default. The suite now has 38 passing tests, including IDPF/Adobe font preservation and rejection of actual or mixed encryption. The font-support fix was also checked on the supplied manuscript: 77 XHTML documents, all 12 obfuscated fonts preserved byte-for-byte, and the original master and identifiers unchanged. EPUBCheck was not run because Java and EPUBCheck were not available in this environment.



