using Verifacta.Detection;
using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// "Valid" is only useful if it says valid against what. A document declaring a specification with
/// no rule set here was judged against EN 16931 and reported as valid, with nothing in the result
/// to say the rules it actually claimed to follow were never applied.
/// </summary>
[Collection("validator")]
public class ValidationContractTests(ValidatorFixture fixture)
{
    [Theory]
    [InlineData("urn:cen.eu:en16931:2017", true)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0", true)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0", true)]
    [InlineData("urn:factur-x.eu:1p0:basic", false)]
    [InlineData("urn:some.authority:cius:2029", false)]
    [InlineData(null, false)]
    public void Knows_which_declared_specifications_have_rules_of_their_own(string? identifier, bool covered)
    {
        Assert.Equal(covered, RulePackCatalog.Covers(InvoiceProfile.Parse(identifier)));
    }

    [Fact]
    public void Reports_that_an_unknown_profile_fell_back_to_en16931()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(WithProfile("urn:some.authority:cius:2029"));

        Assert.Equal(RuleSet.En16931, result.RuleSet);
        Assert.False(result.ProfileCovered);
        // The fallback verdict still stands on its own terms; it is the silence that was the problem.
        Assert.DoesNotContain(result.Findings, finding => finding.RuleId == InvoiceValidator.ProfileRuleId);
    }

    [Fact]
    public void Strict_mode_refuses_a_profile_it_has_no_rules_for()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(WithProfile("urn:some.authority:cius:2029"), strict: true);

        Assert.False(result.IsValid);
        Assert.False(result.ProfileCovered);
        var finding = Assert.Single(result.Findings, candidate => candidate.RuleId == InvoiceValidator.ProfileRuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("urn:some.authority:cius:2029", finding.Message);
    }

    [Fact]
    public void Strict_mode_leaves_a_covered_profile_alone()
    {
        if (!fixture.Available) return;

        var result = fixture.Validator!.Validate(Fixture.Load("xrechnung-ubl.xml"), strict: true);

        Assert.True(result.ProfileCovered);
        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Strict_mode_defers_to_a_rule_set_the_caller_named()
    {
        if (!fixture.Available) return;

        // Naming a rule set is a decision; strict mode has nothing left to warn about.
        var result = fixture.Validator!.Validate(
            WithProfile("urn:some.authority:cius:2029"), RuleSet.En16931, strict: true);

        Assert.DoesNotContain(result.Findings, finding => finding.RuleId == InvoiceValidator.ProfileRuleId);
    }

    [Fact]
    public void Separates_a_schema_that_passed_from_one_that_was_never_checked()
    {
        if (!fixture.Available) return;

        var checked_ = fixture.Validator!.Validate(Fixture.Load("xrechnung-ubl.xml"));
        var skipped = fixture.Validator.Validate(Fixture.Load("xrechnung-ubl.xml"), validateSchema: false);

        Assert.True(checked_.SchemaChecked);
        Assert.True(checked_.SchemaValid);

        Assert.False(skipped.SchemaChecked);
        Assert.True(skipped.SchemaValid);
    }

    private static InvoiceDocument WithProfile(string identifier) => InvoiceDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"))
            .Replace(
                "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0",
                identifier));
}
