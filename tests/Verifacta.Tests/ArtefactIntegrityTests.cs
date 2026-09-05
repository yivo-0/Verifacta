using Verifacta.Rendering;
using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// The claim Verifacta rests on is that it runs the publishers' artefacts unchanged. An altered
/// stylesheet used to compile and validate happily, reporting a verdict that was nobody's but ours.
/// </summary>
[Collection("validator")]
public class ArtefactIntegrityTests(ValidatorFixture fixture) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("verifacta-integrity").FullName;

    /// <summary>
    /// Best effort. A validator keeps the stylesheets it compiled open for as long as it lives, and
    /// nothing here disposes the Saxon processor, so the copy is sometimes still in use when the
    /// test ends. Failing the run over a temp directory would be reporting the cleanup, not the
    /// test — and it did, intermittently, until this caught it.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Refuses_to_validate_against_an_altered_rule_artefact()
    {
        if (!fixture.Available) return;

        var catalog = CopyOfTheRules();
        Corrupt(catalog.Pack("xrechnung").PathTo("XRechnung-UBL-validation.xsl"));

        var exception = Assert.Throws<RulePackException>(
            () => new InvoiceValidator(catalog).Validate(Fixture.Load("xrechnung-ubl.xml")));

        Assert.Contains("does not match the manifest hash", exception.Message);
        Assert.Contains("XRechnung-UBL-validation.xsl", exception.Message);
    }

    [Fact]
    public void Refuses_to_validate_against_an_altered_schema()
    {
        if (!fixture.Available) return;

        var catalog = CopyOfTheRules();
        Corrupt(catalog.Pack("schemas").PathTo("ubl/2.1/xsd/maindoc/UBL-Invoice-2.1.xsd"));

        var exception = Assert.Throws<RulePackException>(
            () => new InvoiceValidator(catalog).Validate(Fixture.Load("xrechnung-ubl.xml")));

        Assert.Contains("does not match the manifest hash", exception.Message);
    }

    [Fact]
    public void Refuses_to_render_with_an_altered_stylesheet()
    {
        if (!fixture.Available) return;

        var catalog = CopyOfTheRules();
        Corrupt(catalog.Pack("visualization").PathTo("xsl/xrechnung-html.xsl"));

        var exception = Assert.Throws<RulePackException>(
            () => new InvoiceRenderer(catalog).ToHtml(Fixture.Load("xrechnung-ubl.xml")));

        Assert.Contains("does not match the manifest hash", exception.Message);
    }

    [Fact]
    public void Reports_an_artefact_that_has_been_removed()
    {
        if (!fixture.Available) return;

        var catalog = CopyOfTheRules();
        File.Delete(catalog.Pack("xrechnung").PathTo("XRechnung-UBL-validation.xsl"));

        var exception = Assert.Throws<RulePackException>(
            () => new InvoiceValidator(catalog).Validate(Fixture.Load("xrechnung-ubl.xml")));

        Assert.Contains("is missing", exception.Message);
    }

    [Fact]
    public void An_untouched_copy_validates_normally()
    {
        if (!fixture.Available) return;

        var result = new InvoiceValidator(CopyOfTheRules()).Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.True(result.IsValid);
    }

    /// <summary>A private copy, so a test that corrupts an artefact cannot touch the real ones.</summary>
    private RulePackCatalog CopyOfTheRules()
    {
        foreach (var source in Directory.EnumerateFiles(fixture.Catalog!.Root, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(_root, Path.GetRelativePath(fixture.Catalog.Root, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }

        return RulePackCatalog.Load(_root);
    }

    private static void Corrupt(string path) => File.AppendAllText(path, "<!-- altered -->");
}
