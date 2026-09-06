using Klarfakt.Detection;
using Klarfakt.Validation;

namespace Klarfakt.Tests;

/// <summary>
/// KLARFAKT_RULES is the documented way to run against artefacts you manage yourself, and paths
/// reach it from shells, container mounts and config files — so the separator style is not ours to
/// choose. A forward-slash path on Windows used to load fine and then throw on the first document.
/// </summary>
[Collection("validator")]
public class RulesDirectoryTests(ValidatorFixture fixture)
{
    [Fact]
    public void Accepts_a_rules_directory_written_with_forward_slashes()
    {
        if (!fixture.Available) return;

        var catalog = RulePackCatalog.Load(fixture.Catalog!.Root.Replace('\\', '/'));

        Assert.True(catalog.IsRestored);
        Assert.Equal(fixture.Catalog.Root, catalog.Root);

        var result = new InvoiceValidator(catalog).Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.True(result.IsValid);
        Assert.Contains("xrechnung 3.0.2", Assert.Single(result.RulePacks));
    }

    [Fact]
    public void Accepts_a_relative_rules_directory()
    {
        if (!fixture.Available) return;

        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fixture.Catalog!.Root);
        var catalog = RulePackCatalog.Load(relative);

        Assert.Equal(fixture.Catalog.Root, catalog.Root);
        Assert.True(catalog.IsRestored);
    }

    [Fact]
    public void Names_the_pack_behind_every_rule_set_it_supports()
    {
        if (!fixture.Available) return;

        var catalog = RulePackCatalog.Load(fixture.Catalog!.Root.Replace('\\', '/'));

        foreach (var (ruleSet, syntax, expected) in new[]
        {
            (RuleSet.En16931, InvoiceSyntax.Ubl, "cen-en16931"),
            (RuleSet.En16931, InvoiceSyntax.Cii, "cen-en16931"),
            (RuleSet.PeppolBisBilling3, InvoiceSyntax.Ubl, "peppol-bis"),
            (RuleSet.XRechnung, InvoiceSyntax.Ubl, "xrechnung"),
            (RuleSet.XRechnung, InvoiceSyntax.Cii, "xrechnung"),
        })
        {
            Assert.StartsWith(expected, catalog.Describe(ruleSet, syntax), StringComparison.Ordinal);
        }
    }
}
