using System.Xml;
using System.Xml.Linq;
using Klarfakt.Detection;
using Klarfakt.Reading;

namespace Klarfakt;

public sealed class UnsupportedDocumentException : Exception
{
    public UnsupportedDocumentException(string message) : base(message)
    {
    }
}

public sealed class InvoiceDocument
{
    // Comments and processing instructions are kept: a validator must judge the document as it
    // arrived, and discarding nodes changes the outcome of rules that count or measure content.
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
    };

    private readonly XElement _root;

    private InvoiceDocument(
        XDocument xml,
        XElement root,
        InvoiceSyntax syntax,
        DocumentKind kind,
        InvoiceProfile profile,
        string? embeddedFileName)
    {
        Xml = xml;
        _root = root;
        Syntax = syntax;
        Kind = kind;
        Profile = profile;
        EmbeddedFileName = embeddedFileName;
    }

    public XDocument Xml { get; }

    public InvoiceSyntax Syntax { get; }

    public DocumentKind Kind { get; }

    public InvoiceProfile Profile { get; }

    /// <summary>
    /// The attachment the XML was taken from when the source was a hybrid PDF, otherwise null.
    /// </summary>
    public string? EmbeddedFileName { get; }

    /// <summary>Loads an invoice from XML, or from the XML embedded in a hybrid PDF/A-3 file.</summary>
    public static InvoiceDocument Load(string path, DocumentLimits? limits = null)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, limits);
    }

    /// <summary>Loads an invoice from XML, or from the XML embedded in a hybrid PDF/A-3 file.</summary>
    public static InvoiceDocument Load(Stream stream, DocumentLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= DocumentLimits.Default;

        // Buffered because the format is decided from the first bytes and PdfReader needs to seek.
        var buffered = Buffer(stream, limits.MaxBytes);

        var head = new byte[5];
        var read = buffered.Read(head, 0, head.Length);
        buffered.Position = 0;

        if (!PdfAttachments.LooksLikePdf(head.AsSpan(0, read)))
        {
            return Create(ReadXml(buffered), null);
        }

        var (content, fileName) = PdfAttachments.ExtractInvoice(buffered, limits);
        return Create(ReadXml(new MemoryStream(content)), fileName);
    }

    /// <summary>
    /// Copies the stream while counting, so an oversized input is refused as it arrives rather than
    /// after it has already been held in memory.
    /// </summary>
    private static MemoryStream Buffer(Stream stream, long maxBytes)
    {
        var buffered = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffered.Length + read > maxBytes)
            {
                throw new UnsupportedDocumentException(
                    $"The document is larger than the {maxBytes:N0} byte limit. " +
                    "Raise DocumentLimits.MaxBytes if this is genuinely an invoice.");
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        return buffered;
    }

    public static InvoiceDocument Parse(string xml)
    {
        using var textReader = new StringReader(xml);
        using var reader = XmlReader.Create(textReader, ReaderSettings);
        return Create(LoadXml(reader), null);
    }

    private static XDocument ReadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, ReaderSettings);
        return LoadXml(reader);
    }

    /// <summary>
    /// Malformed XML is the same kind of problem as a root element we do not recognise: the file
    /// cannot be used. Callers get one exception type for that, rather than XmlException from a
    /// truncated file and UnsupportedDocumentException from the next one along.
    /// </summary>
    private static XDocument LoadXml(XmlReader reader)
    {
        try
        {
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new UnsupportedDocumentException($"The XML could not be parsed: {exception.Message}");
        }
    }

    public ReadResult Read() => Syntax switch
    {
        InvoiceSyntax.Ubl => new UblInvoiceReader(_root, Kind).Read(),
        InvoiceSyntax.Cii => new CiiInvoiceReader(_root).Read(),
        _ => throw new UnsupportedDocumentException($"Syntax '{Syntax}' has no reader."),
    };

    private static InvoiceDocument Create(XDocument xml, string? embeddedFileName)
    {
        var root = xml.Root
            ?? throw new UnsupportedDocumentException("The document has no root element.");

        if (root.Name == Ns.Inv + "Invoice")
        {
            return new InvoiceDocument(xml, root, InvoiceSyntax.Ubl, DocumentKind.Invoice, UblProfile(root), embeddedFileName);
        }

        if (root.Name == Ns.Cn + "CreditNote")
        {
            return new InvoiceDocument(xml, root, InvoiceSyntax.Ubl, DocumentKind.CreditNote, UblProfile(root), embeddedFileName);
        }

        if (root.Name == Ns.Rsm + "CrossIndustryInvoice")
        {
            var typeCode = root
                .Descend(Ns.Rsm + "ExchangedDocument", Ns.Ram + "TypeCode").Text();
            var kind = typeCode is "381" or "261" or "396" ? DocumentKind.CreditNote : DocumentKind.Invoice;
            return new InvoiceDocument(xml, root, InvoiceSyntax.Cii, kind, CiiProfile(root), embeddedFileName);
        }

        throw new UnsupportedDocumentException(
            $"Root element '{root.Name}' is not a UBL Invoice, UBL CreditNote or CII CrossIndustryInvoice.");
    }

    private static InvoiceProfile UblProfile(XElement root) => InvoiceProfile.Parse(
        root.El(Ns.Cbc + "CustomizationID").Text(),
        root.El(Ns.Cbc + "ProfileID").Text());

    private static InvoiceProfile CiiProfile(XElement root)
    {
        var context = root.El(Ns.Rsm + "ExchangedDocumentContext");
        return InvoiceProfile.Parse(
            context.Descend(Ns.Ram + "GuidelineSpecifiedDocumentContextParameter", Ns.Ram + "ID").Text(),
            context.Descend(Ns.Ram + "BusinessProcessSpecifiedDocumentContextParameter", Ns.Ram + "ID").Text());
    }
}
