namespace Klarfakt.Model;

public sealed class DocumentTotals
{
    public decimal? LineExtensionAmount { get; set; }

    public decimal? AllowanceTotalAmount { get; set; }

    public decimal? ChargeTotalAmount { get; set; }

    public decimal? TaxExclusiveAmount { get; set; }

    public decimal? TaxTotalAmount { get; set; }

    public decimal? TaxTotalAmountInAccountingCurrency { get; set; }

    public decimal? TaxInclusiveAmount { get; set; }

    public decimal? PrepaidAmount { get; set; }

    public decimal? RoundingAmount { get; set; }

    public decimal? DuePayableAmount { get; set; }
}

public sealed class TaxBreakdownEntry
{
    public string? CategoryCode { get; set; }

    public decimal? Percent { get; set; }

    public decimal? TaxableAmount { get; set; }

    public decimal? TaxAmount { get; set; }

    public string? ExemptionReason { get; set; }

    public string? ExemptionReasonCode { get; set; }

    public DateOnly? TaxPointDate { get; set; }

    public string? DueDateTypeCode { get; set; }
}

public sealed class AllowanceCharge
{
    public bool IsCharge { get; set; }

    public decimal? Amount { get; set; }

    public decimal? BaseAmount { get; set; }

    public decimal? Percentage { get; set; }

    public string? Reason { get; set; }

    public string? ReasonCode { get; set; }

    public string? TaxCategoryCode { get; set; }

    public decimal? TaxPercent { get; set; }
}
