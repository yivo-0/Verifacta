using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Verifacta.Validation;

internal static partial class SvrlReader
{
    private static readonly XNamespace Svrl = "http://purl.oclc.org/dsdl/svrl";

    /// <summary>The longest offending value worth carrying; past this it is noise in a log.</summary>
    private const int MaxValueLength = 200;

    internal static IEnumerable<ValidationFinding> Read(
        XDocument report, string rulePack, string artefact, Func<string, string?> valueAt)
    {
        var elements = report.Descendants(Svrl + "failed-assert")
            .Concat(report.Descendants(Svrl + "successful-report"));

        foreach (var element in elements)
        {
            var text = Collapse(element.Element(Svrl + "text")?.Value);
            var ruleId = element.Attribute("id")?.Value ?? RuleIdFrom(text) ?? "(unnamed)";
            var rawLocation = element.Attribute("location")?.Value ?? string.Empty;
            var flag = element.Attribute("flag")?.Value;

            yield return new ValidationFinding(
                ruleId,
                Severity(flag),
                flag,
                StripRulePrefix(text, ruleId),
                LocationPath.Normalise(rawLocation),
                rawLocation,
                Shorten(Collapse(valueAt(rawLocation))),
                element.Attribute("test")?.Value,
                BusinessTermPattern().Matches(text).Select(match => match.Value).Distinct().ToList(),
                rulePack,
                artefact);
        }
    }

    // The artefacts in use emit exactly fatal, warning and information. An unrecognised flag is
    // treated as an error on purpose: a verdict we cannot interpret should not read as a pass.
    private static ValidationSeverity Severity(string? flag) => flag?.ToLowerInvariant() switch
    {
        "information" or "info" or "notice" => ValidationSeverity.Information,
        "warning" or "warn" => ValidationSeverity.Warning,
        _ => ValidationSeverity.Error,
    };

    private static string? RuleIdFrom(string text) =>
        RuleIdPattern().Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    private static string StripRulePrefix(string text, string ruleId)
    {
        var prefix = $"[{ruleId}]";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) return text;

        return text[prefix.Length..].TrimStart('-', ' ');
    }

    private static string? Shorten(string value) => value.Length switch
    {
        0 => null,
        <= MaxValueLength => value,
        _ => value[..MaxValueLength] + "…",
    };

    private static string Collapse(string? value) =>
        value is null ? string.Empty : WhitespacePattern().Replace(value, " ").Trim();

    [GeneratedRegex(@"^\[([A-Za-z0-9\-]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdPattern();

    [GeneratedRegex(@"\b(?:BT|BG)-\d+(?:-\d+)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex BusinessTermPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
