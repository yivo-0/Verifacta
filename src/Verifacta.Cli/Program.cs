using System.Text.Json;
using System.Text.Json.Serialization;
using Verifacta;
using Verifacta.Validation;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Usage();
    return ExitCode.Usage;
}

var command = args[0];
var paths = args.Skip(1).Where(argument => !argument.StartsWith('-')).ToList();
var options = args.Skip(1).Where(argument => argument.StartsWith('-')).ToList();
var json = options.Contains("--json");

if (command == "rules")
{
    try
    {
        return await Rules(paths.FirstOrDefault(), options.Contains("--force"));
    }
    catch (RulePackException exception)
    {
        Error(exception.Message);
        return ExitCode.Failed;
    }
}

if (paths.Count == 0)
{
    Error("No file given.");
    Usage();
    return ExitCode.Usage;
}

try
{
    return command switch
    {
        "validate" => Validate(paths, options, json),
        "info" => Info(paths, json),
        _ => Unknown(command),
    };
}
catch (RulePackException exception)
{
    Error(exception.Message);
    return ExitCode.Failed;
}

async Task<int> Rules(string? subcommand, bool force)
{
    var catalog = RulePackCatalog.Load();

    switch (subcommand)
    {
        case "restore":
            Console.WriteLine($"Restoring rule packs into {catalog.Root}");
            var written = await catalog.RestoreAsync(force);

            if (written.Count == 0)
            {
                Console.WriteLine("Everything is already present and matches the manifest.");
            }

            foreach (var artefact in written)
            {
                Console.WriteLine($"  {artefact}");
            }

            catalog.VerifyIntegrity();
            Console.WriteLine($"{catalog.Packs.Count} packs ready.");
            return ExitCode.Valid;

        case "verify":
            catalog.VerifyIntegrity();
            foreach (var pack in catalog.Packs.OrderBy(pack => pack.Id))
            {
                Console.WriteLine($"  {pack.Id,-14} {pack.Version,-12} {pack.Files.Count,2} artefacts  " +
                                  $"{pack.Licence}  {pack.Repository}@{pack.Tag}");
            }

            Console.WriteLine($"All artefacts match the manifest in {catalog.Root}.");
            return ExitCode.Valid;

        default:
            Error("Usage: verifacta rules restore [--force] | verifacta rules verify");
            return ExitCode.Usage;
    }
}

int Validate(List<string> files, List<string> flags, bool asJson)
{
    var ruleSet = RuleSet(flags);
    var validateSchema = !flags.Contains("--no-schema");
    var validator = new InvoiceValidator();
    var reports = new List<Report>();
    var worst = ExitCode.Valid;

    foreach (var file in files)
    {
        try
        {
            var document = InvoiceDocument.Load(file);
            var result = validator.Validate(document, ruleSet, validateSchema);

            reports.Add(new Report(
                file,
                result.IsValid ? "valid" : "invalid",
                result.RuleSet.ToString(),
                result.SchemaValid,
                document.EmbeddedFileName,
                result.Findings.Select(Finding.From).ToList(),
                null));

            if (!result.IsValid) worst = ExitCode.Invalid;
        }
        catch (Exception exception) when (exception is UnsupportedDocumentException or ValidationException)
        {
            reports.Add(new Report(file, "error", null, false, null, [], exception.Message));
            worst = ExitCode.Failed;
        }
    }

    if (asJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(reports, JsonDefaults.Options));
        return worst;
    }

    foreach (var report in reports)
    {
        PrintText(report, files.Count > 1);
    }

    return worst;
}

int Info(List<string> files, bool asJson)
{
    var summaries = new List<object>();

    foreach (var file in files)
    {
        try
        {
            var document = InvoiceDocument.Load(file);
            var invoice = document.Read().Invoice;

            if (asJson)
            {
                summaries.Add(new
                {
                    file,
                    syntax = document.Syntax.ToString(),
                    kind = document.Kind.ToString(),
                    profile = document.Profile.Kind.ToString(),
                    version = document.Profile.Version,
                    level = document.Profile.Level,
                    attachment = document.EmbeddedFileName,
                    id = invoice.Id,
                    issueDate = invoice.IssueDate?.ToString("O"),
                    currency = invoice.CurrencyCode,
                    payable = invoice.Totals.DuePayableAmount,
                });
                continue;
            }

            Console.WriteLine($"{file}");
            Console.WriteLine($"  syntax     {document.Syntax}  {document.Kind}");
            Console.WriteLine($"  profile    {document.Profile.Kind} {document.Profile.Version}" +
                              $"{(document.Profile.Level is { } level ? $" ({level})" : string.Empty)}" +
                              $"{(document.Profile.IsExtension ? " extension" : string.Empty)}");
            if (document.EmbeddedFileName is { } attachment)
            {
                Console.WriteLine($"  attachment {attachment}");
            }

            Console.WriteLine($"  invoice    {invoice.Id}  {invoice.IssueDate:yyyy-MM-dd}  " +
                              $"{invoice.Totals.DuePayableAmount} {invoice.CurrencyCode}");
            Console.WriteLine($"  seller     {invoice.Seller.Name}");
            Console.WriteLine($"  buyer      {invoice.Buyer.Name}");
            Console.WriteLine($"  lines      {invoice.Lines.Count}");
        }
        catch (UnsupportedDocumentException exception)
        {
            Error($"{file}: {exception.Message}");
            return ExitCode.Failed;
        }
    }

    if (asJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(summaries, JsonDefaults.Options));
    }

    return ExitCode.Valid;
}

