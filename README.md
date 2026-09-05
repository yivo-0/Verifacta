# Verifacta

[![ci](https://github.com/yivo-0/Verifacta/actions/workflows/ci.yml/badge.svg)](https://github.com/yivo-0/Verifacta/actions/workflows/ci.yml)

Read and validate EN 16931 electronic invoices in .NET — **no Java, no Node.js, no native
dependencies, and nothing leaves your server.**

Every other way to validate a European e-invoice from .NET today needs a JVM sidecar, a Node
process, or an upload to someone else's API. Verifacta runs the official Schematron in-process on
pure managed .NET.

> **Not on NuGet yet** — the first package release is pending. Until then, clone and build:
>
> ```bash
> git clone https://github.com/yivo-0/Verifacta && cd Verifacta
> dotnet build && dotnet run --project src/Verifacta.Cli -- rules restore
> ```
>
> The `verifacta` examples below then read as `dotnet run --project src/Verifacta.Cli -- …`.

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
| **Render** | A received invoice as a self-contained HTML page, using KoSIT's official XRechnung visualisation — so what you show a user matches the reference rendering rather than an interpretation of it. German or English labels. |
| **Validate** | XML Schema, then the EN 16931 business rules, then the national CIUS — the same artefacts in the same order as the German reference validator. Stops at the schema when the structure is wrong, because rules over a broken document give misleading verdicts. |

Findings carry the rule id, severity, the raw SVRL flag, a readable location path, the failing XPath
test, the BT/BG business terms from the message, and which artefact produced them — so you can tell
a CEN core rule from a German CIUS rule from a Peppol national rule.

## Rule packs

The rule artefacts are **not committed**: the CEN rules are EUPL-1.2 and the KoSIT configurations
are Apache-2.0, so they stay under their publishers' terms and are downloaded on request.

```bash
verifacta rules restore    # or: RulePackCatalog.RestoreAsync()
verifacta rules verify
```

That needs nothing but the .NET SDK. The manifest is embedded in the assembly, the download is
plain HTTPS from the pinned upstream release, and every file is checked against its recorded
SHA-256 before it is written — a mismatch fails rather than being used. **Nothing else in Verifacta
touches the network**; validation is entirely local.

Set `VERIFACTA_RULES` to point at an artefact directory you manage yourself, for offline or
air-gapped deployments. `tools/fetch-rules.py` is the maintainer script for bumping the pinned
versions and regenerating the manifest; consumers never need it.

| Pack | Version | Source | Licence |
|---|---|---|---|
| `schemas` | 2026-08-31 | UBL 2.1 + CII D16B as redistributed by KoSIT | Apache-2.0 |
| `cen-en16931` | 1.3.16 | ConnectingEurope/eInvoicing-EN16931 | EUPL-1.2 |
| `peppol-bis` | 3.0.21 | itplr-kosit/validator-configuration-bis | Apache-2.0 |
| `xrechnung` | 3.0.2 | itplr-kosit/validator-configuration-xrechnung | Apache-2.0 |
| `visualization` | 3.0.2 | itplr-kosit/xrechnung-visualization | Apache-2.0 |

`src/Verifacta/RulePacks.json` records the release tag, licence and SHA-256 of every artefact.
`RulePackCatalog.VerifyIntegrity()` checks them before use.

### Staying current

CEN, OpenPeppol and KoSIT each publish roughly twice a year, and a national CIUS release can
change the verdict on invoices that previously passed. A [scheduled
workflow](.github/workflows/rule-updates.yml) checks every pinned pack against its upstream release
weekly and opens an issue when one falls behind, so the gap is visible rather than discovered by a
rejected invoice.

The bump itself is deliberately manual — the corpus reports are reviewed for changed verdicts
before a new pack version ships.

## Command line

```bash
verifacta validate invoice.xml
verifacta render invoice.pdf -o invoice.html
verifacta info invoice.pdf
```

Point it at a folder to find out how much of an existing archive an access point would reject:

```bash
verifacta validate ./invoices --recursive --csv report.csv
```

```
72 file(s): 70 valid, 2 with errors, 0 unreadable

Most frequent errors:
  BR-CL-10                     1 file(s)
  BR-CL-21                     1 file(s)
  BR-CO-16                     1 file(s)
```

The CSV carries one row per invoice — status, rule set, schema result, error and warning counts,
and the rule ids — so a large archive can be triaged in a spreadsheet. Validation runs in parallel
across cores; 521 files take about 12 seconds including the one-off rule-pack compilation.

`render` writes a self-contained page — CSS and script inlined, no network access needed — either
to stdout for a single invoice or beside each file for a folder. `--lang en` switches the labels.

Options: `--rules en16931|peppol|xrechnung`, `--recursive`, `--csv <file>`, `--json`, `--no-schema`,
`-o|--out <file|folder>`, `--lang de|en`.
Exit codes: `0` valid, `1` validation errors, `2` a file could not be processed, `64` usage error.

## Verified against

Every corpus source is pinned to a release tag or commit, so the verification is reproducible
rather than drifting with upstream. The live pass count is on the CI badge above.

- **The CEN rule suite, rule by rule.** All 309 official test files, every `<success>` and `<error>`
  expectation replayed. This is the oracle: it proves agreement with CEN per rule, not just overall.
- **Published instances.** 70 of 72 KoSIT XRechnung business cases and 10 of 12 Peppol examples
  validate without errors. Every exception is a defect in the published example, verified by hand
  against the rule expression and documented in the test suite.
- **Real hybrid PDFs** — 23 of them, including a PDF/A-4 variant, a `zugferd-invoice.xml`
  attachment, an encrypted PDF, one with no attachment, and a truncated one.

Run it yourself: `verifacta rules restore && python tools/fetch-corpus.py && dotnet test`
(the corpus fetch needs Python and the GitHub CLI; the library itself does not).
The suite writes `corpus/report.md`, `corpus/validation-report.md` and `corpus/pdf-report.md`,
which CI also uploads as artifacts on every run.

Four defects in published upstream examples found and documented: a KoSIT business case with a
transposed digit (`BT-112` 336.90 vs `BT-115` 366.86), both Greek Peppol examples violating the
UBL 2.1 element sequence, and a German digital-health invoice whose `schemeID="XR03"` is required
by XRechnung 3.0.2 but rejected by the CEN 1.3.16 code-list rules — a genuine conflict between the
two specifications.

Separately, the CEN suite ships test files for `BR-CO-25` while its own Schematron never implements
that rule. A test pins the exact set of tested-but-unimplemented rules, so the day CEN adds one the
build says so.

## Not in scope

Writing or generating invoices, Peppol network transport, and ZUGFeRD 1.x (legally superseded —
Germany requires 2.0.1 or later; v1 files are detected and rejected with a clear message).
Rendering to PDF is out of scope too: the publisher's PDF path emits XSL-FO, which needs an FO
processor, and the structured XML is the legally archived original in any case.

## Licence

[Business Source License 1.1](LICENSE). In short:

- **Free for production use** if your organisation's annual revenue is under €1,000,000, or if you
  ship Verifacta inside OSI-licensed open source, regardless of revenue.
- **Free for any non-production use** — evaluation, development, CI, internal testing.
- Above that threshold, a commercial licence is required.
- On **2030-09-04** this version becomes Apache 2.0 automatically, so there is no lock-in if the
  project stops being maintained.

For a commercial licence, or if you are unsure which applies to you, email oleganickij02@gmail.com.

Third-party components and the licences of the validation artefacts are listed in [NOTICE](NOTICE).
Verifacta depends on [SaxonCS-HE](https://www.nuget.org/packages/SaxonCS-HE) (Saxonica, MPL-2.0) for
XSLT 3.0 and [PDFsharp](https://www.nuget.org/packages/PDFsharp) (empira, MIT) for PDF attachments.
Both are pure managed. The rule artefacts themselves are never redistributed — they are fetched from
their publishers and stay under their own terms.

## Requirements

.NET 8 or .NET 10, and nothing else. Python 3 and the GitHub CLI are needed only to fetch the test
corpora or to bump the pinned rule packs — neither is required to use Verifacta.
