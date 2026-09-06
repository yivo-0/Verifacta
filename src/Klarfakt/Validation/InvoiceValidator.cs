using System.Collections.Concurrent;
using System.Xml.Linq;
using Saxon.Api;

namespace Klarfakt.Validation;

/// <summary>
/// Validates invoices against the EN 16931, Peppol BIS Billing and XRechnung rule packs.
/// Compiling a rule pack takes seconds and validating an invoice takes milliseconds, so keep one
/// instance for the lifetime of the process — it caches every compiled pack and is thread-safe.
/// </summary>
public sealed class InvoiceValidator
{
    private readonly Processor _processor = new();
    private readonly ConcurrentDictionary<string, Lazy<XsltExecutable>> _compiled = new(StringComparer.OrdinalIgnoreCase);
    private readonly SchemaValidator _schema = new();
    private readonly RulePackCatalog _catalog;

    public InvoiceValidator(RulePackCatalog? catalog = null) => _catalog = catalog ?? RulePackCatalog.Load();

    public RulePackCatalog Catalog => _catalog;

    internal const string ProfileRuleId = "PROFILE";

    /// <summary>
    /// Validates the document against its XML Schema and then the business rules. When
    /// <paramref name="ruleSet"/> is null the rule set is chosen from the document's own
    /// specification identifier. Set <paramref name="validateSchema"/> to false to check business
    /// rules against a document fragment that is not a complete invoice. Set
    /// <paramref name="strict"/> to reject a document whose declared specification has no rule set
    /// here, rather than quietly judging it against EN 16931 alone.
    /// </summary>
    /// <remarks>
    /// Cancellation is coarse. It is observed before the schema and between rule layers, which is
    /// every point there is: a Saxon transform, once started, runs to completion. Over a batch that
    /// is enough to stop promptly; within one large document it is not.
    /// </remarks>
    public ValidationResult Validate(
        InvoiceDocument document,
        RuleSet? ruleSet = null,
        bool validateSchema = true,
        bool strict = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var selected = ruleSet ?? RulePackCatalog.RuleSetFor(document.Profile);
        var profileCovered = RulePackCatalog.Covers(document.Profile);
        var pack = _catalog.Describe(selected, document.Syntax);
        var findings = new List<ValidationFinding>();
        var schemaValid = true;

        // Only when the rule set was ours to choose: a caller naming one explicitly has already
        // decided what this document should be judged against.
        if (strict && ruleSet is null && !profileCovered)
        {
            findings.Add(new ValidationFinding(
                ProfileRuleId,
                ValidationSeverity.Error,
                "fatal",
                $"'{document.Profile.SpecificationIdentifier ?? "(no specification identifier)"}' has no rule set " +
                "in Klarfakt. Judging it against EN 16931 alone would say nothing about the rules it declares.",
                string.Empty,
                string.Empty,
                document.Profile.SpecificationIdentifier,
                null,
                [],
                pack,
                "klarfakt"));
        }

        if (validateSchema)
        {
            var schemaFindings = _schema.Validate(
                document.Xml,
                _catalog.VerifiedPack("schemas"),
                RulePackCatalog.SchemaName(document.Syntax, document.Kind));

            findings.AddRange(schemaFindings);
            schemaValid = schemaFindings.All(finding => finding.Severity != ValidationSeverity.Error);
        }

        if (schemaValid)
        {
            var input = _processor.NewDocumentBuilder().Build(document.Xml.CreateReader());

            var values = Values(input);

            foreach (var layer in _catalog.Layers(selected, document.Syntax))
            {
                cancellationToken.ThrowIfCancellationRequested();
                findings.AddRange(SvrlReader.Read(Transform(layer, input), pack, Path.GetFileName(layer), values));
            }
        }

        return new ValidationResult(selected, [pack], findings, schemaValid, validateSchema, profileCovered);
    }

    /// <summary>
    /// Compiles every rule artefact up front so the first validation is not the one that pays for
    /// it. Only the validation layers: the catalog also holds the visualisation stylesheets, which
    /// belong to <see cref="Rendering.InvoiceRenderer"/> and do not all compile standalone.
    /// </summary>
    public void Warmup()
    {
        foreach (var layer in _catalog.ValidationLayers())
        {
            Compiled(layer);
        }
    }

    /// <summary>
    /// Resolves a finding's own SVRL location back to the value that failed. The rule message says
    /// what is wrong; an application showing it to a user needs to say which value, and SVRL does
    /// not carry one. One compiler per document, because XPathCompiler is not built to be shared
    /// across threads and the locations are almost all distinct, so caching would not pay.
    /// </summary>
    /// <remarks>
    /// The <c>[not(*)]</c> is the whole trick. A Schematron location is the rule's context node,
    /// not the value its test looked at, so it is often an element several levels up — and the
    /// string value of an element with children is every scrap of text beneath it, which is worse
    /// than saying nothing. Only a leaf gets reported.
    /// </remarks>
    private Func<string, string?> Values(XdmNode input)
    {
        var compiler = _processor.NewXPathCompiler();

        return location =>
        {
            if (location.Length == 0) return null;

            try
            {
                var selector = compiler.Compile($"({location})[not(*)]").Load();
                selector.ContextItem = input;

                return selector.EvaluateSingle()?.StringValue;
            }
            catch (SaxonApiException)
            {
                // A location that will not evaluate is not worth failing the validation over; the
                // rule message and the path still stand on their own.
                return null;
            }
        };
    }

    private XDocument Transform(string stylesheet, XdmNode input)
    {
        var transformer = Compiled(stylesheet).Load30();
        // Schematron stylesheets declare global variables that select from the document root, and
        // Xslt30Transformer does not infer the global context item from the match selection.
        transformer.GlobalContextItem = input;
        var destination = new XdmDestination();

        try
        {
            transformer.ApplyTemplates(input, destination);
        }
        catch (SaxonApiException exception)
        {
            throw new ValidationException(
                $"The rule artefact '{Path.GetFileName(stylesheet)}' failed on this document: {exception.Message}",
                exception);
        }

        return XDocument.Parse(destination.XdmNode.ToString());
    }

    // Lazy, not a bare factory: GetOrAdd does not lock, so a cold cache under Parallel.ForEach
    // would otherwise have every thread compile the same multi-second stylesheet at once.
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
