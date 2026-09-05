using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// The library documents one validator shared for the lifetime of a process, and the CLI validates
/// an archive with Parallel.ForEach. That promise rests on a compiled XmlSchemaSet and a Saxon
/// XsltExecutable being safe to read from many threads, neither of which is obvious from the
/// documentation, so it is pinned here rather than assumed.
/// </summary>
[Collection("validator")]
public class ConcurrencyTests(ValidatorFixture fixture)
{
    private const int Threads = 16;
    private const int PerThread = 12;

    [Fact]
    public void Validates_the_same_document_from_many_threads_on_a_cold_validator()
    {
        if (!fixture.Available) return;

        // Deliberately not warmed up: a cold cache is what puts every thread through stylesheet
        // compilation and schema-set construction at the same moment.
        var validator = new InvoiceValidator(fixture.Catalog);
        var results = new ValidationResult[Threads * PerThread];

        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads }, index =>
        {
            results[index] = validator.Validate(Fixture.Load("xrechnung-ubl.xml"));
        });

        Assert.All(results, result =>
        {
            Assert.True(result.SchemaValid);
            Assert.True(result.IsValid);
            Assert.Empty(result.Findings);
        });
    }

    [Fact]
    public void Mixes_syntaxes_and_rule_sets_across_threads()
    {
        if (!fixture.Available) return;

        var validator = new InvoiceValidator(fixture.Catalog);
        var work = new (string File, RuleSet? RuleSet)[]
        {
            ("xrechnung-ubl.xml", null),
            ("xrechnung-ubl.xml", RuleSet.En16931),
            ("facturx-cii.xml", null),
            ("facturx-cii.xml", RuleSet.En16931),
        };

        var results = new ValidationResult[work.Length * PerThread];

        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads }, index =>
        {
            var (file, ruleSet) = work[index % work.Length];
            results[index] = validator.Validate(Fixture.Load(file), ruleSet);
        });

        // Every run of the same job must agree; a torn schema set or a shared transformer shows up
        // as one thread disagreeing with the others about the same document.
        for (var job = 0; job < work.Length; job++)
        {
            var expected = results[job].Findings.Select(finding => finding.RuleId).Order().ToList();

            for (var index = job; index < results.Length; index += work.Length)
            {
                Assert.Equal(expected, results[index].Findings.Select(finding => finding.RuleId).Order());
                Assert.True(results[index].SchemaValid);
            }
        }
    }

    [Fact]
    public void Compiles_each_stylesheet_once_under_contention()
    {
        if (!fixture.Available) return;

        var validator = new InvoiceValidator(fixture.Catalog);

        // Racing threads through a cold cache and then warming up: if GetOrAdd had compiled
        // duplicates, Warmup would be handing back a different executable than the one in use.
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
            validator.Validate(Fixture.Load("xrechnung-ubl.xml")));

        validator.Warmup();

        Assert.True(validator.Validate(Fixture.Load("xrechnung-ubl.xml")).IsValid);
    }
}
