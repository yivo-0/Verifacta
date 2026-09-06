using System.Text;
using System.Xml;
using Klarfakt.Detection;
using Klarfakt.Reading;

namespace Klarfakt.Tests;

[Collection("validator")]
public class CorpusTests
{
    private const string Missing = "(corpus missing)";

    private static readonly string[] CuratedSources = ["kosit-xrechnung", "peppol-bis", "facturx", "cen-en16931"];

    private static readonly string? Root = Locate();

    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();
            if (Root is null)
            {
                data.Add(Missing);
                return data;
            }

            foreach (var file in Corpus.Files(Root, ".xml"))
            {
                data.Add(Path.GetRelativePath(Root, file));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Reads_without_unexpected_exceptions(string relativePath)
    {
        if (relativePath == Missing || Root is null) return;

        var path = Path.Combine(Root, relativePath);

        InvoiceDocument document;
        try
        {
            document = InvoiceDocument.Load(path);
        }
        catch (UnsupportedDocumentException)
        {
            return;
        }
        catch (XmlException)
        {
            return;
        }

        var result = document.Read();
        Assert.NotNull(result.Invoice);
    }

    [Fact]
    public void Curated_examples_are_fully_parsable()
    {
        if (Root is null) return;

        var offenders = new List<string>();

        foreach (var (relativePath, result) in EnumerateReadable())
        {
            if (!CuratedSources.Contains(SourceOf(relativePath))) continue;
            if (!result.HasFindings) continue;

            offenders.Add($"{relativePath}: " + string.Join("; ", result.Findings.Select(f => f.ToString())));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Writes_corpus_report()
    {
        if (Root is null) return;

        var report = new StringBuilder();
        report.AppendLine("# Corpus report");
        report.AppendLine();

        var bySource = new SortedDictionary<string, Counters>();
        var findingsByKind = new SortedDictionary<ReadFindingKind, int>();
        var profiles = new SortedDictionary<string, int>();
        var unreadable = new List<string>();
        var withFindings = new List<string>();

        foreach (var file in Corpus.Files(Root, ".xml"))
        {
            var relativePath = Path.GetRelativePath(Root, file);
            var counters = bySource.TryGetValue(SourceOf(relativePath), out var existing)
                ? existing
                : bySource[SourceOf(relativePath)] = new Counters();
            counters.Total++;

            InvoiceDocument document;
            try
            {
                document = InvoiceDocument.Load(file);
            }
            catch (UnsupportedDocumentException)
            {
                counters.Unsupported++;
                unreadable.Add($"{relativePath} (unsupported root)");
                continue;
            }
            catch (XmlException exception)
            {
                counters.Malformed++;
                unreadable.Add($"{relativePath} (malformed: {exception.Message})");
                continue;
            }

            var key = $"{document.Syntax}/{document.Profile.Kind}" +
                      (document.Profile.Level is { } level ? $" ({level})" : string.Empty);
            profiles[key] = profiles.TryGetValue(key, out var count) ? count + 1 : 1;

            var result = document.Read();
            if (result.HasFindings)
            {
                counters.WithFindings++;
                withFindings.Add($"{relativePath}: " +
                                 string.Join("; ", result.Findings.Select(f => f.ToString())));
                foreach (var finding in result.Findings)
                {
                    findingsByKind[finding.Kind] =
                        findingsByKind.TryGetValue(finding.Kind, out var kindCount) ? kindCount + 1 : 1;
                }
            }
            else
            {
                counters.Clean++;
            }
        }

        report.AppendLine("| Source | Files | Read clean | With findings | Unsupported root | Malformed XML |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var (source, counters) in bySource)
        {
            report.AppendLine($"| {source} | {counters.Total} | {counters.Clean} | {counters.WithFindings} " +
                              $"| {counters.Unsupported} | {counters.Malformed} |");
        }

        report.AppendLine();
        report.AppendLine("## Detected profiles");
        report.AppendLine();
        foreach (var (profile, count) in profiles)
        {
            report.AppendLine($"- {profile}: {count}");
        }

        if (findingsByKind.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Read findings by kind");
            report.AppendLine();
            foreach (var (kind, count) in findingsByKind)
            {
                report.AppendLine($"- {kind}: {count}");
            }
        }

        AppendList(report, "## Files with read findings", withFindings);
        AppendList(report, "## Files not readable", unreadable);

        Corpus.WriteReport("report", report.ToString());
    }

    private static void AppendList(StringBuilder report, string title, List<string> items)
    {
        if (items.Count == 0) return;

        report.AppendLine();
        report.AppendLine(title);
        report.AppendLine();
        foreach (var item in items)
        {
            report.AppendLine($"- {item}");
        }
    }

    private static IEnumerable<(string RelativePath, ReadResult Result)> EnumerateReadable()
    {
        foreach (var file in Corpus.Files(Root!, ".xml"))
        {
            ReadResult result;
            try
            {
                result = InvoiceDocument.Load(file).Read();
            }
            catch (UnsupportedDocumentException)
            {
                continue;
            }
            catch (XmlException)
            {
                continue;
            }

            yield return (Path.GetRelativePath(Root!, file), result);
        }
    }

    private static string SourceOf(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

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

    private sealed class Counters
    {
        internal int Total { get; set; }

        internal int Clean { get; set; }

        internal int WithFindings { get; set; }

        internal int Unsupported { get; set; }

        internal int Malformed { get; set; }
    }
}
