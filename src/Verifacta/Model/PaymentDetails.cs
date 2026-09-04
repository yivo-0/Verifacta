namespace Verifacta.Model;

public sealed class PaymentDetails
{
    public string? MeansCode { get; set; }

    public string? MeansText { get; set; }

    public string? RemittanceInformation { get; set; }

    public string? Terms { get; set; }

    public List<CreditTransfer> CreditTransfers { get; } = [];

    public PaymentCard? Card { get; set; }

    public DirectDebit? DirectDebit { get; set; }
}

public sealed class CreditTransfer
{
    public string? AccountIdentifier { get; set; }

    public string? AccountName { get; set; }

    public string? ServiceProviderIdentifier { get; set; }
}

public sealed class PaymentCard
{
    public string? PrimaryAccountNumber { get; set; }

    public string? HolderName { get; set; }
}

public sealed class DirectDebit
{
    public string? MandateReference { get; set; }

    public string? CreditorIdentifier { get; set; }

    public string? DebitedAccountIdentifier { get; set; }
}
