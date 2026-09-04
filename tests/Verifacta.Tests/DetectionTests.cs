using Verifacta.Detection;

namespace Verifacta.Tests;

public class DetectionTests
{
    [Fact]
    public void Detects_ubl_xrechnung()
    {
        var document = Fixture.Load("xrechnung-ubl.xml");

        Assert.Equal(InvoiceSyntax.Ubl, document.Syntax);
        Assert.Equal(DocumentKind.Invoice, document.Kind);
        Assert.Equal(ProfileKind.XRechnung, document.Profile.Kind);
        Assert.Equal("3.0", document.Profile.Version);
        Assert.False(document.Profile.IsExtension);
        Assert.Equal("urn:fdc:peppol.eu:2017:poacc:billing:01:1.0", document.Profile.BusinessProcess);
    }

    [Fact]
    public void Detects_cii_facturx()
    {
        var document = Fixture.Load("facturx-cii.xml");

        Assert.Equal(InvoiceSyntax.Cii, document.Syntax);
        Assert.Equal(DocumentKind.Invoice, document.Kind);
        Assert.Equal(ProfileKind.FacturX, document.Profile.Kind);
        Assert.Equal("1.0", document.Profile.Version);
        Assert.Equal("basic", document.Profile.Level);
    }

    [Theory]
    [InlineData("urn:cen.eu:en16931:2017", ProfileKind.En16931, "2017", null)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0", ProfileKind.PeppolBisBilling3, "3.0", null)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0", ProfileKind.XRechnung, "3.0", null)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:xoev-de:kosit:standard:xrechnung_2.3", ProfileKind.XRechnung, "2.3", null)]
    [InlineData("urn:cen.eu:en16931:2017#conformant#urn:xoev-de:kosit:extension:xrechnung_3.0", ProfileKind.XRechnung, "3.0", null)]
    [InlineData("urn:factur-x.eu:1p0:minimum", ProfileKind.FacturX, "1.0", "minimum")]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic", ProfileKind.FacturX, "1.0", "basic")]
    [InlineData("urn:cen.eu:en16931:2017#conformant#urn:zugferd.de:2p0:extended", ProfileKind.FacturX, "2.0", "extended")]
    [InlineData("something-else", ProfileKind.Unknown, null, null)]
    [InlineData("123", ProfileKind.Unknown, null, null)]
    public void Classifies_specification_identifier(string identifier, ProfileKind expected, string? version, string? level)
    {
        var profile = InvoiceProfile.Parse(identifier);

        Assert.Equal(expected, profile.Kind);
        Assert.Equal(version, profile.Version);
        Assert.Equal(level, profile.Level);
    }

    [Fact]
    public void Flags_the_xrechnung_extension()
    {
        var profile = InvoiceProfile.Parse(
            "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0" +
            "#conformant#urn:xeinkauf.de:kosit:extension:xrechnung_3.0");

        Assert.Equal(ProfileKind.XRechnung, profile.Kind);
        Assert.Equal("3.0", profile.Version);
        Assert.True(profile.IsExtension);
    }

    [Fact]
    public void Rejects_non_invoice_documents()
    {
        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Parse("<Order xmlns=\"urn:example\" />"));

        Assert.Contains("not a UBL Invoice", exception.Message);
    }

    [Fact]
    public void Rejects_documents_with_a_doctype()
    {
        Assert.ThrowsAny<Exception>(
            () => InvoiceDocument.Parse("<!DOCTYPE Invoice [<!ENTITY x \"y\">]><Invoice />"));
    }
}
