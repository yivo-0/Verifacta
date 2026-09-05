using System.Globalization;
using System.Xml.Linq;

namespace Verifacta.Reading;

internal sealed class ReadContext
{
    private static readonly string[] CiiDateFormats = ["yyyyMMdd", "yyyyMM", "yyyy-MM-dd"];

    private readonly List<ReadFinding> _findings = [];

    internal IReadOnlyList<ReadFinding> Findings => _findings;

    internal decimal? Decimal(XElement? element)
    {
        var raw = element.Text();
        if (raw is null) return null;

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        Add(element!, ReadFindingKind.UnparsableNumber, $"'{raw}' is not a valid decimal.");
        return null;
    }

    internal DateOnly? Date(XElement? element)
    {
        var raw = element.Text();
        if (raw is null) return null;

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
        {
            // Read, but reported: the specification requires yyyy-MM-dd, so anything else is an
            // interpretation of the document rather than the document itself.
            var read = DateOnly.FromDateTime(fallback);
            Add(element!, ReadFindingKind.UnexpectedValue, $"'{raw}' is not an ISO date; read as {read:yyyy-MM-dd}.");
            return read;
        }

        Add(element!, ReadFindingKind.UnparsableDate, $"'{raw}' is not a valid date.");
        return null;
    }

    internal DateOnly? CiiDate(XElement? wrapper)
    {
        if (wrapper is null) return null;

        var stringElement = wrapper.Element(Ns.Udt + "DateTimeString") ?? wrapper.Element(Ns.Udt + "DateString");
        var raw = (stringElement ?? wrapper).Text();
        if (raw is null) return null;

        var target = stringElement ?? wrapper;
        var format = target.Attr("format");

        if (format == "102" && DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateOnly.TryParseExact(raw, CiiDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
        {
            var read = DateOnly.FromDateTime(fallback);
            Add(target, ReadFindingKind.UnexpectedValue,
                $"'{raw}' is not a date in format '{format ?? "102"}'; read as {read:yyyy-MM-dd}.");
            return read;
        }

        Add(target, ReadFindingKind.UnparsableDate, $"'{raw}' is not a valid date" + (format is null ? "." : $" for format '{format}'."));
        return null;
    }

    internal bool? Indicator(XElement? element)
    {
        var raw = element.Text();
        if (raw is null) return null;

        return raw.ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => Unexpected(element!, raw),
        };

        bool? Unexpected(XElement target, string value)
        {
            Add(target, ReadFindingKind.UnexpectedValue, $"'{value}' is not a valid indicator.");
            return null;
        }
    }

    internal byte[]? Binary(XElement? element)
    {
        var raw = element.Text();
        if (raw is null) return null;

        try
        {
            return Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            Add(element!, ReadFindingKind.UnexpectedValue, "Embedded document is not valid base64.");
            return null;
        }
    }

    internal void Add(XElement element, ReadFindingKind kind, string message) =>
        _findings.Add(new ReadFinding(kind, element.Path(), message));
}
