using System.Text.RegularExpressions;
using System.Xml.Linq;
using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// Replays the CEN EN 16931 rule test suite. Each testSet file targets one rule and holds a series
/// of fragments declaring either &lt;success&gt; (the rule must not fire) or &lt;error&gt; (it must).
/// This is the oracle: it proves Verifacta agrees with CEN rule by rule, not just overall.
/// </summary>
[Collection("validator")]
public partial class RuleOracleTests(ValidatorFixture fixture)
{
    private const string Missing = "(corpus missing)";

    private static readonly XNamespace Vefa = "http://difi.no/xsd/vefa/validator/1.0";

    private static readonly string? Root = Locate();

    public static TheoryData<string> TestSets
    {
        get
        {
            var data = new TheoryData<string>();
            if (Root is null)
            {
                data.Add(Missing);
                return data;
            }

            foreach (var file in Directory.EnumerateFiles(Root, "*.xml", SearchOption.AllDirectories).Order())
            {
                data.Add(Path.GetRelativePath(Root, file));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TestSets))]
    public void Agrees_with_the_cen_rule_tests(string relativePath)
    {
        if (relativePath == Missing || Root is null || !fixture.Available) return;

        var testSet = XDocument.Load(Path.Combine(Root, relativePath));
        if (testSet.Root?.Name != Vefa + "testSet") return;

        var mismatches = new List<string>();
        var index = 0;

        foreach (var test in testSet.Root.Elements(Vefa + "test"))
        {
            index++;
            var document = test.Elements().FirstOrDefault(child => child.Name.Namespace != Vefa);
            if (document is null) continue;

            var expectations = test.Element(Vefa + "assert");
            var mustNotFire = expectations.Els(Vefa + "success");
            var mustFire = expectations.Els(Vefa + "error");
            if (mustNotFire.Count == 0 && mustFire.Count == 0) continue;

            var fired = Fire(document);

            foreach (var rule in mustFire.Where(rule => !fired.Contains(rule) && Implemented(rule)))
            {
                mismatches.Add($"test #{index}: expected {rule} to fire, it did not");
            }

            foreach (var rule in mustNotFire.Where(fired.Contains))
            {
                mismatches.Add($"test #{index}: expected {rule} not to fire, it did");
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// The CEN suite ships tests for rules its own Schematron does not implement. A rule that is
    /// absent from the artefact cannot fire, so those expectations are not disagreements.
    /// <see cref="Lists_rules_tested_but_not_implemented"/> pins the exact set.
    /// </summary>
    [Fact]
    public void Lists_rules_tested_but_not_implemented()
    {
        if (Root is null || !fixture.Available) return;

        var tested = Directory.EnumerateFiles(Root, "*.xml", SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .Where(document => document.Root?.Name == Vefa + "testSet")
            .SelectMany(document => document.Descendants(Vefa + "error").Concat(document.Descendants(Vefa + "success")))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct();

        Assert.Equal(["BR-CO-25"], tested.Where(rule => !Implemented(rule)).Order());
    }

    private bool Implemented(string ruleId) => ImplementedRules.Value.Contains(ruleId);

    private static readonly Lazy<HashSet<string>> ImplementedRules = new(() =>
    {
        var pack = RulePackCatalog.Load().Pack("cen-en16931");
        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in pack.Files.Keys)
        {
            foreach (var match in RuleIdPattern().Matches(File.ReadAllText(Path.Combine(pack.Directory, name))))
            {
                rules.Add(((Match)match).Groups[1].Value);
            }
        }

        return rules;
    });

    private HashSet<string> Fire(XElement document)
    {
        // DisableFormatting matters: indenting a fragment injects whitespace text nodes and changes
        // the string value of elements, which silently flips rules that measure content length.
        var invoice = InvoiceDocument.Parse(new XDocument(document).ToString(SaveOptions.DisableFormatting));
        // The CEN fragments are deliberately partial documents, so only the business rules apply.
        var result = fixture.Validator!.Validate(invoice, RuleSet.En16931, validateSchema: false);

        return result.Findings.Select(finding => finding.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"""id"">([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)<", RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdPattern();

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "corpus", "cen-rule-tests");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}

internal static class TestSetExtensions
{
    internal static List<string> Els(this XElement? parent, XName name) =>
        parent?.Elements(name).Select(element => element.Value.Trim()).Where(value => value.Length > 0).ToList() ?? [];
}
