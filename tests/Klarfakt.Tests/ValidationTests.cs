using Klarfakt.Detection;
using Klarfakt.Validation;

namespace Klarfakt.Tests;

[Collection("validator")]
public class ValidationTests(ValidatorFixture fixture)
{
    [Fact]
    public void Rule_packs_match_their_manifest_hashes()
    {
        if (!fixture.Available) return;

        fixture.Catalog!.VerifyIntegrity();

        Assert.Equal(
            ["cen-en16931", "peppol-bis", "schemas", "visualization", "xrechnung"],
            fixture.Catalog.Packs.Select(pack => pack.Id).Order());
    }

    [Theory]
    [InlineData(ProfileKind.XRechnung, RuleSet.XRechnung)]
    [InlineData(ProfileKind.PeppolBisBilling3, RuleSet.PeppolBisBilling3)]
    [InlineData(ProfileKind.En16931, RuleSet.En16931)]
    [InlineData(ProfileKind.FacturX, RuleSet.En16931)]
    [InlineData(ProfileKind.Unknown, RuleSet.En16931)]
    public void Selects_the_rule_set_from_the_profile(ProfileKind kind, RuleSet expected)
    {
        var identifier = kind switch
        {
            ProfileKind.XRechnung => "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0",
            ProfileKind.PeppolBisBilling3 => "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
            ProfileKind.En16931 => "urn:cen.eu:en16931:2017",
            ProfileKind.FacturX => "urn:factur-x.eu:1p0:basic",
            _ => "nonsense",
        };

        Assert.Equal(expected, RulePackCatalog.RuleSetFor(InvoiceProfile.Parse(identifier)));
    }

    [Fact]
    public void Catches_a_broken_invoice_total()
    {
        if (!fixture.Available) return;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace(
                "<cbc:TaxInclusiveAmount currencyID=\"EUR\">2023.00</cbc:TaxInclusiveAmount>",
                "<cbc:TaxInclusiveAmount currencyID=\"EUR\">2033.00</cbc:TaxInclusiveAmount>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings, candidate => candidate.RuleId == "BR-CO-15");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("BT-112", finding.BusinessTerms);
        Assert.StartsWith("/ubl-invoice:Invoice", finding.Location);
        Assert.DoesNotContain("namespace-uri()", finding.Location);
        Assert.Equal("fatal", finding.Flag);
        Assert.Equal("EN16931-UBL-validation.xsl", finding.Artefact);
    }

    [Fact]
    public void Accepts_a_conformant_xrechnung_invoice()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.True(result.SchemaValid);
        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Attributes_a_german_rule_to_the_xrechnung_artefact()
    {
        if (!fixture.Available) return;

        // BT-10 is optional in EN 16931 but mandatory in the German CIUS, so dropping it proves
        // which layer a finding came from.
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace("<cbc:BuyerReference>04011000-12345-34</cbc:BuyerReference>", string.Empty);

        var findings = fixture.Validator!.Validate(InvoiceDocument.Parse(xml)).Findings;

        var finding = Assert.Single(findings, candidate => candidate.RuleId == "BR-DE-15");
        Assert.Equal("XRechnung-UBL-validation.xsl", finding.Artefact);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Accepts_schema_valid_fixtures()
    {
        if (!fixture.Available) return;

        foreach (var name in new[] { "xrechnung-ubl.xml", "facturx-cii.xml" })
        {
            var result = fixture.Validator!.Validate(Fixture.Load(name));
            Assert.True(result.SchemaValid, $"{name} should conform to its XML Schema");
            Assert.DoesNotContain(result.Findings, finding => finding.RuleId == "XSD");
        }
    }

    [Fact]
    public void Stops_at_the_schema_when_the_structure_is_wrong()
    {
        if (!fixture.Available) return;

        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace(
                "<cbc:ID>RE-2026-0042</cbc:ID>",
                "<cbc:ID>RE-2026-0042</cbc:ID><cbc:Bogus>nope</cbc:Bogus>");

        var result = fixture.Validator!.Validate(InvoiceDocument.Parse(xml));

        Assert.False(result.SchemaValid);
        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("XSD", finding.RuleId);
        Assert.Equal("UBL-Invoice-2.1.xsd", finding.Artefact);
        Assert.Contains("Bogus", finding.Message);
        Assert.Equal("/ubl-invoice:Invoice/cbc:Bogus", finding.Location);
    }

    [Fact]
    public void Checks_business_rules_without_the_schema_when_asked()
    {
        if (!fixture.Available) return;

        var fragment = InvoiceDocument.Parse(
            """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>FRAGMENT</cbc:ID>
            </Invoice>
            """);

        var result = fixture.Validator!.Validate(fragment, RuleSet.En16931, validateSchema: false);

        Assert.True(result.SchemaValid);
        Assert.DoesNotContain(result.Findings, finding => finding.RuleId == "XSD");
        Assert.Contains(result.Findings, finding => finding.RuleId == "BR-01");
    }

    [Fact]
    public void Normalises_svrl_locations()
    {
        const string raw =
            "/*:Invoice[namespace-uri()='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'][1]" +
            "/*:LegalMonetaryTotal[namespace-uri()='urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2'][1]" +
            "/*:TaxInclusiveAmount[namespace-uri()='urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2'][2]";

        Assert.Equal(
            "/ubl-invoice:Invoice/cac:LegalMonetaryTotal/cbc:TaxInclusiveAmount[2]",
            LocationPath.Normalise(raw));
    }

    [Fact]
    public void Normalises_saxon_braced_locations()
    {
        const string raw =
            "/Q{urn:oasis:names:specification:ubl:schema:xsd:Invoice-2}Invoice[1]" +
            "/Q{urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2}LegalMonetaryTotal[1]" +
            "/Q{urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2}TaxInclusiveAmount[1]";

        Assert.Equal(
            "/ubl-invoice:Invoice/cac:LegalMonetaryTotal/cbc:TaxInclusiveAmount",
            LocationPath.Normalise(raw));
    }

    [Fact]
    public void Reports_the_rule_pack_that_produced_the_verdict()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.Equal(RuleSet.XRechnung, result.RuleSet);
        Assert.Contains("xrechnung 3.0.2", Assert.Single(result.RulePacks));
    }

    [Fact]
    public void Rejects_peppol_rules_for_cii()
    {
        if (!fixture.Available) return;

        var exception = Assert.Throws<RulePackException>(
            () => fixture.Validator!.Validate(Fixture.Load("facturx-cii.xml"), RuleSet.PeppolBisBilling3));

        Assert.Contains("UBL only", exception.Message);
    }
}
