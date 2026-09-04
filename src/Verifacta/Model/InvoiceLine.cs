namespace Verifacta.Model;

public sealed class InvoiceLine
{
    public string? Id { get; set; }

    public string? Note { get; set; }

    public string? ObjectIdentifier { get; set; }

    public decimal? Quantity { get; set; }

    public string? QuantityUnitCode { get; set; }

    public decimal? NetAmount { get; set; }

    public string? OrderLineReference { get; set; }

    public string? AccountingCost { get; set; }

    public DatePeriod? Period { get; set; }

    public List<AllowanceCharge> AllowancesAndCharges { get; } = [];

    public LinePrice Price { get; } = new();

    public string? TaxCategoryCode { get; set; }

    public decimal? TaxPercent { get; set; }

    public LineItem Item { get; } = new();
}

public sealed class LinePrice
{
    public decimal? NetAmount { get; set; }

    public decimal? Discount { get; set; }

    public decimal? GrossAmount { get; set; }

    public decimal? BaseQuantity { get; set; }

    public string? BaseQuantityUnitCode { get; set; }
}

public sealed class LineItem
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? SellerIdentifier { get; set; }

    public string? BuyerIdentifier { get; set; }

    public Identifier? StandardIdentifier { get; set; }

    public List<Identifier> ClassificationIdentifiers { get; } = [];

    public string? OriginCountryCode { get; set; }

    public List<ItemAttribute> Attributes { get; } = [];
}

public sealed class ItemAttribute
{
    public string? Name { get; set; }

    public string? Value { get; set; }
}
