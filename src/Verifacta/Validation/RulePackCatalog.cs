using System.Collections.Concurrent;
using System.Reflection;
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

    public RulePackException(string message, Exception inner) : base(message, inner)
    {
    }
}

public sealed class RulePackFile
{
    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Where the artefact came from, as "release-asset.zip!path/inside/the/archive".</summary>
    [JsonPropertyName("origin")]
    public string Origin { get; init; } = string.Empty;

    internal string Asset => Origin.Split('!', 2)[0];

    internal string Member => Origin.Split('!', 2) is [_, var member] ? member : string.Empty;
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

    /// <summary>Manifest names use forward slashes; the schema pack is nested.</summary>
    public string PathTo(string name) =>
        Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar));

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>
/// The rule packs Verifacta validates against. The manifest is embedded in this assembly and names
/// every artefact, its upstream release and its SHA-256; the artefacts themselves are downloaded by
/// <see cref="RestoreAsync"/> because their licences are not ours to redistribute.
/// </summary>
public sealed class RulePackCatalog
{
    private const string ManifestResource = "Verifacta.RulePacks.json";
    private const string RestoreHint = "Run 'verifacta rules restore', or call RulePackCatalog.RestoreAsync().";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, RulePack> _packs;
    private readonly ConcurrentDictionary<string, bool> _verified = new(StringComparer.OrdinalIgnoreCase);

    private RulePackCatalog(string root, IEnumerable<RulePack> packs)
    {
        Root = root;
        _packs = packs.ToDictionary(pack => pack.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The directory the artefacts live in. It need not exist yet.</summary>
    public string Root { get; }

    public IReadOnlyCollection<RulePack> Packs => _packs.Values;

    /// <summary>True when every artefact named in the manifest is present on disk.</summary>
    public bool IsRestored => _packs.Values.All(pack => pack.Files.Keys.All(name => File.Exists(pack.PathTo(name))));

    /// <summary>
    /// Reads the embedded manifest. When <paramref name="rulesDirectory"/> is null the artefact
    /// directory is the first "rules" folder found from the running assembly upwards, falling back
    /// to "rules" beside the assembly so a restore has somewhere to write.
    /// </summary>
    public static RulePackCatalog Load(string? rulesDirectory = null)
    {
        using var stream = typeof(RulePackCatalog).GetTypeInfo().Assembly.GetManifestResourceStream(ManifestResource)
            ?? throw new RulePackException($"The embedded manifest '{ManifestResource}' is missing from the assembly.");

        var manifest = JsonSerializer.Deserialize<Manifest>(stream, SerializerOptions)
            ?? throw new RulePackException($"The embedded manifest '{ManifestResource}' could not be read.");

        // Normalised so a directory given with forward slashes, or relative, behaves like any
        // other. VERIFACTA_RULES is routinely pasted from a shell or a container path.
        var root = Path.GetFullPath(rulesDirectory ?? Locate() ?? Path.Combine(AppContext.BaseDirectory, "rules"));

        foreach (var pack in manifest.Packs)
        {
            pack.Directory = Path.Combine(root, pack.Id, pack.Version);
        }

        return new RulePackCatalog(root, manifest.Packs);
    }

    public RulePack Pack(string id) => _packs.TryGetValue(id, out var pack)
        ? pack
        : throw new RulePackException($"Rule pack '{id}' is not in the manifest.");

    /// <summary>Confirms every artefact is present and matches the SHA-256 recorded in the manifest.</summary>
    public void VerifyIntegrity()
    {
        foreach (var pack in _packs.Values)
        {
            Verify(pack);
        }
    }

    /// <summary>
    /// The pack, checked against the manifest the first time it is used in this process. Compiling
    /// an artefact that has been altered would produce verdicts that are not the publisher's, which
    /// is the one thing this library exists to guarantee, so the check is not opt-in.
    /// </summary>
    internal RulePack VerifiedPack(string id)
    {
        var pack = Pack(id);

        // Recorded only once it has passed, so a pack repaired by a restore in the same process is
        // not held against an earlier failure. Verifying twice under contention is harmless.
        if (!_verified.ContainsKey(id))
        {
            Verify(pack);
            _verified[id] = true;
        }

        return pack;
    }

    private static void Verify(RulePack pack)
    {
        foreach (var (name, expected) in pack.Files)
        {
            var path = pack.PathTo(name);
            if (!File.Exists(path))
            {
                throw new RulePackException($"{pack} is missing '{name}'. {RestoreHint}");
            }

            if (!Matches(path, expected.Sha256))
            {
                throw new RulePackException(
                    $"{pack} artefact '{name}' does not match the manifest hash. {RestoreHint}");
            }
        }
    }

    /// <summary>
    /// Downloads any artefact that is missing or does not match its recorded hash, straight from the
    /// pinned upstream release. Nothing is downloaded unless this is called: a compliance library
    /// should not make network calls behind the caller's back.
    /// </summary>
    /// <returns>The artefacts that were written.</returns>
    public async Task<IReadOnlyList<string>> RestoreAsync(
        bool force = false,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        var owned = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var written = new List<string>();

        try
        {
            foreach (var pack in _packs.Values)
            {
                var outstanding = pack.Files
                    .Where(file => force || !Matches(pack.PathTo(file.Key), file.Value.Sha256))
                    .ToList();

                if (outstanding.Count == 0) continue;

                // One download per release asset, however many artefacts we take out of it.
                foreach (var group in outstanding.GroupBy(file => file.Value.Asset))
                {
                    using var archive = await OpenAssetAsync(client, pack, group.Key, cancellationToken);

                    foreach (var (name, file) in group)
                    {
                        var entry = archive.GetEntry(file.Member)
                            ?? throw new RulePackException(
                                $"'{file.Member}' is not in {group.Key} of {pack.Repository}@{pack.Tag}.");

                        using var content = entry.Open();
                        using var buffer = new MemoryStream();
                        await content.CopyToAsync(buffer, cancellationToken);
                        var bytes = buffer.ToArray();

                        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new RulePackException(
                                $"{pack} artefact '{name}' downloaded from {pack.Repository}@{pack.Tag} " +
                                $"has hash {actual}, but the manifest records {file.Sha256}.");
                        }

                        var path = pack.PathTo(name);
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                        written.Add($"{pack.Id}/{pack.Version}/{name}");
                    }
                }
            }
        }
        finally
        {
            if (owned) client.Dispose();
        }

        return written;
    }

    public static RuleSet RuleSetFor(InvoiceProfile profile) => profile.Kind switch
    {
        ProfileKind.XRechnung => RuleSet.XRechnung,
        ProfileKind.PeppolBisBilling3 => RuleSet.PeppolBisBilling3,
        _ => RuleSet.En16931,
    };

    /// <summary>The profile versions the shipped packs implement, as they appear in an identifier.</summary>
    internal const string XRechnungProfileVersion = "3.0";

    internal const string PeppolProfileVersion = "3.0";

    /// <summary>
    /// Whether the specification a document declares has a rule set of its own here. Anything else
    /// falls back to EN 16931 — a sound verdict on the core rules, and no statement at all about the
    /// national or sector rules the document says it follows.
    /// </summary>
    /// <remarks>
    /// The kind alone is not enough to answer this. One version of each pack is shipped, so
    /// <c>xrechnung_99.0</c> is not covered by the 3.0 rules merely because the name matched. And a
    /// CIUS or extension is declared by suffixing the EN 16931 identifier with <c>#</c> and its own
    /// URN — NLCIUS and every other national profile look exactly like plain EN 16931 to a check
    /// that only reads the kind, which is how they used to be reported as fully covered.
    /// </remarks>
    public static bool Covers(InvoiceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Kind switch
        {
            ProfileKind.XRechnung => profile.Version == XRechnungProfileVersion,
            ProfileKind.PeppolBisBilling3 => profile.Version == PeppolProfileVersion,
            ProfileKind.En16931 => profile.SpecificationIdentifier?.Contains('#') == false,
            _ => false,
        };
    }

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
        var (pack, files) = LayerSource(ruleSet, syntax);
        return files.Select(pack.PathTo).ToList();
    }

