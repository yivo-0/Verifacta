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

        Assert.Equal(["a.xml", "b.pdf"], files.Select(Path.GetFileName));
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

        Assert.Equal([path], Batch.Expand([path], recursive: false));
    }

    [Fact]
    public void Does_not_list_the_same_file_twice()
    {
        Touch("a.xml");
        var path = Path.Combine(_root, "a.xml");

        Assert.Single(Batch.Expand([_root, path], recursive: false));
    }

    [Fact]
    public void Writes_a_csv_row_per_invoice()
    {
        var reports = new List<Report>
        {
            new("a.xml", "valid", "XRechnung", true, null, [], null),
            new("b.xml", "invalid", "En16931", true, null,
                [new Finding("BR-CO-15", "Error", "totals disagree", "/x", ["BT-112"], "EN16931-UBL-validation.xsl")],
                null),
        };

        using var writer = new StringWriter();
        Batch.WriteCsv(writer, reports);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("file,status,ruleSet,schemaValid,errors,warnings,rules,detail", lines[0]);
        Assert.Equal("a.xml,valid,XRechnung,True,0,0,,", lines[1]);
        Assert.Equal("b.xml,invalid,En16931,True,1,0,BR-CO-15,", lines[2]);
    }

    [Fact]
    public void Quotes_csv_fields_that_carry_a_separator()
    {
        var reports = new List<Report>
        {
            new("in,voice.xml", "error", null, false, null, [], "he said \"no\""),
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
            new("d.xml", "valid", "XRechnung", true, null, [], null),
            new("e.xml", "error", null, false, null, [], "not a PDF"),
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
        var reports = new List<Report> { new("a.xml", "invalid", "XRechnung", false, null, [], null) };

        using var writer = new StringWriter();
        Batch.WriteSummary(writer, reports);

        Assert.Contains("did not conform to their XML Schema", writer.ToString());
    }

    private static Report Invalid(string file, string ruleId) => new(
        file, "invalid", "XRechnung", true, null,
        [new Finding(ruleId, nameof(ValidationSeverity.Error), "failed", "/x", [], "artefact.xsl")],
        null);

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
