using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Verifacta.Reading;

namespace Verifacta.Validation;

/// <summary>
/// Validates a document against its XML Schema. KoSIT's scenarios run this before Schematron and
/// stop when it fails, because business rules evaluated over a structurally invalid document
/// produce misleading verdicts.
/// </summary>
internal sealed class SchemaValidator
{
    internal const string RuleId = "XSD";

    private readonly ConcurrentDictionary<string, XmlSchemaSet> _sets = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<ValidationFinding> Validate(
        XDocument document,
        string schemaDirectory,
        string artefact,
        string pack)
    {
        var schemas = _sets.GetOrAdd(schemaDirectory, Compile);
        var findings = new List<ValidationFinding>();

        document.Validate(schemas, (sender, args) =>
        {
            var element = sender as XElement ?? (sender as XAttribute)?.Parent;

            findings.Add(new ValidationFinding(
                RuleId,
                args.Severity == XmlSeverityType.Error ? ValidationSeverity.Error : ValidationSeverity.Warning,
                args.Severity.ToString().ToLowerInvariant(),
                args.Message,
                element is null ? string.Empty : element.Path(),
                string.Empty,
                null,
                [],
                pack,
                artefact));
        });

        return findings;
    }

    private static XmlSchemaSet Compile(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new RulePackException(RulePackCatalog.MissingArtefact(directory));
        }

        // Every module is added explicitly and xsd:import is not resolved at all. UBL modules
        // reference each other's prefixes without always importing them, so following imports
        // alone fails to compile; resolving imports *as well* loads some namespaces twice. Feeding
        // the complete module set with resolution off is deterministic, and it guarantees that
        // validating an invoice never reaches the network.
        var set = new XmlSchemaSet { XmlResolver = null };

        // The xmldsig core schema declares an internal DTD of entities, so DTDs are parsed here.
        // These files are hash-verified against the manifest; invoices themselves are still read
        // with DTDs prohibited outright.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*.xsd", SearchOption.AllDirectories).Order())
        {
            using var reader = XmlReader.Create(file, settings);
            set.Add(XmlSchema.Read(reader, null)
                ?? throw new RulePackException($"'{file}' is not a valid XML Schema."));
        }

        set.Compile();
        return set;
    }
}
