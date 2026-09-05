using Verifacta;
using Verifacta.Validation;

// 1. The rule artefacts are not shipped in the package — their publishers' licences are not ours
//    to redistribute. Restore downloads them from the pinned upstream releases and checks every
//    file against the SHA-256 in the embedded manifest. Do this once, at deployment or at build.
var catalog = RulePackCatalog.Load();

if (!catalog.IsRestored)
{
    Console.WriteLine($"Restoring rule packs into {catalog.Root}...");
    await catalog.RestoreAsync();
}

// 2. Compiling the rule packs takes seconds; validating takes milliseconds. Keep one validator for
//    the lifetime of the process. It is thread-safe.
var validator = new InvoiceValidator(catalog);

// 3. Load an invoice. The same call reads UBL, CII, or the XML inside a Factur-X / ZUGFeRD PDF.
var document = InvoiceDocument.Load(args.FirstOrDefault() ?? "invoice.xml");

Console.WriteLine($"{document.Syntax} {document.Kind}, profile {document.Profile.Kind}");

// 4. Validate. With no rule set given, it is chosen from the invoice's own specification identifier.
var result = validator.Validate(document);

Console.WriteLine($"Rule set:  {result.RuleSet} ({string.Join(", ", result.RulePacks)})");
Console.WriteLine($"Schema:    {(result.SchemaChecked ? result.SchemaValid ? "valid" : "INVALID" : "not checked")}");
Console.WriteLine($"Profile:   {(result.ProfileCovered ? "has its own rules" : "no rules here, judged against EN 16931")}");
Console.WriteLine($"Verdict:   {(result.IsValid ? "valid" : "INVALID")}");

foreach (var finding in result.Findings)
{
    Console.WriteLine();
    Console.WriteLine($"  {finding.Severity} {finding.RuleId} {string.Join(" ", finding.BusinessTerms)}");
    Console.WriteLine($"    at    {finding.Location}");

    // The value the rule tripped over, when it pointed at exactly one. Rules that compare several
    // fields report none, because the alternative is quoting half the invoice back at the user.
    if (finding.Value is { } value)
    {
        Console.WriteLine($"    value {value}");
    }

    Console.WriteLine($"    {finding.Message}");

    // Which artefact produced it — this is what separates a CEN core rule from a German CIUS rule.
    Console.WriteLine($"    from  {finding.Artefact}");
}

return result.IsValid ? 0 : 1;
