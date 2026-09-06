using System.IO.Compression;
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

    internal static (byte[] Content, string FileName) ExtractInvoice(Stream stream, DocumentLimits limits)
    {
        var attachments = Read(stream, limits);

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

    private static Dictionary<string, byte[]> Read(Stream stream, DocumentLimits limits)
    {
        // A malformed PDF can fail anywhere inside the reader, and PDFsharp does not confine itself
        // to PdfReaderException — a truncated file raises ArgumentOutOfRangeException. Callers get
        // one exception type for "this file is not usable" rather than whatever surfaced.
        try
        {
            var attachments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var budget = new Budget(limits);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var embedded = document.Internals.Catalog.Elements
                .GetDictionary("/Names")?.Elements
                .GetDictionary("/EmbeddedFiles");

            if (embedded is not null)
            {
                CollectNameTree(embedded, attachments, limits, budget);
            }

            CollectFileAnnotations(document, attachments, limits, budget);
            return attachments;
        }
        catch (Exception exception) when (exception is not (UnsupportedDocumentException or OutOfMemoryException))
        {
            throw new UnsupportedDocumentException(
                $"The PDF could not be read ({exception.GetType().Name}): {exception.Message}");
        }
    }

    /// <summary>
    /// A PDF name tree is either a flat /Names array or an inner node with /Kids.
    /// Walked iteratively, and never through the same node twice: the file comes from outside, and
    /// a /Kids entry pointing back at an ancestor recursed until the process died on a stack
    /// overflow — which no caller can catch, because .NET does not let one.
    /// </summary>
    private static void CollectNameTree(
        PdfDictionary root, Dictionary<string, byte[]> attachments, DocumentLimits limits, Budget budget)
    {
        var pending = new Stack<PdfDictionary>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var visited = 0;

        pending.Push(root);

        while (pending.Count > 0)
        {
            if (++visited > limits.MaxPdfNameTreeNodes)
            {
                throw new UnsupportedDocumentException(
                    $"The PDF's embedded-file tree has more than {limits.MaxPdfNameTreeNodes} nodes, " +
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

                    Add(name.Value, specification, attachments, limits, budget);
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
    private static void CollectFileAnnotations(
        PdfDocument document, Dictionary<string, byte[]> attachments, DocumentLimits limits, Budget budget)
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
                if (specification is not null) Add(null, specification, attachments, limits, budget);
            }
        }
    }

    private static void Add(
        string? name, PdfDictionary specification, Dictionary<string, byte[]> attachments,
        DocumentLimits limits, Budget budget)
    {
        var fileName = Clean(name)
            ?? Clean(specification.Elements.GetString("/UF"))
            ?? Clean(specification.Elements.GetString("/F"));

        if (fileName is null) return;

        var files = specification.Elements.GetDictionary("/EF");
        var content = files?.Elements.GetDictionary("/F") ?? files?.Elements.GetDictionary("/UF");
        var bytes = Content(content, fileName, limits);

        if (bytes is not { Length: > 0 }) return;

        budget.Take(fileName, bytes.LongLength);
        attachments[fileName] = bytes;
    }

    /// <summary>
    /// What is left of the per-document extraction allowance. A cap on one attachment bounds
    /// nothing on its own — a PDF may carry as many embedded files as it likes, each just under it.
    /// </summary>
    private sealed class Budget(DocumentLimits limits)
    {
        private long _taken;

        internal void Take(string fileName, long bytes)
        {
            _taken += bytes;

            if (_taken > limits.MaxTotalAttachmentBytes)
            {
                throw new UnsupportedDocumentException(
                    $"The document's attachments come to over {_taken:N0} bytes by '{fileName}', past the " +
                    $"{limits.MaxTotalAttachmentBytes:N0} byte limit. Raise " +
                    "DocumentLimits.MaxTotalAttachmentBytes if this is genuinely an invoice.");
            }
        }
    }

    /// <summary>
    /// The attachment's bytes, decompressed no further than the limit allows.
    /// </summary>
    /// <remarks>
    /// PDFsharp's UnfilteredValue hands back the whole decompressed attachment, so checking the
    /// limit afterwards charges the memory before refusing it: 261 KB of deflate is 256 MB of
    /// array, and a 2.5 MB file reaches the point where .NET refuses the allocation for us. Flate
    /// is inflated here instead, a block at a time, and abandoned the moment it goes over.
    /// </remarks>
    private static byte[]? Content(PdfDictionary? content, string fileName, DocumentLimits limits)
    {
        if (content?.Stream is not { } stream) return null;

        var bytes = IsPlainFlate(content)
            ? Inflate(stream.Value, fileName, limits)
            : stream.UnfilteredValue;

        if (bytes is not null && bytes.LongLength > limits.MaxAttachmentBytes)
        {
            throw TooLarge(fileName, bytes.LongLength, limits);
        }

        return bytes;
    }

    /// <summary>
    /// Flate with no decode parameters, which is what every hybrid invoice in the corpus uses.
    /// Anything else — a predictor, a filter chain, an unusual filter — goes through PDFsharp,
    /// because guessing at it wrongly would lose a readable invoice to save memory.
    /// </summary>
    private static bool IsPlainFlate(PdfDictionary content) =>
        content.Elements.GetName("/Filter") == "/FlateDecode" &&
        !content.Elements.ContainsKey("/DecodeParms") &&
        !content.Elements.ContainsKey("/DP");

    private static byte[] Inflate(byte[] raw, string fileName, DocumentLimits limits)
    {
        foreach (var zlib in new[] { true, false })
        {
            try
            {
                using var source = new MemoryStream(raw);
                using Stream inflater = zlib
                    ? new ZLibStream(source, CompressionMode.Decompress)
                    : new DeflateStream(source, CompressionMode.Decompress);

                return ReadBounded(inflater, fileName, limits);
            }
            catch (InvalidDataException) when (zlib)
            {
                // A few producers write raw deflate where the specification says zlib.
            }
        }

        throw new UnsupportedDocumentException($"The attachment '{fileName}' is not readable Flate data.");
    }

    private static byte[] ReadBounded(Stream source, string fileName, DocumentLimits limits)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limits.MaxAttachmentBytes)
            {
                throw TooLarge(fileName, buffer.Length + read, limits, atLeast: true);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static UnsupportedDocumentException TooLarge(
        string fileName, long size, DocumentLimits limits, bool atLeast = false) =>
        new($"The attachment '{fileName}' is {(atLeast ? "over " : string.Empty)}{size:N0} bytes, " +
            $"past the {limits.MaxAttachmentBytes:N0} byte limit. " +
            "Raise DocumentLimits.MaxAttachmentBytes if this is genuinely an invoice.");

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
