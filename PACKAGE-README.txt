ARC Fingerprint 2.1.2

GET STARTED
Extract the complete ZIP, then double-click ArcFingerprint.exe. Keep the other files in the extracted folder. This build requires the .NET 8 Desktop Runtime.

Choose an EPUB or PDF master, add readers, select ARC, Beta, or Alpha, then generate the copies. The optional "Visible mark on PDF pages" adds a small footer ID to every page; it is off by default. Use "Inspect a copy" to look up a copy in the private recipient mapping.

PDF NOTES
PDF fingerprints are stored in document metadata. The optional visible mark improves persistence but can still be removed. Password-protected/encrypted PDFs, digitally signed PDFs, and PDFs with interactive forms are not supported. PDF/A conformance and visual layout are not validated; review copies before sending.

PRIVACY
The app processes files locally and never sends email. Keep PRIVATE-recipient-mapping.csv private and send each reader only their own file. A fingerprint identifies a copy prepared for a reader; it does not prove who shared it.

PDFsharp 6.2.4 is included under the MIT license. See THIRD-PARTY-NOTICES.txt.

When merging, if a mapped book copy no longer matches its saved hash, the app asks before continuing. The embedded reader fingerprint and EPUB/PDF structure must still validate; the merged validation-report.json records both hashes.

To add a reader to a consolidated collection, choose its private mapping and a clean master already represented in that collection. If the master appears in more than one edition, choose ARC, Beta, or Alpha. Existing fingerprint IDs remain unchanged.
