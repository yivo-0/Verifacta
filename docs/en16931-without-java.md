# Validating European e-invoices in .NET, without a JVM

Since 1 January 2025, every company in Germany must be able to receive an
electronic invoice. Not send one — that comes later, in stages — but receive one,
today, with no revenue threshold and no exemption for small businesses. Belgium
went live in January 2026. Poland in February. France starts in September 2026.

If you write software that receives invoices, someone is going to send you a file
and expect you to know whether it is valid. This is a note about doing that in
.NET, which until about six months ago meant running Java.

## What "valid" actually involves

EN 16931 is the European standard for the semantic content of an invoice. It says
nothing about file format directly; it defines around 200 business terms (BT-1 is
the invoice number, BT-112 the total with VAT) and the rules between them.

A real invoice then arrives in one of two syntaxes — OASIS UBL 2.1 or UN/CEFACT
CII D16B — and usually claims to follow a national profile on top of the standard.
Germany has XRechnung. Peppol has BIS Billing 3.0. France and Italy have their
own. So validating one invoice means, in order:

1. XML Schema. Is this structurally a UBL Invoice at all?
2. The EN 16931 business rules. `BR-CO-15`: does the total with VAT equal the
   total without VAT plus the VAT amount?
3. The national profile's extra rules. `BR-DE-15`: XRechnung requires a buyer
   reference, which EN 16931 leaves optional.

Steps 2 and 3 are published as **Schematron** — a rule language where each rule is
an XPath expression and a message. The publishers ship them precompiled to XSLT,
which sounds like good news until you look at the header:

```xml
<schema queryBinding="xslt2" xmlns="http://purl.oclc.org/dsdl/schematron">
```

## The reason everyone shipped a JVM

`System.Xml.Xsl.XslCompiledTransform` implements XSLT 1.0. Only XSLT 1.0. There
has never been an XSLT 2.0 implementation in the .NET base class library and
Microsoft has been clear that there will not be one.

The EN 16931 artefacts use XSLT 2.0 constructs throughout, and the German ones
reach further still — Saxon's `Q{namespace}name` notation shows up in the location
paths they emit. There is no rewriting your way around this. You need a real
XSLT 2.0/3.0 processor.

So every option involved somebody else's runtime:

| Option | What it costs you |
|---|---|
| KoSIT validationtool | a JVM in your image, and a process to shell out to |
| SaxonJS | Node.js, plus converting the stylesheets to `.sef.json` first |
| Hosted validation APIs | your customers' invoice data leaves your infrastructure |

That last row is the one that mattered for the people I ended up talking to. An
on-premise German accounting product cannot post its customers' invoices to a
third party for checking, and adding a JVM to a .NET deployment because of one XSLT
file is the kind of thing that gets an architecture rejected.

I found a repository that captured the situation exactly: someone had created
`einvoice-validator` in May 2026, a .NET wrapper that shells out to the KoSIT
validator in Docker. The README says it wraps the reference validator "instead of
implementing Peppol business rules in application code". Two commits, zero stars,
abandoned after two days. Somebody needed this, found nothing, built a Java
wrapper by hand, and walked away.

## What changed

**SaxonCS-HE 13.0.0**, released 29 May 2026 under MPL-2.0. It is a native .NET
build of Saxon — not IKVM, not a Java transpile — and it is the first
licence-free XSLT 3.0 processor for .NET. Before Saxon 13, SaxonCS was
Enterprise-only.

That is the whole unlock. The gap was never mysterious; the thing that closed it
simply did not exist until recently.

```csharp
var processor = new Processor();
var executable = processor.NewXsltCompiler().Compile(new Uri(stylesheetPath));
var transformer = executable.Load30();
```

Compiling the 895 KB `EN16931-UBL-validation.xslt` takes about two seconds warm,
six cold. Validating an invoice afterwards takes **27 milliseconds**. That ratio is
the single most important architectural fact: compile once, keep the
`XsltExecutable` for the life of the process, and never do it per request.

## Four things that will cost you an afternoon

These are the ones I lost time to. None of them are in any tutorial.

**The context item is absent.** Load the KoSIT stylesheets and the first
transformation fails with `XPDY0002`. The Schematron artefacts declare global
variables that select from the document root, and `Xslt30Transformer` does not
infer a global context item from the node you pass to `ApplyTemplates`. You have
to set it yourself:

