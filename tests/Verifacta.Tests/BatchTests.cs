using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// The folder-validation plumbing behind "verifacta validate ./invoices". None of it was covered
/// while it was the command the README leads with.
/// </summary>
public class BatchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("verifacta-batch").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Expands_a_folder_to_the_invoice_files_inside_it()
    {
        Touch("a.xml");
        Touch("b.pdf");
        Touch("notes.txt");
        Touch("nested/c.xml");

        var files = Batch.Expand([_root], recursive: false);

        Assert.Equal(["a.xml", "b.pdf"], files.Select(file => Path.GetFileName(file.FullPath)));
    }

    [Fact]
    public void Descends_only_when_asked()
    {
        Touch("a.xml");
        Touch("nested/deep/c.xml");

        Assert.Single(Batch.Expand([_root], recursive: false));
        Assert.Equal(2, Batch.Expand([_root], recursive: true).Count);
    }

    [Fact]
    public void Matches_extensions_case_insensitively()
    {
        // Three .XML invoices were invisible to CI on Linux once already.
        Touch("upper.XML");
        Touch("mixed.Pdf");

        Assert.Equal(2, Batch.Expand([_root], recursive: false).Count);
    }

    [Fact]
    public void Passes_explicit_paths_through_without_checking_them()
    {
        var path = Path.Combine(_root, "missing.xml");

        Assert.Equal([path], Batch.Expand([path], recursive: false).Select(file => file.FullPath));
    }

    [Fact]
    public void Does_not_list_the_same_file_twice()
    {
        Touch("a.xml");
        var path = Path.Combine(_root, "a.xml");

        Assert.Single(Batch.Expand([_root, path], recursive: false));
    }

    [Fact]
    public void Keeps_the_path_each_file_had_inside_its_folder()
    {
        Touch("customer-a/001.xml");
        Touch("customer-b/001.xml");

        var files = Batch.Expand([_root], recursive: true);

        Assert.Equal(
            [Path.Combine("customer-a", "001.xml"), Path.Combine("customer-b", "001.xml")],
            files.Select(file => file.RelativePath));
    }

    [Fact]
    public void Writes_a_csv_row_per_invoice()
    {
        var reports = new List<Report>
        {
            Valid("a.xml"),
            Report("b.xml", "invalid", "En16931", profile: "urn:cen.eu:en16931:2017",
                findings: [new Finding("BR-CO-15", "Error", "totals disagree", "/x", "2033.00", ["BT-112"], "EN16931-UBL-validation.xsl")]),
        };

        using var writer = new StringWriter();
        Batch.WriteCsv(writer, reports);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "file,status,ruleSet,profile,profileCovered,schemaChecked,schemaValid,errors,warnings,rules,detail",
            lines[0]);
        Assert.Equal("a.xml,valid,XRechnung,urn:xrechnung,True,True,True,0,0,,", lines[1]);
        Assert.Equal("b.xml,invalid,En16931,urn:cen.eu:en16931:2017,True,True,True,1,0,BR-CO-15,", lines[2]);
    }

    [Fact]
    public void Records_that_the_schema_was_skipped_and_the_profile_unknown()
    {
        var reports = new List<Report>
        {
            Report("a.xml", "valid", "En16931", profile: "urn:something:else",
                profileCovered: false, schemaChecked: false),
        };

        using var writer = new StringWriter();
        Batch.WriteCsv(writer, reports);
        var row = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];

        // "valid" alone would be a claim the run cannot support.
        Assert.Equal("a.xml,valid,En16931,urn:something:else,False,False,True,0,0,,", row);
    }

    [Fact]
    public void Quotes_csv_fields_that_carry_a_separator()
    {
        var reports = new List<Report>
        {
            Report("in,voice.xml", "error", null, error: "he said \"no\""),
        };

        using var writer = new StringWriter();
        Batch.WriteCsv(writer, reports);

        Assert.Contains("\"in,voice.xml\"", writer.ToString());
        Assert.Contains("\"he said \"\"no\"\"\"", writer.ToString());
    }

    [Fact]
    public void Summarises_an_archive_by_the_rules_that_fail_most()
    {
        var reports = new List<Report>
        {
            Invalid("a.xml", "BR-CO-15"),
            Invalid("b.xml", "BR-CO-15"),
            Invalid("c.xml", "BR-DE-15"),
            Valid("d.xml"),
            Report("e.xml", "error", null, error: "not a PDF"),
        };

        using var writer = new StringWriter();
        Batch.WriteSummary(writer, reports);
        var summary = writer.ToString();

        Assert.Contains("5 file(s): 1 valid, 3 with errors, 1 unreadable", summary);
        Assert.Contains("BR-CO-15", summary);
        Assert.Matches(@"BR-CO-15\s+2 file", summary);
        Assert.Matches(@"BR-DE-15\s+1 file", summary);
    }

    [Fact]
    public void Says_when_a_file_never_reached_the_business_rules()
    {
        var reports = new List<Report> { Report("a.xml", "invalid", "XRechnung", schemaValid: false) };

        using var writer = new StringWriter();
        Batch.WriteSummary(writer, reports);

        Assert.Contains("did not conform to their XML Schema", writer.ToString());
    }

    [Fact]
    public void Says_when_a_profile_had_no_rule_set_of_its_own()
    {
        var reports = new List<Report> { Report("a.xml", "valid", "En16931", profileCovered: false) };

        using var writer = new StringWriter();
        Batch.WriteSummary(writer, reports);
        var summary = writer.ToString();

        Assert.Contains("judged against EN 16931 alone", summary);
        Assert.Contains("--strict", summary);
    }

    [Fact]
    public void Says_when_the_schema_was_never_checked()
    {
        var reports = new List<Report> { Report("a.xml", "valid", "En16931", schemaChecked: false) };

        using var writer = new StringWriter();
        Batch.WriteSummary(writer, reports);

        Assert.Contains("XML Schema was not checked", writer.ToString());
    }

    private static Report Invalid(string file, string ruleId) => Report(
        file, "invalid", "XRechnung",
        findings: [new Finding(ruleId, nameof(ValidationSeverity.Error), "failed", "/x", null, [], "artefact.xsl")]);

    private static Report Valid(string file) => Report(file, "valid", "XRechnung");

    private static Report Report(
        string file,
        string status,
        string? ruleSet,
        bool schemaChecked = true,
        bool schemaValid = true,
        bool profileCovered = true,
        string? profile = "urn:xrechnung",
        List<Finding>? findings = null,
        string? error = null) =>
        new(file, status, ruleSet, schemaChecked, schemaValid, profileCovered, profile, null, findings ?? [], error);

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
