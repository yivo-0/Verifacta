# Klarfakt

Read and validate EN 16931 electronic invoices in .NET — **no Java, no Node.js, no native
dependencies, and nothing leaves your server.**

Klarfakt runs the publishers' own compiled Schematron in-process: the same artefacts the German
reference validator executes, fetched from their pinned upstream releases and checked against a
recorded SHA-256. **100 of 100 comparable files agree rule for rule with KoSIT's validationtool** on
every CI run.

```csharp
using Klarfakt;
using Klarfakt.Validation;

var catalog = RulePackCatalog.Load();
if (!catalog.IsRestored) await catalog.RestoreAsync();   // once, at deployment or build

// Compiling takes seconds, validating takes milliseconds. Keep one; it is thread-safe.
var validator = new InvoiceValidator(catalog);

var document = InvoiceDocument.Load("invoice.xml");      // or a Factur-X / ZUGFeRD PDF
var result = validator.Validate(document);               // rule set chosen from the invoice itself

foreach (var error in result.Errors)
{
    Console.WriteLine($"{error.RuleId} at {error.Location}: {error.Message}");
}
```

```
BR-CL-04 at /ubl-invoice:Invoice/cbc:DocumentCurrencyCode: Invoice currency code
MUST be coded using ISO code list 4217 alpha-3
```

## What it covers

| | |
|---|---|
| **Syntaxes** | UBL 2.1 Invoice and CreditNote, CII D16B, and the XML inside a hybrid Factur-X / ZUGFeRD 2.x PDF |
| **Rule sets** | EN 16931, Peppol BIS Billing 3.0, XRechnung 3.0.2 |
| **Render** | a self-contained HTML page from KoSIT's official XRechnung visualisation |
| **Frameworks** | net8.0, net10.0 |

Findings carry the rule id, severity, location, the failing XPath test, the BT/BG business terms,
the offending value where the rule pointed at one, and which artefact produced them — so a CEN core
rule is distinguishable from a German CIUS rule.

## Rule artefacts

They are **not** in this package. The CEN rules are EUPL-1.2 and the KoSIT configurations are
Apache-2.0, so they stay with their publishers and are downloaded on request:

```bash
klarfakt rules restore
```

Plain HTTPS from the pinned upstream release, every file checked against the SHA-256 in the embedded
manifest before it is written, and again before first use. **Nothing else in Klarfakt touches the
network.** Set `KLARFAKT_RULES` to point at a directory you manage yourself for air-gapped
deployments.

## Licence

**Free unless your organisation turns over more than €1,000,000 a year.** Free at any size inside
OSI-licensed open source. Free always for evaluation, development, CI and internal testing.

Above that threshold and in production, a commercial licence is **€690 a year** for unlimited
developers, deployments and validations, covering every version released during the term
permanently. Every version becomes Apache 2.0 four years after it ships — 1.x on 2030-09-04. Terms
are Business Source License 1.1, which is not an OSI-approved licence.

[Documentation, samples and source](https://github.com/yivo-0/Klarfakt) · oleganickij02@gmail.com
