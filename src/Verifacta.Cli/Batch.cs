using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Verifacta;
using Verifacta.Validation;

/// <summary>
/// Validating a folder of invoices. Aimed at pointing Verifacta at an existing archive to find out
/// how much of it an access point would reject.
/// </summary>
internal static class Batch
{
    private static readonly string[] Extensions = [".xml", ".pdf"];

    /// <summary>Expands directories to the invoice files inside them; plain paths pass through.</summary>
    internal static List<string> Expand(IEnumerable<string> paths, bool recursive)
    {
        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory
                    .EnumerateFiles(path, "*", search)
                    .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)));
            }
            else
            {
                files.Add(path);
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
    }

    internal static List<Report> Run(
        InvoiceValidator validator, List<string> files, RuleSet? ruleSet, bool validateSchema)
    {
        var reports = new ConcurrentBag<Report>();

        // The validator caches compiled stylesheets and is thread-safe, so a large archive is
        // worth spreading across cores; a single file is not.
        if (files.Count > 1)
        {
            Parallel.ForEach(files, file => reports.Add(Validate(validator, file, ruleSet, validateSchema)));
        }
        else
        {
            reports.Add(Validate(validator, files[0], ruleSet, validateSchema));
        }

        return reports.OrderBy(report => report.File, StringComparer.Ordinal).ToList();
    }

    private static Report Validate(
        InvoiceValidator validator, string file, RuleSet? ruleSet, bool validateSchema)
    {
        try
        {
            var document = InvoiceDocument.Load(file);
            var result = validator.Validate(document, ruleSet, validateSchema);

            return new Report(
                file,
                result.IsValid ? "valid" : "invalid",
                result.RuleSet.ToString(),
                result.SchemaValid,
                document.EmbeddedFileName,
                result.Findings.Select(Finding.From).ToList(),
                null);
        }
        catch (Exception exception) when (exception is UnsupportedDocumentException or ValidationException)
        {
            return new Report(file, "error", null, false, null, [], exception.Message);
        }
    }

    internal static void WriteCsv(TextWriter writer, IEnumerable<Report> reports)
    {
        writer.WriteLine("file,status,ruleSet,schemaValid,errors,warnings,rules,detail");

        foreach (var report in reports)
        {
            var errors = report.Findings.Count(f => f.Severity == nameof(ValidationSeverity.Error));
            var warnings = report.Findings.Count(f => f.Severity == nameof(ValidationSeverity.Warning));
            var rules = string.Join(";", report.Findings
                .Where(f => f.Severity == nameof(ValidationSeverity.Error))
                .Select(f => f.RuleId)
                .Distinct());

            writer.WriteLine(string.Join(',', [
                Escape(report.File),
                report.Status,
                report.RuleSet ?? string.Empty,
                report.SchemaValid.ToString(CultureInfo.InvariantCulture),
                errors.ToString(CultureInfo.InvariantCulture),
                warnings.ToString(CultureInfo.InvariantCulture),
                Escape(rules),
                Escape(report.Error ?? string.Empty),
            ]));
        }
    }

    /// <summary>The part an ISV actually reads: how much of the archive would be rejected, and why.</summary>
    internal static void WriteSummary(TextWriter writer, IReadOnlyList<Report> reports)
    {
        var valid = reports.Count(report => report.Status == "valid");
        var invalid = reports.Count(report => report.Status == "invalid");
        var unreadable = reports.Count(report => report.Status == "error");
        var schemaFailures = reports.Count(report => report.Status != "error" && !report.SchemaValid);

        writer.WriteLine();
        writer.WriteLine($"{reports.Count} file(s): {valid} valid, {invalid} with errors, {unreadable} unreadable");

        if (schemaFailures > 0)
        {
            writer.WriteLine($"{schemaFailures} did not conform to their XML Schema, so business rules were not run.");
        }

        var byRule = reports
            .SelectMany(report => report.Findings
                .Where(finding => finding.Severity == nameof(ValidationSeverity.Error))
                .Select(finding => finding.RuleId)
                .Distinct())
            .GroupBy(ruleId => ruleId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(10)
            .ToList();

        if (byRule.Count == 0) return;

        writer.WriteLine();
        writer.WriteLine("Most frequent errors:");
        foreach (var group in byRule)
        {
            writer.WriteLine($"  {group.Key,-24} {group.Count(),5} file(s)");
        }
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n')) return value;

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
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