    private (RulePack Pack, string[] Files) LayerSource(RuleSet ruleSet, InvoiceSyntax syntax)
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

        return (VerifiedPack(packId), files);
    }

    /// <summary>
    /// Every artefact <see cref="Layers"/> can return, for warming the compiler. The catalog also
    /// carries the visualisation stylesheets, and those must not be compiled here: they include
    /// imported modules that do not compile on their own.
    /// </summary>
    internal IReadOnlyList<string> ValidationLayers()
    {
        var layers = new List<string>();

        foreach (var ruleSet in Enum.GetValues<RuleSet>())
        {
            foreach (var syntax in Enum.GetValues<InvoiceSyntax>())
            {
                try
                {
                    layers.AddRange(Layers(ruleSet, syntax));
                }
                catch (RulePackException)
                {
                    // Not every rule set covers every syntax; Peppol BIS is defined for UBL only.
                }
            }
        }

        return layers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal string Describe(RuleSet ruleSet, InvoiceSyntax syntax)
    {
        var pack = LayerSource(ruleSet, syntax).Pack;
        return $"{pack.Id} {pack.Version} ({pack.Repository}@{pack.Tag})";
    }

    internal static string MissingArtefact(string path) =>
        $"Rule artefact '{path}' is missing. {RestoreHint}";

    private static async Task<System.IO.Compression.ZipArchive> OpenAssetAsync(
        HttpClient client, RulePack pack, string asset, CancellationToken cancellationToken)
    {
        var url = $"https://github.com/{pack.Repository}/releases/download/{pack.Tag}/{asset}";

        try
        {
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            return new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new RulePackException($"Could not download {url}: {exception.Message}", exception);
        }
    }

    private static bool Matches(string path, string sha256) =>
        File.Exists(path) &&
        string.Equals(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            sha256,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An existing "rules" directory at or above the assembly wins. Failing that, in a source
    /// checkout the artefacts belong at the repository root rather than inside bin/, so a
    /// directory holding .git or a solution file is treated as the root. Otherwise they sit beside
    /// the assembly, which is what a deployed application wants.
    /// </summary>
    private static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("VERIFACTA_RULES") is { Length: > 0 } configured)
        {
            return configured;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? repositoryRoot = null;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "rules");
            if (System.IO.Directory.Exists(candidate)) return candidate;

            if (repositoryRoot is null && IsRepositoryRoot(directory))
            {
                repositoryRoot = candidate;
            }

            directory = directory.Parent;
        }

        return repositoryRoot;
    }

    private static bool IsRepositoryRoot(DirectoryInfo directory) =>
        System.IO.Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
        directory.EnumerateFiles("*.sln").Any() ||
        directory.EnumerateFiles("*.slnx").Any();

    private sealed class Manifest
    {
        [JsonPropertyName("packs")]
        public List<RulePack> Packs { get; init; } = [];
    }
}
