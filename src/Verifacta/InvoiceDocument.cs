using System.Xml;
using System.Xml.Linq;
using Verifacta.Detection;
using Verifacta.Reading;

namespace Verifacta;

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
    public static InvoiceDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Loads an invoice from XML, or from the XML embedded in a hybrid PDF/A-3 file.</summary>
    public static InvoiceDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Buffered because the format is decided from the first bytes and PdfReader needs to seek.
        var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;

        var head = new byte[5];
        var read = buffered.Read(head, 0, head.Length);
        buffered.Position = 0;

        if (!PdfAttachments.LooksLikePdf(head.AsSpan(0, read)))
        {
            return Create(ReadXml(buffered), null);
        }

        var (content, fileName) = PdfAttachments.ExtractInvoice(buffered);
        return Create(ReadXml(new MemoryStream(content)), fileName);
    }

    public static InvoiceDocument Parse(string xml)
    {
        using var textReader = new StringReader(xml);
        using var reader = XmlReader.Create(textReader, ReaderSettings);
        return Create(XDocument.Load(reader, LoadOptions.SetLineInfo), null);
    }

    private static XDocument ReadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, ReaderSettings);
        return XDocument.Load(reader, LoadOptions.SetLineInfo);
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
