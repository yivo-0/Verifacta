using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verifacta.Detection;

namespace Verifacta.Validation;

public sealed class RulePackException : Exception
{
    public RulePackException(string message) : base(message)
    {
    }
}

public sealed class RulePackFile
{
    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("origin")]
    public string Origin { get; init; } = string.Empty;
}

public sealed class RulePack
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repository { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("licence")]
    public string Licence { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public Dictionary<string, RulePackFile> Files { get; init; } = [];

    [JsonIgnore]
    public string Directory { get; internal set; } = string.Empty;

    /// <summary>Manifest names use forward slashes; the schema packs are nested.</summary>
    public string PathTo(string name) =>
        Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar));

    public override string ToString() => $"{Id} {Version}";
}

public sealed class RulePackCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, RulePack> _packs;

    private RulePackCatalog(string root, IEnumerable<RulePack> packs)
    {
        Root = root;
        _packs = packs.ToDictionary(pack => pack.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Root { get; }

    public IReadOnlyCollection<RulePack> Packs => _packs.Values;

    /// <summary>
    /// Loads <c>rules/manifest.json</c>. When <paramref name="rulesDirectory"/> is null the
    /// directory is located by walking up from the running assembly, which is how the tests and
    /// the CLI find it after <c>tools/fetch-rules.py</c> has run.
    /// </summary>
    public static RulePackCatalog Load(string? rulesDirectory = null)
    {
        var root = rulesDirectory ?? Locate()
            ?? throw new RulePackException(
                "No 'rules' directory found. Run 'python tools/fetch-rules.py' to download the rule packs.");

        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new RulePackException($"No manifest at '{manifestPath}'. Run 'python tools/fetch-rules.py'.");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), SerializerOptions)
            ?? throw new RulePackException($"'{manifestPath}' is not a valid rule pack manifest.");

        foreach (var pack in manifest.Packs)
        {
            pack.Directory = Path.Combine(root, pack.Id, pack.Version);
        }

        return new RulePackCatalog(root, manifest.Packs);
    }

    public RulePack Pack(string id) => _packs.TryGetValue(id, out var pack)
        ? pack
        : throw new RulePackException($"Rule pack '{id}' is not in the manifest.");

    /// <summary>
    /// Confirms every artefact is present and matches the SHA-256 recorded when it was fetched.
    /// </summary>
    public void VerifyIntegrity()
    {
        foreach (var pack in _packs.Values)
        {
            foreach (var (name, expected) in pack.Files)
            {
                var path = pack.PathTo(name);
                if (!File.Exists(path))
                {
                    throw new RulePackException($"{pack} is missing '{name}'. Re-run 'python tools/fetch-rules.py'.");
                }

                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RulePackException(
                        $"{pack} artefact '{name}' does not match the manifest hash. Expected {expected.Sha256}, found {actual}.");
                }
            }
        }
    }

    public static RuleSet RuleSetFor(InvoiceProfile profile) => profile.Kind switch
    {
        ProfileKind.XRechnung => RuleSet.XRechnung,
        ProfileKind.PeppolBisBilling3 => RuleSet.PeppolBisBilling3,
        _ => RuleSet.En16931,
    };

    internal string SchemaDirectory => Pack("schemas").Directory;

    /// <summary>The schema a document of this shape is judged against, for labelling findings.</summary>
    internal static string SchemaName(InvoiceSyntax syntax, DocumentKind kind) => (syntax, kind) switch
    {
        (InvoiceSyntax.Ubl, DocumentKind.Invoice) => "UBL-Invoice-2.1.xsd",
        (InvoiceSyntax.Ubl, DocumentKind.CreditNote) => "UBL-CreditNote-2.1.xsd",
        (InvoiceSyntax.Cii, _) => "CrossIndustryInvoice_100pD16B.xsd",
        _ => throw new RulePackException($"No schema for {syntax} {kind}."),
    };

    internal IReadOnlyList<string> Layers(RuleSet ruleSet, InvoiceSyntax syntax)
    {
        var (packId, files) = (ruleSet, syntax) switch
        {
            (RuleSet.En16931, InvoiceSyntax.Ubl) => ("cen-en16931", new[] { "EN16931-UBL-validation.xslt" }),
            (RuleSet.En16931, InvoiceSyntax.Cii) => ("cen-en16931", ["EN16931-CII-validation.xslt"]),
            (RuleSet.PeppolBisBilling3, InvoiceSyntax.Ubl) =>
                ("peppol-bis", ["CEN-EN16931-UBL.xslt", "PEPPOL-EN16931-UBL.xslt"]),
            (RuleSet.XRechnung, InvoiceSyntax.Ubl) =>
                ("xrechnung", ["EN16931-UBL-validation.xsl", "XRechnung-UBL-validation.xsl"]),
            (RuleSet.XRechnung, InvoiceSyntax.Cii) =>
                ("xrechnung", ["EN16931-CII-validation.xsl", "XRechnung-CII-validation.xsl"]),
            (RuleSet.PeppolBisBilling3, InvoiceSyntax.Cii) => throw new RulePackException(
                "Peppol BIS Billing 3.0 is defined for UBL only; validate a CII invoice against EN 16931 or XRechnung."),
            _ => throw new RulePackException($"No rule pack for {ruleSet} in {syntax} syntax."),
        };

        var pack = Pack(packId);
        return files.Select(pack.PathTo).ToList();
    }

    internal string Describe(RuleSet ruleSet, InvoiceSyntax syntax)
    {
        var directory = Path.GetDirectoryName(Layers(ruleSet, syntax)[0])!;
        var pack = _packs.Values.First(candidate => candidate.Directory == directory);
        return $"{pack.Id} {pack.Version} ({pack.Repository}@{pack.Tag})";
    }

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "rules");
            if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    private sealed class Manifest
    {
        [JsonPropertyName("packs")]
        public List<RulePack> Packs { get; init; } = [];
    }
}
