namespace Klarfakt.Model;

public sealed class EInvoice
{
    public string? Id { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? TypeCode { get; set; }

    public string? CurrencyCode { get; set; }

    public string? TaxCurrencyCode { get; set; }

    public DateOnly? TaxPointDate { get; set; }

    public string? BuyerReference { get; set; }

    public string? ProjectReference { get; set; }

    public string? ContractReference { get; set; }

    public string? PurchaseOrderReference { get; set; }

    public string? SalesOrderReference { get; set; }

    public string? ReceivingAdviceReference { get; set; }

    public string? DespatchAdviceReference { get; set; }

    public string? TenderReference { get; set; }

    public string? AccountingCost { get; set; }

    public string? BusinessProcess { get; set; }

    public string? SpecificationIdentifier { get; set; }

    public DatePeriod? InvoicePeriod { get; set; }

    public List<InvoiceNote> Notes { get; } = [];

    public List<PrecedingInvoiceReference> PrecedingInvoices { get; } = [];

    public Party Seller { get; set; } = new();

    public Party Buyer { get; set; } = new();

    public Party? Payee { get; set; }

    public Party? SellerTaxRepresentative { get; set; }

    public Delivery? Delivery { get; set; }

    public PaymentDetails Payment { get; set; } = new();

    public List<AllowanceCharge> AllowancesAndCharges { get; } = [];

    public List<TaxBreakdownEntry> TaxBreakdown { get; } = [];

    public DocumentTotals Totals { get; set; } = new();

    public List<Attachment> Attachments { get; } = [];

    public List<InvoiceLine> Lines { get; } = [];
}

public sealed class DatePeriod
{
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}

public sealed class PrecedingInvoiceReference
{
    public string? Id { get; set; }

    public DateOnly? IssueDate { get; set; }
}

public sealed class Delivery
{
    public string? Name { get; set; }

    public Identifier? LocationIdentifier { get; set; }

    public DateOnly? ActualDeliveryDate { get; set; }

    public PostalAddress? Address { get; set; }
}

public sealed class Attachment
{
    public string? DocumentIdentifier { get; set; }

    public string? Description { get; set; }

    public string? ExternalUri { get; set; }

    public string? FileName { get; set; }

    public string? MimeCode { get; set; }

    public byte[]? Content { get; set; }
}

public sealed class InvoiceNote
{
    public string? SubjectCode { get; set; }

    public string? Text { get; set; }
}
