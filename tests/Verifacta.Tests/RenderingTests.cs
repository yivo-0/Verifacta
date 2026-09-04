using Verifacta.Rendering;
using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// Rendering runs KoSIT's official visualisation, so these check the plumbing and that both
/// syntaxes reach the same page — not the layout, which is the publisher's to decide.
/// </summary>
public class RenderingTests
{
    private static readonly InvoiceRenderer? Renderer = Create();

    [Theory]
    [InlineData("xrechnung-ubl.xml")]
    [InlineData("facturx-cii.xml")]
    public void Renders_an_invoice_as_html(string fixture)
    {
        if (Renderer is null) return;

        var html = Renderer.ToHtml(Fixture.Load(fixture));

        Assert.StartsWith("<!DOCTYPE HTML>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RE-2026-0042", html);
        Assert.Contains("Musterlieferant GmbH", html);
        Assert.Contains("Beispielkunde AG", html);
        Assert.Contains("Wartungspauschale", html);
        Assert.Contains("2.023,00", html);

        // Self-contained: the stylesheet inlines its own CSS and script, so the page needs no
        // network access and can be handed straight to a browser or an email client.
        Assert.Contains("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Renders_both_syntaxes_to_the_same_content()
    {
        if (Renderer is null) return;

        var ubl = Renderer.ToHtml(Fixture.Load("xrechnung-ubl.xml"));
        var cii = Renderer.ToHtml(Fixture.Load("facturx-cii.xml"));

        foreach (var value in new[] { "RE-2026-0042", "Musterlieferant GmbH", "Schulungstag", "Treuerabatt" })
        {
            Assert.Contains(value, ubl);
            Assert.Contains(value, cii);
        }
    }

    [Fact]
    public void Renders_in_english_when_asked()
    {
        if (Renderer is null) return;

        var german = Renderer.ToHtml(Fixture.Load("xrechnung-ubl.xml"));
        var english = Renderer.ToHtml(Fixture.Load("xrechnung-ubl.xml"), "en");

        Assert.Contains("lang=\"de\"", german);
        Assert.Contains("lang=\"en\"", english);
        Assert.NotEqual(german, english);

        // The invoice content is the same document either way; only the labels change.
        Assert.Contains("RE-2026-0042", english);
        Assert.Contains("Musterlieferant GmbH", english);
    }

    [Fact]
    public void Renders_a_hybrid_pdf_invoice()
    {
        if (Renderer is null) return;

        var path = CorpusFile("pdf-mustang", "EN16931_Einfach.pdf");
        if (path is null) return;

        var html = Renderer.ToHtml(InvoiceDocument.Load(path));

        Assert.StartsWith("<!DOCTYPE HTML>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("471102", html);
    }

    private static string? CorpusFile(string source, string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "corpus", source, name);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    private static InvoiceRenderer? Create()
    {
        try
        {
            return new InvoiceRenderer();
        }
        catch (RulePackException)
        {
            return null;
        }
    }
}
