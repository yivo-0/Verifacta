using System.Collections.Concurrent;
using System.Xml.Linq;
using Saxon.Api;

namespace Verifacta.Validation;

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
    public ValidationResult Validate(
        InvoiceDocument document,
        RuleSet? ruleSet = null,
        bool validateSchema = true,
        bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(document);

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
                "in Verifacta. Judging it against EN 16931 alone would say nothing about the rules it declares.",
                string.Empty,
                string.Empty,
                null,
                [],
                pack,
                "verifacta"));
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

            foreach (var layer in _catalog.Layers(selected, document.Syntax))
            {
                findings.AddRange(SvrlReader.Read(Transform(layer, input), pack, Path.GetFileName(layer)));
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
