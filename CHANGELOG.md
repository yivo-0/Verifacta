# Changelog

Notable changes, newest first. This project follows [Semantic Versioning](https://semver.org).

Rule pack versions are called out separately from library versions, because a
pack bump can change the verdict on an invoice that previously passed while the
library itself is unchanged.

## Unreleased

### Added

- Two long-form articles under `docs/`.
- `SECURITY.md`, `CONTRIBUTING.md`, a code of conduct, issue templates and a
  package icon.

## 1.0.0-preview.1

First package. Reads, validates and renders EN 16931 invoices with no JVM, no
Node process and no network call outside `rules restore`.

### Rule packs

| Pack | Version | Source |
|---|---|---|
| `schemas` | 2026-08-31 | UBL 2.1 + CII D16B as redistributed by KoSIT |
| `cen-en16931` | 1.3.16 | ConnectingEurope/eInvoicing-EN16931 |
| `peppol-bis` | 3.0.21 | itplr-kosit/validator-configuration-bis |
| `xrechnung` | 3.0.2 | itplr-kosit/validator-configuration-xrechnung |
| `visualization` | 3.0.2 | itplr-kosit/xrechnung-visualization |

### Added

- `InvoiceDocument` reads UBL 2.1 Invoice and CreditNote, CII D16B, and the XML
  embedded in a hybrid Factur-X or ZUGFeRD 2.x PDF.
- One canonical `EInvoice` model across both syntaxes. A malformed date or amount
  becomes a finding rather than an exception.
- `InvoiceValidator` runs XML Schema, then the EN 16931 rules, then the national
  CIUS, in the order the German reference validator uses. Findings carry the rule
  id, severity, SVRL flag, location, failing XPath test, business terms, the
  offending value where the rule pointed at one, and the artefact that produced
  them.
- `InvoiceRenderer` produces a self-contained HTML page from KoSIT's official
  XRechnung visualisation, in German or English.
- `RulePackCatalog` restores artefacts from pinned upstream releases and verifies
  each against the manifest SHA-256 before first use.
- `DocumentLimits` bounds input size, attachment size and PDF name-tree traversal.
- `verifacta` command line: `rules restore` / `rules verify`, `validate`,
  `render`, `info`. Folder input, CSV and JSON output, `--strict`, `--parallel`,
  and Ctrl+C reporting what finished.
- `ValidationResult.SchemaChecked` and `ProfileCovered`, so a verdict says what it
  actually covered.

### Verification

- All 309 CEN rule-test files replayed, every `<success>` and `<error>`
  expectation, proving agreement per rule rather than in aggregate.
- 100 of 100 comparable files agree rule for rule with KoSIT's validationtool
  v1.6.3 on every CI run.
- 956 tests on net8.0 and net10.0.
- Four defects found in published upstream examples, documented in the test suite.
