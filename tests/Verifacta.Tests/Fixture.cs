namespace Verifacta.Tests;

internal static class Fixture
{
    internal static InvoiceDocument Load(string fileName) =>
        InvoiceDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
