using Klarfakt.Detection;
using Klarfakt.Rendering;
using Klarfakt.Validation;

namespace Klarfakt.Tests;

/// <summary>
/// Warmup compiles artefacts up front, so it is the one place that touches files no other code
/// path reaches. A pack holding imported modules or assets — the visualisation pack does both —
/// broke folder validation with an unhandled compiler error while every other test stayed green.
/// </summary>
[Collection("validator")]
public class WarmupTests(ValidatorFixture fixture)
{
    [Fact]
    public void Validator_warms_up_without_touching_the_visualisation_pack()
    {
        if (!fixture.Available) return;

        new InvoiceValidator(fixture.Catalog).Warmup();
    }

    [Fact]
    public void Renderer_warms_up()
    {
        if (!fixture.Available) return;

        new InvoiceRenderer(fixture.Catalog).Warmup();
    }

    [Fact]
    public void Warmup_covers_every_artefact_validation_can_reach()
    {
        if (!fixture.Available) return;

        var warmed = fixture.Catalog!.ValidationLayers();

        foreach (var ruleSet in Enum.GetValues<RuleSet>())
        {
            foreach (var syntax in new[] { InvoiceSyntax.Ubl, InvoiceSyntax.Cii })
            {
                IReadOnlyList<string> layers;
                try
                {
                    layers = fixture.Catalog.Layers(ruleSet, syntax);
                }
                catch (RulePackException)
                {
                    continue;
                }

                Assert.All(layers, layer => Assert.Contains(layer, warmed));
            }
        }
    }

    [Fact]
    public void Warmup_excludes_the_visualisation_stylesheets()
    {
        if (!fixture.Available) return;

        var visualisation = fixture.Catalog!.Pack("visualization");

        Assert.DoesNotContain(
            fixture.Catalog.ValidationLayers(),
            layer => layer.StartsWith(visualisation.Directory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validating_a_folder_after_warmup_still_works()
    {
        if (!fixture.Available) return;

        var validator = new InvoiceValidator(fixture.Catalog);
        validator.Warmup();

        var result = validator.Validate(Fixture.Load("xrechnung-ubl.xml"));

        Assert.True(result.IsValid);
    }
}
