using System.Text.RegularExpressions;
using Verifacta.Reading;

namespace Verifacta.Validation;

/// <summary>
/// Rewrites the SVRL location Saxon emits — <c>/*:Invoice[namespace-uri()='urn:…Invoice-2'][1]</c> —
/// into the prefixed form the rest of Verifacta reports, so read findings and validation findings
/// share one path convention.
/// </summary>
internal static partial class LocationPath
{
    internal static string Normalise(string? rawLocation)
    {
        if (string.IsNullOrWhiteSpace(rawLocation)) return string.Empty;

        var normalised = PredicateStepPattern().Replace(rawLocation, Rewrite);
        return BracedStepPattern().Replace(normalised, Rewrite);
    }

    private static string Rewrite(Match match)
    {
        var local = match.Groups["local"].Value;
        var prefix = Ns.Prefix(match.Groups["ns"].Value);
        var index = match.Groups["index"].Success ? match.Groups["index"].Value : null;

        var step = prefix.Length == 0 ? $"*:{local}" : $"{prefix}:{local}";
        return index is null or "1" ? step : $"{step}[{index}]";
    }

    // The CEN artefacts report steps as *:Name[namespace-uri()='…'].
    [GeneratedRegex(
        @"\*:(?<local>[A-Za-z_][\w.\-]*)\[namespace-uri\(\)='(?<ns>[^']*)'\](?:\[(?<index>\d+)\])?",
        RegexOptions.CultureInvariant)]
    private static partial Regex PredicateStepPattern();

    // The KoSIT artefacts report steps in Saxon's Q{uri}Name notation.
    [GeneratedRegex(
        @"Q\{(?<ns>[^}]*)\}(?<local>[A-Za-z_][\w.\-]*)(?:\[(?<index>\d+)\])?",
        RegexOptions.CultureInvariant)]
    private static partial Regex BracedStepPattern();
}
