using System.Text.Json;
using System.Text.Json.Serialization;
using Verifacta;
using Verifacta.Rendering;
using Verifacta.Validation;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Usage();
    return ExitCode.Usage;
}

var command = args[0];
var paths = new List<string>();
var flags = new HashSet<string>(StringComparer.Ordinal);
var values = new Dictionary<string, string>(StringComparer.Ordinal);

// Options that take a value are consumed with their argument, otherwise "--rules xrechnung" would
// treat "xrechnung" as a file to validate.
string[] valueOptions = ["--rules", "--csv", "--out", "-o", "--lang"];

for (var index = 1; index < args.Length; index++)
{
    var argument = args[index];

    if (!argument.StartsWith('-'))
    {
        paths.Add(argument);
    }
    else if (argument.IndexOf('=') is var equals && equals > 0)
    {
        values[argument[..equals]] = argument[(equals + 1)..];
    }
    else if (valueOptions.Contains(argument) && index + 1 < args.Length)
    {
        values[argument] = args[++index];
    }
    else
    {
        flags.Add(argument);
    }
}

try
{
    return command switch
    {
        "rules" => await Rules(paths.FirstOrDefault(), flags.Contains("--force")),
        "validate" => Validate(),
        "render" => Render(),
        "info" => Info(),
        _ => Unknown(command),
    };
}
catch (RulePackException exception)
{
    Error(exception.Message);
    return ExitCode.Failed;
}
// A bad output path is a user mistake, not a defect: say what went wrong instead of a stack trace.
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
            foreach (var pack in catalog.Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {pack.Id,-14} {pack.Version,-12} {pack.Files.Count,2} artefacts  " +
                                  $"{pack.Repository}@{pack.Tag}");
            }

            Console.WriteLine($"All artefacts match the manifest in {catalog.Root}.");
            return ExitCode.Valid;

        default:
            Error("Usage: verifacta rules restore [--force] | verifacta rules verify");
            return ExitCode.Usage;
    }
}

int Validate()
{
    var files = Batch.Expand(paths, flags.Contains("--recursive") || flags.Contains("-r"));

    if (files.Count == 0)
    {
        Error(paths.Count == 0 ? "No file or folder given." : "No .xml or .pdf files found.");
        return ExitCode.Usage;
    }

    var validator = new InvoiceValidator();

    // Compiling every pack up front costs seconds once; over an archive it pays for itself, and it
    // keeps the progress line honest instead of stalling on the first file.
    if (files.Count > 10)
    {
        Console.Error.WriteLine($"Compiling rule packs, then validating {files.Count} files...");
        validator.Warmup();
    }

    var reports = Batch.Run(
        validator, files, RuleSet(), !flags.Contains("--no-schema"), flags.Contains("--strict"));

    if (flags.Contains("--json"))
    {
        Console.WriteLine(JsonSerializer.Serialize(reports, JsonDefaults.Options));
    }
    else if (values.TryGetValue("--csv", out var csv))
    {
        using var writer = new StreamWriter(csv);
        Batch.WriteCsv(writer, reports);
        Console.WriteLine($"Wrote {reports.Count} rows to {csv}");
        Batch.WriteSummary(Console.Out, reports);
    }
    else
    {
        foreach (var report in reports)
        {
            PrintText(report, files.Count > 1);
        }

        if (files.Count > 1) Batch.WriteSummary(Console.Out, reports);
    }

    if (reports.Any(report => report.Status == "error")) return ExitCode.Failed;

    return reports.Any(report => report.Status == "invalid") ? ExitCode.Invalid : ExitCode.Valid;
}

