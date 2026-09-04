using System.Text;
using System.Xml.Linq;

namespace Verifacta.Reading;

internal static class Xml
{
    internal static XElement? El(this XElement? parent, XName name) => parent?.Element(name);

    internal static IEnumerable<XElement> Els(this XElement? parent, XName name) =>
        parent?.Elements(name) ?? [];

    internal static XElement? Descend(this XElement? parent, params XName[] path)
    {
        var current = parent;
        foreach (var name in path)
        {
            current = current?.Element(name);
            if (current is null) return null;
        }

        return current;
    }

    internal static string? Text(this XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static string? Attr(this XElement? element, string name)
    {
        var value = element?.Attribute(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static string Path(this XElement element)
    {
        var parts = new List<string>();
        for (var current = element; current is not null; current = current.Parent)
        {
            var prefix = Ns.Prefix(current.Name.Namespace);
            var name = prefix.Length == 0 ? current.Name.LocalName : $"{prefix}:{current.Name.LocalName}";
            var siblings = current.Parent?.Elements(current.Name).ToList();
            if (siblings is { Count: > 1 })
            {
                name += $"[{siblings.IndexOf(current) + 1}]";
            }

            parts.Add(name);
        }

        parts.Reverse();
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append('/').Append(part);
        }

        return builder.ToString();
    }
}
