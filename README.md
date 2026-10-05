# ARC Fingerprint

**Make a personal EPUB copy for every reader—without sending your manuscript to a service.**

ARC Fingerprint is a simple Windows app for authors and small publishing teams. Add your readers, choose ARC, Beta, or Alpha, and create a set of individually marked EPUBs ready to email.

[**Download for Windows**](https://github.com/levtechcorp/arc-fingerprint/releases/latest) · [View all releases](https://github.com/levtechcorp/arc-fingerprint/releases)

## How it works

1. Choose your master EPUB.
2. Add readers by importing a CSV or typing names and email addresses.
3. Choose **ARC**, **Beta**, or **Alpha** and generate the copies.
4. Email each reader their matching EPUB.

Files are named so they're easy to pick out of your batch, for example:

**Island Fortune - Beta - Jane Reader - 001-A1B2C3D4.epub**

## Everything you need, in one batch

- A uniquely fingerprinted EPUB for each reader.
- A private recipient list that matches each copy to its reader.
- A quick inspection tool to look up who a copy was prepared for.

The app keeps your book and reader list on your computer. It does not upload your EPUB, connect to an account, or send email. Your original master file stays unchanged.

**Keep the recipient list private.** Send each reader only their own EPUB, not the mapping file or the whole batch folder.

## Get started

Download the Windows ZIP above, extract it, and open **ArcFingerprint.exe**. Visual Studio is not needed. If Windows asks for it, install the **.NET 8 Desktop Runtime**; it is not included in the ZIP.

You can enter readers one at a time or import a CSV with **Name** and **Email** columns. [Download the example CSV](recipients-example.csv).

## A note about fingerprints

Fingerprints can help you identify which reader copy a file came from, but they can be removed—especially if an EPUB is converted. A match points to a copy you prepared; it does not prove who shared it. ARC Fingerprint is a tracking aid, not DRM.

DRM-protected books are not supported. The app works with standard EPUB 2 and EPUB 3 books.

## For developers

The source code and Visual Studio solution are in this repository. To build the app, see [START-HERE.md](START-HERE.md).
