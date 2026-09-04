using System.Text;
using System.Xml;
using Verifacta.Detection;
using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// Validates every readable invoice in the corpus and records the outcome. The pipeline mirrors
/// KoSIT's scenarios.xml — the same artefacts in the same order — so a verdict here is the verdict
/// their reference validator produces. The published instances that still fail are catalogued in
/// <see cref="UpstreamDefects"/> with the reason each one is genuinely non-conformant.
/// </summary>
[Collection("validator")]
public class CorpusValidationTests(ValidatorFixture fixture)
{
    private static readonly string? Root = Locate();

    /// <summary>
    /// Published example invoices that Verifacta reports errors on. Each was checked by hand
    /// against the rule expression and the document: all three are defects in the published
    /// examples, not disagreements in Verifacta. KoSIT's own scenarios.xml runs the same two
    /// artefacts in the same order we do, so their validator reports these too.
    /// </summary>
    private static readonly Dictionary<string, string> UpstreamDefects = new()
    {
        ["GR-base-example-TaxRepresentative.xml"] =
            "XSD: cac:AdditionalDocumentReference appears after cac:PayeeParty, which UBL 2.1's element "
            + "sequence does not allow. The file also has no cac:PartyLegalEntity/cbc:RegistrationName, "
            + "so it would fail BR-06 as well.",
        ["GR-base-example-correct.xml"] =
            "XSD: same UBL 2.1 sequence violation — cac:AdditionalDocumentReference in the wrong position.",
        ["05.01a-INVOICE_ubl.xml"] =
            "BR-CO-16: BT-112 is 336.90 and BT-115 is 366.86 with no prepaid or rounding amount — a transposed digit.",
        ["04.05a-INVOICE_uncefact.xml"] =
            "BR-CL-10/BR-CL-21: a DiGA invoice using schemeID=XR03 for BT-29/BT-46/BT-60. XR03 is an "
            + "XRechnung code for health-insurer IK numbers and is not in the ISO 6523 ICD list the CEN "
            + "rules demand, so CEN 1.3.16 and XRechnung 3.0.2 genuinely conflict here.",
    };

    [Fact]
    public void Kosit_instances_conform_to_xrechnung()
    {
        if (Root is null || !fixture.Available) return;

        Assert.Equal(
            ["04.05a-INVOICE_uncefact.xml", "05.01a-INVOICE_ubl.xml"],
            Offenders("kosit-xrechnung", RuleSet.XRechnung));
    }

    [Fact]
    public void Peppol_examples_conform_to_peppol_bis()
    {
        if (Root is null || !fixture.Available) return;

        Assert.Equal(
            ["GR-base-example-TaxRepresentative.xml", "GR-base-example-correct.xml"],
            Offenders("peppol-bis", RuleSet.PeppolBisBilling3));
    }

    [Fact]
    public void Every_upstream_defect_is_still_observed()
    {
        if (Root is null || !fixture.Available) return;

        var observed = Offenders("kosit-xrechnung", RuleSet.XRechnung)
            .Concat(Offenders("peppol-bis", RuleSet.PeppolBisBilling3))
            .ToHashSet();

        foreach (var (file, reason) in UpstreamDefects)
        {
            Assert.True(
                observed.Contains(file),
                $"{file} now validates cleanly ({reason}) — remove it from UpstreamDefects.");
        }
    }

    private List<string> Offenders(string source, RuleSet ruleSet)
    {
        var offenders = new List<string>();

        foreach (var file in Files(source))
        {
            if (!TryLoad(file, out var document)) continue;
            if (ruleSet == RuleSet.PeppolBisBilling3 && document!.Syntax != InvoiceSyntax.Ubl) continue;

            if (!fixture.Validator!.Validate(document!, ruleSet).IsValid)
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Sort(StringComparer.Ordinal);
        return offenders;
    }

    [Fact]
    public void Writes_validation_report()
    {
        if (Root is null || !fixture.Available) return;

        var report = new StringBuilder();
        report.AppendLine("# Validation report");
        report.AppendLine();
        report.AppendLine("| Source | Validated | Clean | With errors | With warnings |");
        report.AppendLine("|---|---:|---:|---:|---:|");

        var ruleIds = new SortedDictionary<string, int>();
        var detail = new List<string>();
        var engineFailures = new List<string>();

        foreach (var source in Directory.EnumerateDirectories(Root).Select(Path.GetFileName).Order())
        {
            int validated = 0, clean = 0, withErrors = 0, withWarnings = 0;

            foreach (var file in Files(source!))
            {
                if (!TryLoad(file, out var document)) continue;

                ValidationResult result;
                try
                {
                    result = fixture.Validator!.Validate(document!);
                }
                catch (RulePackException)
                {
                    continue;
                }
                catch (ValidationException)
                {
                    engineFailures.Add($"- `{source}/{Path.GetFileName(file)}`");
                    continue;
                }

                validated++;
                if (result.Errors.Count > 0) withErrors++;
                if (result.Warnings.Count > 0) withWarnings++;
                if (result.Findings.Count == 0) clean++;

                foreach (var id in result.Errors.Select(error => error.RuleId).Distinct())
                {
                    ruleIds[id] = ruleIds.TryGetValue(id, out var count) ? count + 1 : 1;
                }

                if (result.Errors.Count > 0)
                {
                    detail.Add($"- `{source}/{Path.GetFileName(file)}` ({result.RuleSet}): " +
                               string.Join(", ", result.Errors.Select(error => error.RuleId).Distinct()));
                }
            }

            report.AppendLine($"| {source} | {validated} | {clean} | {withErrors} | {withWarnings} |");
        }

        if (ruleIds.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Errors by rule");
            report.AppendLine();
            foreach (var (id, count) in ruleIds.OrderByDescending(pair => pair.Value))
            {
                report.AppendLine($"- {id}: {count}");
            }
        }

        if (engineFailures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Files the rule artefacts could not process");
            report.AppendLine();
            engineFailures.ForEach(line => report.AppendLine(line));
        }

        if (detail.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Files with errors");
            report.AppendLine();
            detail.ForEach(line => report.AppendLine(line));
        }

        File.WriteAllText(Path.Combine(Root, "validation-report.md"), report.ToString());
    }

    private static IEnumerable<string> Files(string source)
    {
        var folder = Path.Combine(Root!, source);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).Order()
            : [];
    }

    private static bool TryLoad(string file, out InvoiceDocument? document)
    {
        try
        {
            document = InvoiceDocument.Load(file);
            return true;
        }
        catch (UnsupportedDocumentException)
        {
            document = null;
            return false;
        }
        catch (XmlException)
        {
            document = null;
            return false;
        }
    }

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "corpus");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
