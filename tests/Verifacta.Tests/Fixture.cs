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

    /// <summary>
    /// Most of the suite turns into a no-op without the corpora or the rule packs, so a green run on
    /// a clean clone proves very little. CI sets VERIFACTA_REQUIRE_CORPUS, which turns anything
    /// missing into a failure instead of silence. Left unset locally, so a fresh clone still runs.
    /// </summary>
    internal static bool Required { get; } =
        Environment.GetEnvironmentVariable("VERIFACTA_REQUIRE_CORPUS") is { Length: > 0 };

    /// <summary>The corpus directory, found from the assembly upwards, or null if it is absent.</summary>
    internal static string? Root { get; } = Locate();

    /// <summary>
    /// Writes a corpus report, named for the framework that produced it. The suite runs on net8.0
    /// and net10.0 at the same time, and two runs calling File.WriteAllText on one path collide
    /// often enough to fail a build for no reason — which is exactly the kind of failure that
    /// teaches people to re-run the build instead of reading it.
    /// </summary>
    internal static void WriteReport(string name, string content)
    {
        if (Root is null) return;

        var moniker = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        File.WriteAllText(Path.Combine(Root, $"{name}-{moniker}.md"), content);
    }

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "corpus");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
