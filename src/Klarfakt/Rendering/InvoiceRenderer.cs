using System.Collections.Concurrent;
using Saxon.Api;
using Klarfakt.Detection;
using Klarfakt.Validation;

namespace Klarfakt.Rendering;

public sealed class RenderingException : Exception
{
    public RenderingException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Renders a received invoice as HTML using KoSIT's official XRechnung visualisation, so what a
/// user sees matches the reference rendering rather than an interpretation of it.
/// Compiling the stylesheets takes seconds and rendering takes milliseconds, so keep one instance
/// for the lifetime of the process — it caches every compiled stylesheet and is thread-safe.
/// </summary>
public sealed class InvoiceRenderer
{
    private readonly Processor _processor = new();
    private readonly ConcurrentDictionary<string, Lazy<XsltExecutable>> _compiled = new(StringComparer.OrdinalIgnoreCase);
    private readonly RulePackCatalog _catalog;

    public InvoiceRenderer(RulePackCatalog? catalog = null) => _catalog = catalog ?? RulePackCatalog.Load();

    /// <summary>
    /// Renders the invoice as a standalone HTML document. The publisher ships German and English
    /// label catalogues; <paramref name="language"/> selects between them.
    /// </summary>
    public string ToHtml(InvoiceDocument document, string language = "de")
    {
        using var writer = new StringWriter();
        ToHtml(document, writer, language);
        return writer.ToString();
    }

    /// <summary>Renders the invoice as HTML into <paramref name="writer"/>.</summary>
    public void ToHtml(InvoiceDocument document, TextWriter writer, string language = "de")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        var pack = _catalog.VerifiedPack("visualization");
        var input = _processor.NewDocumentBuilder().Build(document.Xml.CreateReader());

        // Two steps, as the publisher specifies: the syntax is first mapped to the intermediate
        // "xr" semantic model, which the HTML stylesheet then renders.
        var semantic = Transform(pack.PathTo(SemanticStylesheet(document)), input);
        Serialise(pack.PathTo("xsl/xrechnung-html.xsl"), semantic, writer, language);
    }

    /// <summary>Compiles the stylesheets up front so the first render is not the one that pays.</summary>
    public void Warmup()
    {
        var pack = _catalog.VerifiedPack("visualization");

        // Only the entry points. The pack also carries imported modules, CSS and script, none of
        // which compile on their own -- xr-content.xsl alone fails on undeclared variables.
        foreach (var name in EntryStylesheets)
        {
            Compiled(pack.PathTo(name));
        }
    }

    private static readonly string[] EntryStylesheets =
    [
        "xsl/ubl-invoice-xr.xsl",
        "xsl/ubl-creditnote-xr.xsl",
        "xsl/cii-xr.xsl",
        "xsl/xrechnung-html.xsl",
    ];

    private static string SemanticStylesheet(InvoiceDocument document) => (document.Syntax, document.Kind) switch
    {
        (InvoiceSyntax.Ubl, DocumentKind.Invoice) => "xsl/ubl-invoice-xr.xsl",
        (InvoiceSyntax.Ubl, DocumentKind.CreditNote) => "xsl/ubl-creditnote-xr.xsl",
        (InvoiceSyntax.Cii, _) => "xsl/cii-xr.xsl",
        _ => throw new RenderingException($"No visualisation stylesheet for {document.Syntax} {document.Kind}."),
    };

    private XdmNode Transform(string stylesheet, XdmNode input)
    {
        var transformer = Compiled(stylesheet).Load30();
        transformer.GlobalContextItem = input;
        var destination = new XdmDestination();

        try
        {
            transformer.ApplyTemplates(input, destination);
        }
        catch (SaxonApiException exception)
        {
            throw new RenderingException(
                $"'{Path.GetFileName(stylesheet)}' failed on this document: {exception.Message}", exception);
        }

        return destination.XdmNode;
    }

    private void Serialise(string stylesheet, XdmNode input, TextWriter writer, string language)
    {
        var transformer = Compiled(stylesheet).Load30();
        transformer.GlobalContextItem = input;
        transformer.SetStylesheetParameters(new Dictionary<QName, XdmValue>
        {
            [new QName("lang")] = new XdmAtomicValue(language),
        });

        // A Serializer honours the stylesheet's own xsl:output, so the result is real HTML rather
        // than the XML serialisation of the result tree.
        var serializer = _processor.NewSerializer(writer);

        try
        {
            transformer.ApplyTemplates(input, serializer);
        }
        catch (SaxonApiException exception)
        {
            throw new RenderingException(
                $"'{Path.GetFileName(stylesheet)}' failed on this document: {exception.Message}", exception);
        }
    }

    // Lazy, not a bare factory: GetOrAdd does not lock, so a cold cache rendering a folder in
    // parallel would otherwise have every thread compile the same stylesheet at once.
    private XsltExecutable Compiled(string stylesheet) => _compiled.GetOrAdd(
        stylesheet,
        path => new Lazy<XsltExecutable>(() =>
        {
            if (!File.Exists(path))
            {
                throw new RulePackException(RulePackCatalog.MissingArtefact(path));
            }

            return _processor.NewXsltCompiler().Compile(new Uri(path));
        })).Value;
}