static void PrintText(Report report, bool showFile)
{
    if (showFile) Console.WriteLine(report.File);

    if (report.Error is { } message)
    {
        Error($"  {message}");
        return;
    }

    if (!report.SchemaValid)
    {
        Console.WriteLine("  does not conform to its XML Schema; business rules were not run");
    }

    foreach (var finding in report.Findings)
    {
        var terms = finding.BusinessTerms.Count == 0 ? string.Empty : $" [{string.Join(" ", finding.BusinessTerms)}]";
        Console.WriteLine($"  {finding.Severity,-11} {finding.RuleId,-22}{terms}");
        Console.WriteLine($"    {finding.Location}");
        Console.WriteLine($"    {finding.Message}");
    }

    var errors = report.Findings.Count(finding => finding.Severity == nameof(ValidationSeverity.Error));
    var warnings = report.Findings.Count(finding => finding.Severity == nameof(ValidationSeverity.Warning));
    Console.WriteLine($"  {report.Status} against {report.RuleSet} — {errors} error(s), {warnings} warning(s)");
}

static int Unknown(string command)
{
    Error($"Unknown command '{command}'.");
    Usage();
    return ExitCode.Usage;
}

static void Error(string message)
{
    Console.Error.WriteLine(message);
}

static RuleSet? RuleSet(List<string> flags)
{
    var index = flags.FindIndex(flag => flag is "--rules");
    var inline = flags.FirstOrDefault(flag => flag.StartsWith("--rules=", StringComparison.Ordinal));

    var value = inline?["--rules=".Length..]
                ?? (index >= 0 && index + 1 < flags.Count ? flags[index + 1] : null);

    return value?.ToLowerInvariant() switch
    {
        null => null,
        "en16931" => Verifacta.Validation.RuleSet.En16931,
        "peppol" => Verifacta.Validation.RuleSet.PeppolBisBilling3,
        "xrechnung" => Verifacta.Validation.RuleSet.XRechnung,
        _ => throw new RulePackException($"Unknown rule set '{value}'. Use en16931, peppol or xrechnung."),
    };
}

static void Usage()
{
    Console.WriteLine(
        """
        verifacta — validate EN 16931 electronic invoices

        Usage:
          verifacta rules restore [--force]
          verifacta rules verify
          verifacta validate <file...> [--rules en16931|peppol|xrechnung] [--no-schema] [--json]
          verifacta info <file...> [--json]

        Run "rules restore" once: it downloads the validation artefacts from their
        publishers and checks them against the SHA-256 recorded in the manifest.
        Nothing else in Verifacta touches the network.

        Files may be UBL or CII XML, or a hybrid Factur-X / ZUGFeRD 2.x PDF.
        The rule set is taken from the invoice itself unless --rules says otherwise.

        Exit codes:
          0  valid
          1  validation errors
          2  the file could not be processed
          64 usage error
        """);
}

internal static class ExitCode
{
    internal const int Valid = 0;
    internal const int Invalid = 1;
    internal const int Failed = 2;
    internal const int Usage = 64;
}

internal sealed record Report(
    string File,
    string Status,
    string? RuleSet,
    bool SchemaValid,
    string? Attachment,
    List<Finding> Findings,
    string? Error);

internal sealed record Finding(
    string RuleId,
    string Severity,
    string Message,
    string Location,
    List<string> BusinessTerms,
    string Artefact)
{
    internal static Finding From(ValidationFinding finding) => new(
        finding.RuleId,
        finding.Severity.ToString(),
        finding.Message,
        finding.Location,
        finding.BusinessTerms.ToList(),
        finding.Artefact);
}

internal static class JsonDefaults
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
