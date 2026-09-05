using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Verifacta.Reading;

/// <summary>
/// Pulls the embedded invoice XML out of a hybrid PDF/A-3 document (Factur-X, ZUGFeRD 2.x).
/// </summary>
internal static class PdfAttachments
{
    /// <summary>The attachment names the hybrid specifications define, most specific first.</summary>
    private static readonly string[] KnownNames =
    [
        "factur-x.xml",
        "zugferd-invoice.xml",
        "xrechnung.xml",
    ];

    internal static bool LooksLikePdf(ReadOnlySpan<byte> head) =>
        head.Length >= 5 && head[0] == '%' && head[1] == 'P' && head[2] == 'D' && head[3] == 'F' && head[4] == '-';

    internal static (byte[] Content, string FileName) ExtractInvoice(Stream stream)
    {
        var attachments = Read(stream);

        if (attachments.Count == 0)
        {
            throw new UnsupportedDocumentException(
                "The PDF carries no embedded files, so it is not a hybrid e-invoice. " +
                "A Factur-X or ZUGFeRD 2.x document embeds its XML as an attachment.");
        }

        foreach (var known in KnownNames)
        {
            var match = attachments.Keys.FirstOrDefault(
                name => string.Equals(name, known, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return (attachments[match], match);
        }

        var xml = attachments.Keys
            .Where(name => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (xml.Count == 1) return (attachments[xml[0]], xml[0]);

        throw new UnsupportedDocumentException(
            $"The PDF embeds [{string.Join(", ", attachments.Keys)}], none of which is a recognised " +
            $"invoice attachment ({string.Join(", ", KnownNames)}).");
    }

    private static Dictionary<string, byte[]> Read(Stream stream)
    {
        // A malformed PDF can fail anywhere inside the reader, and PDFsharp does not confine itself
        // to PdfReaderException — a truncated file raises ArgumentOutOfRangeException. Callers get
        // one exception type for "this file is not usable" rather than whatever surfaced.
        try
        {
            var attachments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var embedded = document.Internals.Catalog.Elements
                .GetDictionary("/Names")?.Elements
                .GetDictionary("/EmbeddedFiles");

            if (embedded is not null)
            {
                CollectNameTree(embedded, attachments);
            }

            CollectFileAnnotations(document, attachments);
            return attachments;
        }
        catch (Exception exception) when (exception is not (UnsupportedDocumentException or OutOfMemoryException))
        {
            throw new UnsupportedDocumentException(
                $"The PDF could not be read ({exception.GetType().Name}): {exception.Message}");
        }
    }

    /// <summary>
    /// A real embedded-files tree has a handful of nodes. Anything past this is not a hybrid
    /// invoice, and continuing to walk it only serves whoever built the file.
    /// </summary>
    private const int MaxNameTreeNodes = 4096;

    /// <summary>
    /// A PDF name tree is either a flat /Names array or an inner node with /Kids.
    /// Walked iteratively, and never through the same node twice: the file comes from outside, and
    /// a /Kids entry pointing back at an ancestor recursed until the process died on a stack
    /// overflow — which no caller can catch, because .NET does not let one.
    /// </summary>
    private static void CollectNameTree(PdfDictionary root, Dictionary<string, byte[]> attachments)
    {
        var pending = new Stack<PdfDictionary>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var visited = 0;

        pending.Push(root);

        while (pending.Count > 0)
        {
            if (++visited > MaxNameTreeNodes)
            {
                throw new UnsupportedDocumentException(
                    $"The PDF's embedded-file tree has more than {MaxNameTreeNodes} nodes, " +
                    "which no hybrid invoice does.");
            }

            var node = pending.Pop();
            if (!seen.Add(node)) continue;

            var names = node.Elements.GetArray("/Names");
            if (names is not null)
            {
                for (var index = 0; index + 1 < names.Elements.Count; index += 2)
                {
                    var name = names.Elements[index] as PdfString ?? Resolve<PdfString>(names.Elements[index]);
                    var specification = names.Elements.GetDictionary(index + 1)
                        ?? Resolve<PdfDictionary>(names.Elements[index + 1]);

                    if (name is null || specification is null) continue;

                    Add(name.Value, specification, attachments);
                }
            }

            var kids = node.Elements.GetArray("/Kids");
            if (kids is null) continue;

            for (var index = 0; index < kids.Elements.Count; index++)
            {
                var kid = kids.Elements.GetDictionary(index) ?? Resolve<PdfDictionary>(kids.Elements[index]);
                if (kid is not null) pending.Push(kid);
            }
        }
    }

    /// <summary>Some producers attach the XML as a page annotation instead of a document-level file.</summary>
    private static void CollectFileAnnotations(PdfDocument document, Dictionary<string, byte[]> attachments)
    {
        foreach (var page in document.Pages)
        {
            var annotations = page.Elements.GetArray("/Annots");
            if (annotations is null) continue;

            for (var index = 0; index < annotations.Elements.Count; index++)
            {
                var annotation = annotations.Elements.GetDictionary(index)
                    ?? Resolve<PdfDictionary>(annotations.Elements[index]);

                if (annotation?.Elements.GetName("/Subtype") != "/FileAttachment") continue;

                var specification = annotation.Elements.GetDictionary("/FS");
                if (specification is not null) Add(null, specification, attachments);
            }
        }
    }

    private static void Add(string? name, PdfDictionary specification, Dictionary<string, byte[]> attachments)
    {
        var fileName = Clean(name)
            ?? Clean(specification.Elements.GetString("/UF"))
            ?? Clean(specification.Elements.GetString("/F"));

        if (fileName is null) return;

        var files = specification.Elements.GetDictionary("/EF");
        var content = files?.Elements.GetDictionary("/F") ?? files?.Elements.GetDictionary("/UF");
        var bytes = content?.Stream?.UnfilteredValue;

        if (bytes is { Length: > 0 })
        {
            attachments[fileName] = bytes;
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim().Trim('(', ')');
        if (string.IsNullOrEmpty(trimmed)) return null;

        // Specifications may carry a path; the attachment name is the last segment.
        var separator = trimmed.LastIndexOfAny(['/', '\\']);
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    private static T? Resolve<T>(PdfItem? item) where T : PdfItem =>
        (item as PdfReference)?.Value as T;
}
