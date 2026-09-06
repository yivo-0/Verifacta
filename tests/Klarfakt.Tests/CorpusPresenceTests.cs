using System.Text;

namespace Klarfakt.Tests;

/// <summary>
/// Every corpus test returns early when its files are absent, so "all tests passed" on a clean
/// clone says nothing about the corpora. These turn that silence into a failure wherever
/// KLARFAKT_REQUIRE_CORPUS is set, and write down what actually ran either way.
/// </summary>
[Collection("validator")]
public class CorpusPresenceTests(ValidatorFixture fixture)
{
    /// <summary>
    /// Minimum file counts, from the pinned refs in tools/fetch-corpus.py. Minimums rather than
    /// exact counts: an upstream bump that adds cases should not fail the build, one that silently
    /// drops them should.
    /// </summary>
    private static readonly (string Name, int Minimum)[] Corpora =
    [
        ("cen-en16931", 34),
        ("cen-rule-tests", 309),
        ("facturx", 10),
        ("kosit-xrechnung", 72),
        ("mustang", 61),
        ("pdf-edge-cases", 4),
        ("pdf-facturx", 1),
        ("pdf-mustang", 18),
        ("peppol-bis", 12),
    ];

    [Fact]
    public void Every_corpus_is_present_and_has_not_shrunk()
    {
        if (!Corpus.Required && Corpus.Root is null) return;

        Assert.NotNull(Corpus.Root);

        var missing = new List<string>();

        foreach (var (name, minimum) in Corpora)
        {
            var directory = Path.Combine(Corpus.Root!, name);
            if (!Directory.Exists(directory))
            {
                missing.Add($"{name}: absent");
                continue;
            }

            var count = Count(directory);
            if (count < minimum) missing.Add($"{name}: {count} files, expected at least {minimum}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void The_rule_packs_are_restored()
    {
        if (!Corpus.Required && !fixture.Available) return;

        Assert.True(fixture.Available, "the rule packs are not loadable; run 'klarfakt rules restore'");
        Assert.True(fixture.Catalog!.IsRestored, $"the rule packs are not restored in {fixture.Catalog.Root}");
        fixture.Catalog.VerifyIntegrity();
    }

    /// <summary>
    /// Writes what the run actually had available, next to the other corpus reports, so a green
    /// build says which corpora it was green against.
    /// </summary>
    [Fact]
    public void Records_what_this_run_had_available()
    {
        if (Corpus.Root is null) return;

        var report = new StringBuilder()
            .AppendLine("# Corpus coverage")
            .AppendLine()
            .AppendLine($"Rule packs: {(fixture.Available && fixture.Catalog!.IsRestored ? "restored" : "MISSING")}")
            .AppendLine($"Required: {Corpus.Required}")
            .AppendLine()
            .AppendLine("| Corpus | Files | Minimum | |")
            .AppendLine("|---|---|---|---|");

        foreach (var (name, minimum) in Corpora)
        {
            var directory = Path.Combine(Corpus.Root, name);
            var present = Directory.Exists(directory);
            var count = present ? Count(directory) : 0;

            report.AppendLine($"| `{name}` | {count} | {minimum} | {(count >= minimum ? "ok" : "MISSING")} |");
        }

        Corpus.WriteReport("coverage", report.ToString());
    }

    private static int Count(string directory) =>
        Corpus.Files(directory, ".xml").Count() + Corpus.Files(directory, ".pdf").Count();
}