```csharp
var transformer = executable.Load30();
transformer.GlobalContextItem = input;   // not optional
transformer.ApplyTemplates(input, destination);
```

**XmlSchemaSet will not compile the UBL schemas the obvious way.** `XmlResolver` is
null by default on modern .NET, so `xsd:import` resolves to nothing and compilation
fails. Set a resolver and it fails differently: the UBL modules reference each
other's prefixes without always importing them, and following imports as well as
adding files loads some namespaces twice.

What works is to add every module explicitly with resolution turned off:

```csharp
var set = new XmlSchemaSet { XmlResolver = null };
var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };

foreach (var name in schemaFiles)
{
    using var reader = XmlReader.Create(name, settings);
    set.Add(XmlSchema.Read(reader, null));
}

set.Compile();
```

`DtdProcessing.Parse` is needed because the xmldsig core schema declares an
internal DTD of entities. Invoices themselves are still read with DTDs prohibited
outright — that is a different trust boundary.

**Do not tidy the XML.** I spent an evening on a rule that fired locally and not in
the official test suite. The cause was `XDocument.ToString()`, which pretty-prints
by default. Indenting a fragment inserts whitespace text nodes, which changes the
string value of elements, which silently flips any rule that measures content
length. `SaveOptions.DisableFormatting` fixes it. For the same reason, do not strip
comments or processing instructions: a validator has to judge the document as it
arrived.

**A Schematron location is the rule's context, not the value.** When I started
reporting the offending value alongside each finding, I resolved the location
XPath and took its string value. For `BR-CL-04` — an invalid currency code — that
gives you `"XYZ"`, which is exactly what you want. For `BR-CO-15`, which compares
three totals, the location is `/Invoice` and the string value is *every scrap of
text in the entire document*. Adding `[not(*)]` to the XPath, so only leaf nodes
report a value, was the difference between a useful field and an unusable one.

## Proving it, rather than asserting it

Anyone can run a Schematron file and print the output. The interesting question is
whether your verdicts match the verdicts that matter.

Two things settle it.

CEN publishes a test suite: 309 files, each targeting one rule, each declaring
which rules must fire and which must not. Replaying every expectation proves
agreement per rule rather than in aggregate. It also turned up something I would
not have guessed — CEN ships test files for `BR-CO-25` while its own Schematron
never implements that rule. My test suite pins that exact set, so the day CEN adds
it, the build says so.

Then, because Germany is the market, I run KoSIT's own validationtool over the
same invoices in CI and diff the rule ids against mine. Same scenario
configuration release on both sides — read from my manifest, so it cannot drift
onto a different version and produce disagreements that mean nothing.

```
comparing 143 files against itplr-kosit/validator@v1.6.3
100/100 comparable files agree rule for rule with the reference validator (43 not comparable)
```

The 43 are recorded with the reason each cannot be compared, usually that the
XRechnung configuration matched no scenario for that file. A report with no
matched scenario is the reference *declining to judge*, which is not the same as
judging clean, and counting those as agreement would have inflated the number for
nothing.

Java runs the reference. It is not needed to use the library — CI keeping a JDK
around to check my work does not change that.

## Where the artefacts live

One design decision worth explaining, because it looks like an inconvenience.

The rule artefacts are not in the package. The CEN rules are EUPL-1.2, the KoSIT
configurations are Apache-2.0, and they are not mine to redistribute. So the
package embeds a manifest naming every artefact, its upstream release tag, its
licence and its SHA-256, and downloads them on request:

```bash
verifacta rules restore
```

That runs once — at deployment, or in a Docker build stage so the running
container never reaches the network. Nothing else in the library makes a network
call. A pack is checked against the manifest the first time it is used, so an
artefact altered on disk stops validation rather than quietly changing a verdict.

## Where this leaves things

Validating an EN 16931 invoice in .NET is now an ordinary problem. No JVM, no Node
process, no uploading a customer's invoice to somebody else's API. The library I
built is [Verifacta](https://github.com/yivo-0/Verifacta) — free below €1,000,000
of annual revenue and free inside OSI-licensed open source, €690 a year above that — but the more useful
thing in this post is probably the four gotchas, which apply to anyone running
Schematron on .NET regardless of what they build with it.

If you are running EN 16931 validation in production some other way, I would
genuinely like to hear what it cost you. That is the part nobody writes down.
