using Klarfakt.Model;

namespace Klarfakt.Tests;

public class CanonicalReadTests
{
    [Fact]
    public void Reads_the_ubl_invoice()
    {
        var invoice = Fixture.Load("xrechnung-ubl.xml").Read().Invoice;

        Assert.Equal("RE-2026-0042", invoice.Id);
        Assert.Equal(new DateOnly(2026, 3, 15), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 4, 14), invoice.DueDate);
        Assert.Equal("380", invoice.TypeCode);
        Assert.Equal("EUR", invoice.CurrencyCode);
        Assert.Equal("04011000-12345-34", invoice.BuyerReference);
        Assert.Equal("Musterlieferant GmbH", invoice.Seller.Name);
        Assert.Equal("DE123456789", invoice.Seller.VatIdentifier);
        Assert.Equal("0088", invoice.Seller.ElectronicAddress?.Scheme);
        Assert.Equal("rechnung@musterlieferant.de", invoice.Seller.Contact?.Email);
        Assert.Equal("Beispielkunde AG", invoice.Buyer.Name);
        Assert.Equal("DE02120300000000202051", invoice.Payment.CreditTransfers[0].AccountIdentifier);
        Assert.Equal(2023.00m, invoice.Totals.DuePayableAmount);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(500.00m, invoice.Lines[0].Price.NetAmount);
        Assert.Equal("04012345678901", invoice.Lines[0].Item.StandardIdentifier?.Value);
    }

    [Fact]
    public void Reads_the_cii_invoice()
    {
        var invoice = Fixture.Load("facturx-cii.xml").Read().Invoice;

        Assert.Equal("RE-2026-0042", invoice.Id);
        Assert.Equal(new DateOnly(2026, 3, 15), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 4, 14), invoice.DueDate);
        Assert.Equal(new DateOnly(2026, 2, 28), invoice.Delivery?.ActualDeliveryDate);
        Assert.Equal("DE987654321", invoice.Buyer.VatIdentifier);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal("Schulungstag", invoice.Lines[1].Item.Name);
    }

    [Fact]
    public void Both_syntaxes_produce_the_same_canonical_invoice()
    {
        var ubl = Summarize(Fixture.Load("xrechnung-ubl.xml").Read().Invoice);
        var cii = Summarize(Fixture.Load("facturx-cii.xml").Read().Invoice);

        Assert.Equal(ubl, cii);
    }

    [Fact]
    public void Reads_both_fixtures_without_findings()
    {
        Assert.Empty(Fixture.Load("xrechnung-ubl.xml").Read().Findings);
        Assert.Empty(Fixture.Load("facturx-cii.xml").Read().Findings);
    }

    private static Dictionary<string, string?> Summarize(EInvoice invoice)
    {
        var summary = new Dictionary<string, string?>
        {
            ["BT-1 Id"] = invoice.Id,
            ["BT-2 IssueDate"] = invoice.IssueDate?.ToString("O"),
            ["BT-3 TypeCode"] = invoice.TypeCode,
            ["BT-5 Currency"] = invoice.CurrencyCode,
            ["BT-9 DueDate"] = invoice.DueDate?.ToString("O"),
            ["BT-10 BuyerReference"] = invoice.BuyerReference,
            ["BT-13 OrderReference"] = invoice.PurchaseOrderReference,
            ["BT-20 PaymentTerms"] = invoice.Payment.Terms,
            ["BT-21 NoteSubject"] = invoice.Notes.SingleOrDefault()?.SubjectCode,
            ["BT-22 Note"] = invoice.Notes.SingleOrDefault()?.Text,
            ["BT-27 SellerName"] = invoice.Seller.Name,
            ["BT-28 SellerTradingName"] = invoice.Seller.TradingName,
            ["BT-30 SellerLegalId"] = invoice.Seller.LegalRegistrationId?.ToString(),
            ["BT-31 SellerVat"] = invoice.Seller.VatIdentifier,
            ["BT-34 SellerEndpoint"] = invoice.Seller.ElectronicAddress?.ToString(),
            ["BT-35 SellerStreet"] = invoice.Seller.Address?.Line1,
            ["BT-37 SellerCity"] = invoice.Seller.Address?.City,
            ["BT-38 SellerPostcode"] = invoice.Seller.Address?.PostalCode,
            ["BT-40 SellerCountry"] = invoice.Seller.Address?.CountryCode,
            ["BT-41 SellerContact"] = invoice.Seller.Contact?.Name,
            ["BT-42 SellerPhone"] = invoice.Seller.Contact?.Phone,
            ["BT-43 SellerEmail"] = invoice.Seller.Contact?.Email,
            ["BT-44 BuyerName"] = invoice.Buyer.Name,
            ["BT-48 BuyerVat"] = invoice.Buyer.VatIdentifier,
            ["BT-49 BuyerEndpoint"] = invoice.Buyer.ElectronicAddress?.ToString(),
            ["BT-50 BuyerStreet"] = invoice.Buyer.Address?.Line1,
            ["BT-52 BuyerCity"] = invoice.Buyer.Address?.City,
            ["BT-72 DeliveryDate"] = invoice.Delivery?.ActualDeliveryDate?.ToString("O"),
            ["BT-81 PaymentMeansCode"] = invoice.Payment.MeansCode,
            ["BT-82 PaymentMeansText"] = invoice.Payment.MeansText,
            ["BT-83 RemittanceInfo"] = invoice.Payment.RemittanceInformation,
            ["BT-84 PayeeAccount"] = invoice.Payment.CreditTransfers.SingleOrDefault()?.AccountIdentifier,
            ["BT-85 PayeeAccountName"] = invoice.Payment.CreditTransfers.SingleOrDefault()?.AccountName,
            ["BT-106 LineTotal"] = Amount(invoice.Totals.LineExtensionAmount),
            ["BT-107 AllowanceTotal"] = Amount(invoice.Totals.AllowanceTotalAmount),
            ["BT-108 ChargeTotal"] = Amount(invoice.Totals.ChargeTotalAmount),
            ["BT-109 TaxExclusive"] = Amount(invoice.Totals.TaxExclusiveAmount),
            ["BT-110 TaxTotal"] = Amount(invoice.Totals.TaxTotalAmount),
            ["BT-112 TaxInclusive"] = Amount(invoice.Totals.TaxInclusiveAmount),
            ["BT-113 Prepaid"] = Amount(invoice.Totals.PrepaidAmount),
            ["BT-114 Rounding"] = Amount(invoice.Totals.RoundingAmount),
            ["BT-115 DuePayable"] = Amount(invoice.Totals.DuePayableAmount),
            ["Lines"] = invoice.Lines.Count.ToString(),
            ["TaxBreakdown"] = invoice.TaxBreakdown.Count.ToString(),
            ["AllowancesAndCharges"] = invoice.AllowancesAndCharges.Count.ToString(),
        };

        foreach (var (entry, index) in invoice.TaxBreakdown.Select((entry, index) => (entry, index)))
        {
            summary[$"BG-23[{index}] Category"] = entry.CategoryCode;
            summary[$"BG-23[{index}] Percent"] = Amount(entry.Percent);
            summary[$"BG-23[{index}] Taxable"] = Amount(entry.TaxableAmount);
            summary[$"BG-23[{index}] Tax"] = Amount(entry.TaxAmount);
        }

        foreach (var (charge, index) in invoice.AllowancesAndCharges.Select((charge, index) => (charge, index)))
        {
            summary[$"BG-20[{index}] IsCharge"] = charge.IsCharge.ToString();
            summary[$"BG-20[{index}] Amount"] = Amount(charge.Amount);
            summary[$"BG-20[{index}] Reason"] = charge.Reason;
            summary[$"BG-20[{index}] TaxCategory"] = charge.TaxCategoryCode;
            summary[$"BG-20[{index}] TaxPercent"] = Amount(charge.TaxPercent);
        }

        foreach (var (line, index) in invoice.Lines.Select((line, index) => (line, index)))
        {
            summary[$"BG-25[{index}] BT-126 Id"] = line.Id;
            summary[$"BG-25[{index}] BT-129 Quantity"] = Amount(line.Quantity);
            summary[$"BG-25[{index}] BT-130 Unit"] = line.QuantityUnitCode;
            summary[$"BG-25[{index}] BT-131 NetAmount"] = Amount(line.NetAmount);
            summary[$"BG-25[{index}] BT-146 NetPrice"] = Amount(line.Price.NetAmount);
            summary[$"BG-25[{index}] BT-151 TaxCategory"] = line.TaxCategoryCode;
            summary[$"BG-25[{index}] BT-152 TaxPercent"] = Amount(line.TaxPercent);
            summary[$"BG-25[{index}] BT-153 ItemName"] = line.Item.Name;
            summary[$"BG-25[{index}] BT-155 SellerItemId"] = line.Item.SellerIdentifier;
            summary[$"BG-25[{index}] BT-157 StandardId"] = line.Item.StandardIdentifier?.ToString();
        }

        return summary;
    }

    private static string? Amount(decimal? value) => value?.ToString("0.####");
}
