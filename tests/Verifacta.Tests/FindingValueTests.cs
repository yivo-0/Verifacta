using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// SVRL says which rule failed and where, never what the value was. An application turning a
/// finding into something a user can act on needs the value, and resolving the location by hand is
/// the work every consumer would otherwise repeat.
/// </summary>
[Collection("validator")]
public class FindingValueTests(ValidatorFixture fixture)
{
    [Fact]
    public void Says_nothing_when_the_rule_looked_at_more_than_one_value()
    {
        if (!fixture.Available) return;

        // BR-CO-15 compares three totals, so its Schematron context is the invoice itself. The
        // string value of an element with children is every scrap of text beneath it, which is
        // worse than saying nothing — so nothing is what it says.
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace(
                "<cbc:TaxInclusiveAmount currencyID=\"EUR\">2023.00</cbc:TaxInclusiveAmount>",
                "<cbc:TaxInclusiveAmount currencyID=\"EUR\">2033.00</cbc:TaxInclusiveAmount>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));
        var finding = Assert.Single(result.Findings, candidate => candidate.RuleId == "BR-CO-15");

        Assert.Equal("/ubl-invoice:Invoice", finding.Location);
        Assert.Null(finding.Value);
    }

    [Fact]
    public void Carries_the_value_when_the_rule_pointed_at_one()
    {
        if (!fixture.Available) return;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace("<cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>",
                     "<cbc:DocumentCurrencyCode>XYZ</cbc:DocumentCurrencyCode>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));
        var finding = Assert.Single(result.Findings, candidate => candidate.RuleId == "BR-CL-04");

        Assert.Equal("/ubl-invoice:Invoice/cbc:DocumentCurrencyCode", finding.Location);
        Assert.Equal("XYZ", finding.Value);
    }

    [Fact]
    public void Reports_the_value_a_code_list_rejected()
    {
        if (!fixture.Available) return;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace("<cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>",
                     "<cbc:DocumentCurrencyCode>XYZ</cbc:DocumentCurrencyCode>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));

        Assert.Contains(result.Findings, finding => finding.Value == "XYZ");
    }

    [Fact]
    public void A_schema_finding_carries_the_offending_text()
    {
        if (!fixture.Available) return;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace("<cbc:IssueDate>2026-03-15</cbc:IssueDate>", "<cbc:IssueDate>not-a-date</cbc:IssueDate>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));
        var finding = Assert.Single(result.Findings);

        Assert.Equal("XSD", finding.RuleId);
        Assert.Equal("not-a-date", finding.Value);
    }

    [Fact]
    public void Leaves_the_value_null_when_there_is_nothing_useful_to_show()
    {
        if (!fixture.Available) return;

        // BR-01 fires because the specification identifier is absent; there is no value to report.
        var fragment = InvoiceDocument.Parse(
            """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>FRAGMENT</cbc:ID>
            </Invoice>
            """);

        var result = fixture.Validator!.Validate(fragment, RuleSet.En16931, validateSchema: false);
        var finding = Assert.Single(result.Findings, candidate => candidate.RuleId == "BR-01");

        Assert.Null(finding.Value);
    }

    [Fact]
    public void Does_not_change_any_other_part_of_a_finding()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
    }
}