int Render()
{
    var files = Batch.Expand(paths, flags.Contains("--recursive") || flags.Contains("-r"));

    if (files.Count == 0)
    {
        Error(paths.Count == 0 ? "No file or folder given." : "No .xml or .pdf files found.");
        return ExitCode.Usage;
    }

    var language = values.TryGetValue("--lang", out var lang) ? lang : "de";
    var destination = values.TryGetValue("--out", out var output) ? output
        : values.TryGetValue("-o", out var shortOutput) ? shortOutput
        : null;

    if (files.Count > 1 && destination is not null && File.Exists(destination))
    {
        Error($"{files.Count} invoices cannot be rendered into the single file '{destination}'. " +
              "Give a folder instead.");
        return ExitCode.Usage;
    }

    var renderer = new InvoiceRenderer();
    if (files.Count > 1) renderer.Warmup();

    // One file with no destination goes to stdout so it can be piped; anything else is written
    // beside its invoice, or into the folder given by --out.
    if (files.Count == 1 && destination is null)
    {
        try
        {
            Console.Out.Write(renderer.ToHtml(InvoiceDocument.Load(files[0].FullPath), language));
            return ExitCode.Valid;
        }
        catch (Exception exception) when (exception is UnsupportedDocumentException or RenderingException)
        {
            Error($"{files[0].FullPath}: {exception.Message}");
            return ExitCode.Failed;
        }
    }

    var namedFile = files.Count == 1 && destination is not null && !Directory.Exists(destination);
    var sources = files.Select(file => Path.GetFullPath(file.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var targets = new Dictionary<string, InvoiceFile>(StringComparer.OrdinalIgnoreCase);

    // Every destination is worked out and checked before anything is written, so a run that would
    // destroy an invoice or drop one on top of another fails having touched nothing.
    foreach (var file in files)
    {
        var target = Path.GetFullPath(namedFile
            ? destination!
            : destination is null
                ? Path.ChangeExtension(file.FullPath, ".html")
                : Path.Combine(destination, Path.ChangeExtension(file.RelativePath, ".html")));

        if (sources.Contains(target))
        {
            Error($"'{target}' is one of the invoices being rendered. Choose a different destination.");
            return ExitCode.Usage;
        }

        if (targets.TryGetValue(target, out var claimed))
        {
            Error($"'{claimed.FullPath}' and '{file.FullPath}' would both be written to '{target}'. " +
                  "Render the folders separately, or use --out with the folder they share.");
            return ExitCode.Usage;
        }

        targets[target] = file;
    }

    var failed = false;

    foreach (var (target, file) in targets.OrderBy(entry => entry.Value.FullPath, StringComparer.Ordinal))
    {
        try
        {
            WriteAtomically(target, writer => renderer.ToHtml(InvoiceDocument.Load(file.FullPath), writer, language));
            Console.WriteLine($"  {target}");
        }
        // Reported per file rather than thrown: one unreadable invoice should not abandon the rest.
        catch (Exception exception) when (exception is UnsupportedDocumentException or RenderingException
                                              or IOException or UnauthorizedAccessException)
        {
            Error($"  {file.FullPath}: {exception.Message}");
            failed = true;
        }
    }

    return failed ? ExitCode.Failed : ExitCode.Valid;
}

int Info()
{
    var files = Batch.Expand(paths, flags.Contains("--recursive") || flags.Contains("-r"));
    var summaries = new List<object>();
    var asJson = flags.Contains("--json");
    var failed = false;

    if (files.Count == 0)
    {
        Error("No file or folder given.");
        return ExitCode.Usage;
    }

    foreach (var invoiceFile in files)
    {
        var file = invoiceFile.FullPath;

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
        // Reported per file rather than returned on the first one: pointing info at a folder
        // should describe everything it can read, the way validate and render do.
        catch (Exception exception) when (exception is UnsupportedDocumentException
                                             or IOException or UnauthorizedAccessException)
        {
            Error($"{file}: {exception.Message}");
            failed = true;
        }
    }

    if (asJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(summaries, JsonDefaults.Options));
    }

    return failed ? ExitCode.Failed : ExitCode.Valid;
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
    else if (!report.SchemaChecked)
    {
        Console.WriteLine("  XML Schema not checked");
    }

    if (!report.ProfileCovered)
    {
        Console.WriteLine($"  profile '{report.Profile ?? "(none declared)"}' has no rule set here; " +
                          "judged against EN 16931 alone");
    }

    foreach (var finding in report.Findings)
    {
        var terms = finding.BusinessTerms.Count == 0 ? string.Empty : $" [{string.Join(" ", finding.BusinessTerms)}]";
        Console.WriteLine($"  {finding.Severity,-11} {finding.RuleId,-22}{terms}");
        if (finding.Location.Length > 0) Console.WriteLine($"    {finding.Location}");
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

static void Error(string message) => Console.Error.WriteLine(message);

/// <summary>
/// Renders to a temporary file in the destination folder and moves it into place, so a document
/// that fails halfway through leaves whatever was already there untouched rather than a truncated
/// page. The move is what makes the result appear all at once.
/// </summary>
static void WriteAtomically(string target, Action<TextWriter> render)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var temporary = target + ".partial";

    try
    {
        using (var writer = new StreamWriter(temporary))
        {
            render(writer);
        }

        File.Move(temporary, target, overwrite: true);
    }
    catch
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one worth reporting.
        }

        throw;
    }
}

RuleSet? RuleSet() => values.TryGetValue("--rules", out var value)
    ? value.ToLowerInvariant() switch
    {
        "en16931" => Verifacta.Validation.RuleSet.En16931,
        "peppol" => Verifacta.Validation.RuleSet.PeppolBisBilling3,
        "xrechnung" => Verifacta.Validation.RuleSet.XRechnung,
        _ => throw new RulePackException($"Unknown rule set '{value}'. Use en16931, peppol or xrechnung."),
    }
    : null;

static void Usage()
{
    Console.WriteLine(
        """
        verifacta — validate EN 16931 electronic invoices

        Usage:
          verifacta rules restore [--force]
          verifacta rules verify
          verifacta validate <file|folder...> [options]
          verifacta render <file|folder...> [-o <file|folder>] [--lang de|en]
          verifacta info <file|folder...> [--json]

        Options:
          --rules <set>   en16931 | peppol | xrechnung (default: taken from the invoice)
          --recursive     descend into subfolders, -r for short
          --csv <file>    write one row per invoice, with a summary on the console
          --json          machine-readable output
          --no-schema     check business rules only, skipping XML Schema
          --strict        fail an invoice whose declared specification has no rule set
                          here, instead of judging it against EN 16931 alone
          -o, --out       where to write rendered HTML; stdout for a single invoice
          --lang          label language for render: de (default) or en

        render produces a self-contained HTML page using the official XRechnung
        visualisation, so what you show a user matches the reference rendering.

        Run "rules restore" once: it downloads the validation artefacts from their
        publishers and checks them against the SHA-256 recorded in the manifest.
        Nothing else in Verifacta touches the network.

        Files may be UBL or CII XML, or a hybrid Factur-X / ZUGFeRD 2.x PDF. Point it at a
        folder to see how much of an existing archive would be rejected.

        Exit codes:
          0  valid
          1  validation errors
          2  a file could not be processed
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

internal static class JsonDefaults
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
