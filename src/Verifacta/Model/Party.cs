namespace Verifacta.Model;

public sealed class Identifier
{
    public Identifier(string value, string? scheme = null)
    {
        Value = value;
        Scheme = scheme;
    }

    public string Value { get; }

    public string? Scheme { get; }

    public override string ToString() => Scheme is null ? Value : Scheme + ":" + Value;
}

public sealed class Party
{
    public string? Name { get; set; }

    public string? TradingName { get; set; }

    public List<Identifier> Identifiers { get; } = [];

    public Identifier? LegalRegistrationId { get; set; }

    public string? VatIdentifier { get; set; }

    public string? TaxRegistrationId { get; set; }

    public string? AdditionalLegalInformation { get; set; }

    public Identifier? ElectronicAddress { get; set; }

    public PostalAddress? Address { get; set; }

    public Contact? Contact { get; set; }
}

public sealed class PostalAddress
{
    public string? Line1 { get; set; }

    public string? Line2 { get; set; }

    public string? Line3 { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    public string? CountrySubdivision { get; set; }

    public string? CountryCode { get; set; }
}

public sealed class Contact
{
    public string? Name { get; set; }

    public string? Department { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }
}
