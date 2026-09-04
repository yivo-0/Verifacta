namespace Verifacta.Tests;

internal static class Fixture
{
    internal static InvoiceDocument Load(string fileName) =>
        InvoiceDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}

internal static class Corpus
{
    /// <summary>
    /// Enumerates corpus files by extension, case-insensitively. A "*.xml" search pattern matches
    /// case-insensitively on Windows but not on Linux, which silently hid three .XML invoices from
    /// CI while they were validated locally.
    /// </summary>
    internal static IEnumerable<string> Files(string root, string extension) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
}
