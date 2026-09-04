Read and validate EN 16931 e-invoices in .NET without Java or Node

Verifacta detects, reads and validates European electronic invoices in pure
managed .NET. Every existing option for .NET requires a JVM sidecar, a Node
process, or uploading the invoice to a third-party API.

- Detect: UBL 2.1 Invoice/CreditNote, CII D16B, and the XML embedded in a
  hybrid Factur-X / ZUGFeRD 2.x PDF; classifies XRechnung, Peppol BIS 3,
  Factur-X and plain EN 16931 profiles.
- Read: one canonical EInvoice model for both syntaxes, tolerant of malformed
  values (they become findings, never exceptions).
- Validate: XML Schema, then EN 16931 Schematron, then the national CIUS --
  the same artefacts in the same order as KoSIT's reference scenarios.

Rule artefacts are fetched on demand rather than committed: the CEN rules are
EUPL-1.2 and the KoSIT configurations Apache-2.0. rules/manifest.json pins
each pack to an upstream release tag and records a SHA-256 per artefact.

Verified by 883 tests: the full CEN rule suite replayed expectation by
expectation, the published KoSIT and Peppol instances, and real hybrid PDFs
including PDF/A-4, encrypted, attachment-less and truncated files. Four
defects in the published upstream examples were found and are documented in
the suite.
