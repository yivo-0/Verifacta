using Verifacta.Validation;

namespace Verifacta.Tests;

public sealed class ValidatorFixture
{
    public ValidatorFixture()
    {
        Available = TryLoad(out var catalog);
        Catalog = catalog;
        Validator = Available ? new InvoiceValidator(catalog) : null;
    }

    public bool Available { get; }

    public RulePackCatalog? Catalog { get; }

    public InvoiceValidator? Validator { get; }

    private static bool TryLoad(out RulePackCatalog? catalog)
    {
        try
        {
            catalog = RulePackCatalog.Load();
            return true;
        }
        catch (RulePackException)
        {
            catalog = null;
            return false;
        }
    }
}

[CollectionDefinition("validator")]
public sealed class ValidatorCollection : ICollectionFixture<ValidatorFixture>;
