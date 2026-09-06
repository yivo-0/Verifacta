# Security

Verifacta parses files that arrive from outside your organisation. An invoice is
sent to you by a supplier, and a supplier can be anyone. The library is written on
that assumption, and reports about where it fails to hold are welcome.

## Reporting a vulnerability

Use [private vulnerability reporting](https://github.com/yivo-0/Verifacta/security/advisories/new),
or email **oleganickij02@gmail.com**. Please do not open a public issue for
anything that could be exploited.

Expect an acknowledgement within three working days and an assessment within
seven. If a fix is warranted it goes into the next release, with a GitHub
Security Advisory naming the affected versions.

## What counts

A malformed or hostile document should be **rejected**, not survived by accident.
Anything below is in scope:

- a document that terminates the process — a stack overflow from recursive
  structure, an unbounded allocation, an unhandled exception escaping
  `InvoiceDocument.Load` or `InvoiceValidator.Validate` as anything other than
  `UnsupportedDocumentException` or `ValidationException`
- a document that costs disproportionately more to read than its size suggests
- any network request made by the library outside `RulePackCatalog.RestoreAsync`
- reading a file from disk or a URL that the document names — XXE, schema
  location hijacking, or anything else that follows a reference out of the file
- a rule artefact that has been altered on disk being used to produce a verdict
- a verdict that disagrees with the publishers' own artefacts

The last one is a correctness bug rather than a vulnerability, but it matters
enough here to be worth the same channel.

## What does not

- **A wrong verdict caused by an out-of-date rule pack.** Bump the pack.
- **Denial of service by volume.** Validation is CPU-bound by design; bound the
  concurrency in your host. `DocumentLimits` bounds a single document, not a
  queue.
- **Vulnerabilities in the rule artefacts themselves.** These are published by
  CEN, OpenPEPPOL and KoSIT and are not redistributed here. Report those upstream.
- **PDF/A-3 container conformance.** Not checked, by design — see issue #12.

## Existing hardening

Since the input is untrusted, some of this is worth stating plainly:

| | |
|---|---|
| DTD processing | prohibited outright on invoices; XML entity expansion cannot occur |
| External resolution | `XmlResolver` is null everywhere; nothing follows a reference out of the document |
| Recursive structure | the PDF embedded-file tree is walked iteratively, with a visited set and a node cap |
| Attachment size | enforced during decompression for Flate, so a compression bomb is abandoned rather than allocated |
| Input size | enforced as bytes arrive, not after buffering |
| Network | only `RestoreAsync`, only to the pinned upstream releases, over HTTPS |
| Artefact integrity | every pack is checked against the SHA-256 in the embedded manifest before first use |

## Supported versions

While the package is at `1.0.0-preview`, fixes go into the next preview. Once
`1.0.0` ships, the latest minor of the current major receives security fixes.
