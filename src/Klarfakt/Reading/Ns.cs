using System.Xml.Linq;

namespace Klarfakt.Reading;

internal static class Ns
{
    internal const string UblInvoiceUri = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    internal const string UblCreditNoteUri = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    internal const string CiiUri = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";

    internal static readonly XNamespace Inv = UblInvoiceUri;
    internal static readonly XNamespace Cn = UblCreditNoteUri;
    internal static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    internal static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    internal static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    internal static readonly XNamespace Rsm = CiiUri;
    internal static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    internal static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    internal static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    private static readonly Dictionary<string, string> Prefixes = new()
    {
        [UblInvoiceUri] = "ubl-invoice",
        [UblCreditNoteUri] = "ubl-creditnote",
        [Cbc.NamespaceName] = "cbc",
        [Cac.NamespaceName] = "cac",
        [Ext.NamespaceName] = "ext",
        [CiiUri] = "rsm",
        [Ram.NamespaceName] = "ram",
        [Udt.NamespaceName] = "udt",
        [Qdt.NamespaceName] = "qdt",
    };

    internal static string Prefix(XNamespace ns) =>
        Prefixes.TryGetValue(ns.NamespaceName, out var p) ? p : string.Empty;
}
