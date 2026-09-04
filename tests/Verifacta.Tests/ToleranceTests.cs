using Verifacta.Reading;

namespace Verifacta.Tests;

public class ToleranceTests
{
    [Fact]
    public void Reports_broken_values_without_throwing()
    {
        var result = Fixture.Load("broken-values-ubl.xml").Read();

        Assert.Equal("BROKEN-1", result.Invoice.Id);
        Assert.Null(result.Invoice.IssueDate);
        Assert.Null(result.Invoice.Totals.LineExtensionAmount);
        Assert.Equal(1190.00m, result.Invoice.Totals.DuePayableAmount);
        Assert.Null(result.Invoice.Lines[0].Quantity);
        Assert.Equal("Kaputte Zeile", result.Invoice.Lines[0].Item.Name);
    }

    [Fact]
    public void Records_a_finding_per_broken_value()
    {
        var result = Fixture.Load("broken-values-ubl.xml").Read();

        Assert.True(result.HasFindings);
        Assert.Equal(3, result.Findings.Count);

        var date = Assert.Single(result.Findings, finding => finding.Kind == ReadFindingKind.UnparsableDate);
        Assert.Equal("/ubl-invoice:Invoice/cbc:IssueDate", date.Path);
        Assert.Contains("2026-13-45", date.Message);

        Assert.Equal(2, result.Findings.Count(finding => finding.Kind == ReadFindingKind.UnparsableNumber));
        Assert.Contains(
            result.Findings,
            finding => finding.Path == "/ubl-invoice:Invoice/cac:LegalMonetaryTotal/cbc:LineExtensionAmount");
        Assert.Contains(
            result.Findings,
            finding => finding.Path == "/ubl-invoice:Invoice/cac:InvoiceLine/cbc:InvoicedQuantity");
    }
}
