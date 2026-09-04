# Verifacta

Read and validate EN 16931 electronic invoices in .NET — **no Java, no Node.js, no native
dependencies, and nothing leaves your server.**

Every other way to validate a European e-invoice from .NET today needs a JVM sidecar, a Node
process, or an upload to someone else's API. Verifacta runs the official Schematron in-process on
pure managed .NET.

```bash
dotnet add package Verifacta
```

```csharp
using Verifacta;
using Verifacta.Validation;

// Compile the rule packs once and keep the validator: compiling takes seconds, validating takes
// milliseconds. It is thread-safe.
var validator = new InvoiceValidator();

var document = InvoiceDocument.Load("invoice.xml");   // or a Factur-X / ZUGFeRD PDF
var result = validator.Validate(document);            // rule set chosen from the invoice itself

foreach (var error in result.Errors)
{
    Console.WriteLine($"{error.RuleId} {string.Join(" ", error.BusinessTerms)} at {error.Location}");
    Console.WriteLine($"  {error.Message}");
}
```

```
BR-CO-15 BT-112 BT-109 at /ubl-invoice:Invoice/cac:LegalMonetaryTotal/cbc:TaxInclusiveAmount
  Invoice total amount with VAT (BT-112) = Invoice total amount without VAT (BT-109) + Invoice total VAT amount (BT-110)
```

## What it does

| | |
|---|---|
| **Detect** | UBL 2.1 Invoice and CreditNote, CII D16B, and the XML inside a hybrid Factur-X / ZUGFeRD 2.x PDF. Identifies the profile: XRechnung, Peppol BIS Billing 3, Factur-X (with level), plain EN 16931. |
| **Read** | One canonical `EInvoice` model regardless of syntax — parties, addresses, contacts, delivery, payment means, allowances and charges, VAT breakdown, all ten total fields, and lines with prices, item identifiers and attributes. Tolerant: a malformed date or amount becomes a finding, never an exception. |
| **Validate** | XML Schema, then the EN 16931 business rules, then the national CIUS — the same artefacts in the same order as the German reference validator. Stops at the schema when the structure is wrong, because rules over a broken document give misleading verdicts. |

Findings carry the rule id, severity, the raw SVRL flag, a readable location path, the failing XPath
test, the BT/BG business terms from the message, and which artefact produced them — so you can tell
a CEN core rule from a German CIUS rule from a Peppol national rule.

## Rule packs

The rule artefacts are **not committed**: the CEN rules are EUPL-1.2 and the KoSIT configurations
are Apache-2.0, so they are fetched on demand and pinned to an upstream release.

```bash
python tools/fetch-rules.py     # rule packs and XML Schemas
python tools/fetch-corpus.py    # official test corpora (needed only to run the tests)
```

| Pack | Version | Source | Licence |
|---|---|---|---|
| `schemas` | 2026-08-31 | UBL 2.1 + CII D16B as redistributed by KoSIT | Apache-2.0 |
| `cen-en16931` | 1.3.16 | ConnectingEurope/eInvoicing-EN16931 | EUPL-1.2 |
| `peppol-bis` | 3.0.21 | itplr-kosit/validator-configuration-bis | Apache-2.0 |
| `xrechnung` | 3.0.2 | itplr-kosit/validator-configuration-xrechnung | Apache-2.0 |

`rules/manifest.json` records the release tag, licence and SHA-256 of every artefact.
`RulePackCatalog.VerifyIntegrity()` checks them before use.

## Command line

```bash
verifacta validate invoice.xml
verifacta validate *.xml --rules xrechnung --json
verifacta info invoice.pdf
```

Exit codes: `0` valid, `1` validation errors, `2` the file could not be processed, `64` usage error.

## Verified against

883 tests, run in about 15 seconds.

- **The CEN rule suite, rule by rule.** All 309 official test files, every `<success>` and `<error>`
  expectation. This is the oracle: it proves agreement with CEN per rule, not just overall.
- **Published instances.** 70 of 72 KoSIT XRechnung business cases and 10 of 12 Peppol examples
  validate without errors. Every exception is a defect in the published example, verified by hand
  against the rule expression and documented in the test suite.
- **Real hybrid PDFs**, including a PDF/A-4 variant, a `zugferd-invoice.xml` attachment, an
  encrypted PDF, one with no attachment, and a truncated one.

Four defects in published upstream examples found and documented: a KoSIT business case with a
transposed digit (`BT-112` 336.90 vs `BT-115` 366.86), both Greek Peppol examples violating the
UBL 2.1 element sequence, and a German digital-health invoice whose `schemeID="XR03"` is required
by XRechnung 3.0.2 but rejected by the CEN 1.3.16 code-list rules — a genuine conflict between the
two specifications.

## Not in scope

Writing or generating invoices, Peppol network transport, and ZUGFeRD 1.x (legally superseded —
Germany requires 2.0.1 or later; v1 files are detected and rejected with a clear message).

## Licence

[Business Source License 1.1](LICENSE). In short:

- **Free for production use** if your organisation's annual revenue is under €1,000,000, or if you
  ship Verifacta inside OSI-licensed open source, regardless of revenue.
- **Free for any non-production use** — evaluation, development, CI, internal testing.
- Above that threshold, a commercial licence is required.
- On **2030-09-04** this version becomes Apache 2.0 automatically, so there is no lock-in if the
  project stops being maintained.

Third-party components and the licences of the validation artefacts are listed in [NOTICE](NOTICE).
Verifacta depends on [SaxonCS-HE](https://www.nuget.org/packages/SaxonCS-HE) (Saxonica, MPL-2.0) for
XSLT 3.0 and [PDFsharp](https://www.nuget.org/packages/PDFsharp) (empira, MIT) for PDF attachments.
Both are pure managed. The rule artefacts themselves are never redistributed — they are fetched from
their publishers and stay under their own terms.

## Requirements

.NET 8 or .NET 10. Python 3 and the GitHub CLI are needed only to run the fetch scripts.
