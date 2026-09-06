using System.Text.RegularExpressions;

namespace Verifacta.Detection;

public enum ProfileKind
{
    Unknown,
    En16931,
    XRechnung,
    PeppolBisBilling3,
    FacturX,
}

public sealed partial class InvoiceProfile
{
    private InvoiceProfile(
        string? specificationIdentifier,
        string? businessProcess,
        ProfileKind kind,
        string? version,
        string? level,
        bool isExtension)
    {
        SpecificationIdentifier = specificationIdentifier;
        BusinessProcess = businessProcess;
        Kind = kind;
        Version = version;
        Level = level;
        IsExtension = isExtension;
    }

    public string? SpecificationIdentifier { get; }

    public string? BusinessProcess { get; }

    public ProfileKind Kind { get; }

    public string? Version { get; }

    public string? Level { get; }

    public bool IsExtension { get; }

    public static InvoiceProfile Parse(string? specificationIdentifier, string? businessProcess = null)
    {
        var id = specificationIdentifier?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return new InvoiceProfile(specificationIdentifier, businessProcess, ProfileKind.Unknown, null, null, false);
        }

        var value = id.ToLowerInvariant();
        var isExtension = value.Contains(":extension:", StringComparison.Ordinal)
            || value.Contains("#conformant#", StringComparison.Ordinal);

        if (XRechnungPattern().Match(value) is { Success: true } xrechnung)
        {
            return new InvoiceProfile(id, businessProcess, ProfileKind.XRechnung, xrechnung.Groups[1].Value, null, isExtension);
        }

        // The version is read from the identifier rather than assumed. A hardcoded "3.0" reported
        // a Peppol BIS 4.0 document as 3.0, which then looked like a profile we fully cover.
        if (PeppolPattern().Match(value) is { Success: true } peppol)
        {
            return new InvoiceProfile(
                id, businessProcess, ProfileKind.PeppolBisBilling3, peppol.Groups[1].Value, null, isExtension);
        }

        if (value.Contains("peppol.eu:2017:poacc:billing", StringComparison.Ordinal))
        {
            return new InvoiceProfile(id, businessProcess, ProfileKind.PeppolBisBilling3, null, null, isExtension);
        }

        if (value.Contains("factur-x.eu", StringComparison.Ordinal) ||
            value.Contains("zugferd", StringComparison.Ordinal))
        {
            return new InvoiceProfile(id, businessProcess, ProfileKind.FacturX, HybridVersion(value), HybridLevel(value), isExtension);
        }

        if (value.StartsWith("urn:cen.eu:en16931:2017", StringComparison.Ordinal))
        {
            return new InvoiceProfile(id, businessProcess, ProfileKind.En16931, "2017", null, isExtension);
        }

        return new InvoiceProfile(id, businessProcess, ProfileKind.Unknown, null, null, false);
    }

    private static string? HybridVersion(string value) =>
        HybridVersionPattern().Match(value) is { Success: true } match
            ? $"{match.Groups[1].Value}.{match.Groups[2].Value}"
            : null;

    private static string? HybridLevel(string value)
    {
        var lastColon = value.LastIndexOf(':');
        if (lastColon < 0 || lastColon == value.Length - 1) return null;

        return value[(lastColon + 1)..];
    }

    // Matches both the current XRechnung URN (urn:xeinkauf.de:kosit:xrechnung_3.0) and the
    // pre-3.0 form (urn:xoev-de:kosit:standard:xrechnung_2.3).
    [GeneratedRegex(@"kosit:(?:standard:|extension:)?xrechnung[_:](\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex XRechnungPattern();

    [GeneratedRegex(@"(\d+)p(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex HybridVersionPattern();

    [GeneratedRegex(@"peppol\.eu:2017:poacc:billing:(\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PeppolPattern();
}
