using System.Text;
using Klarfakt.Detection;

namespace Klarfakt.Tests;

/// <summary>
/// Hybrid PDF/A-3 handling, exercised against real Factur-X and ZUGFeRD files including the edge
/// cases the incumbent library hit: an encrypted PDF, one with no attachment, and a corrupt one.
/// </summary>
public class PdfTests
{
    private const string Missing = "(corpus missing)";

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

            foreach (var file in Corpus.Files(Root, ".pdf"))
            {
                data.Add(Path.GetRelativePath(Root, file));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Either_reads_the_invoice_or_explains_why_not(string relativePath)
    {
        if (relativePath == Missing || Root is null) return;

        InvoiceDocument document;
        try
        {
            document = InvoiceDocument.Load(Path.Combine(Root, relativePath));
        }
        catch (UnsupportedDocumentException exception)
        {
            // The message must name the actual problem, not leak a stack-level detail.
            Assert.NotEmpty(exception.Message);
            Assert.DoesNotContain("Object reference", exception.Message);
            return;
        }

        Assert.NotNull(document.EmbeddedFileName);
        var result = document.Read();
        Assert.NotNull(result.Invoice);
    }

    [Theory]
    [InlineData("pdf-facturx/invoice_EN16931.pdf", "factur-x.xml", ProfileKind.En16931, "F20260023")]
    [InlineData("pdf-mustang/EN16931_Einfach.pdf", "factur-x.xml", ProfileKind.En16931, "471102")]
    [InlineData("pdf-mustang/EXTENDED_Fremdwaehrung_wdis_fx-pdfa4.pdf", "factur-x.xml", ProfileKind.FacturX, "47110815")]
    [InlineData("pdf-mustang/ZTESTZUGFERD_1_INVDSS_012015738820PDF-1.pdf", "zugferd-invoice.xml", ProfileKind.FacturX, "2015738820")]
    public void Extracts_the_invoice_from_a_hybrid_pdf(
        string relativePath, string attachment, ProfileKind profile, string invoiceId)
    {
        if (Root is null) return;

        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;

        var document = InvoiceDocument.Load(path);

        Assert.Equal(attachment, document.EmbeddedFileName);
        Assert.Equal(InvoiceSyntax.Cii, document.Syntax);
        Assert.Equal(profile, document.Profile.Kind);

        var result = document.Read();
        Assert.Equal(invoiceId, result.Invoice.Id);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("pdf-edge-cases/not-embedded.pdf", "carries no embedded files")]
    [InlineData("pdf-mustang/EmptyPDFA1.pdf", "carries no embedded files")]
    [InlineData("pdf-mustang/corrupt-factur-x-waytoosmall.pdf", "could not be read")]
    [InlineData("pdf-mustang/zugferd_invoice.pdf", "is not a UBL Invoice")]
    public void Explains_why_a_pdf_is_not_usable(string relativePath, string expected)
    {
        if (Root is null) return;

        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;

        var exception = Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Load(path));
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Writes_pdf_report()
    {
        if (Root is null) return;

        var report = new StringBuilder();
        report.AppendLine("# Hybrid PDF report");
        report.AppendLine();
        report.AppendLine("| File | Attachment | Syntax | Profile | Invoice id | Outcome |");
        report.AppendLine("|---|---|---|---|---|---|");

        foreach (var file in Corpus.Files(Root, ".pdf"))
        {
            var name = Path.GetRelativePath(Root, file).Replace('\\', '/');

            try
            {
                var document = InvoiceDocument.Load(file);
                var invoice = document.Read().Invoice;
                var profile = document.Profile.Kind +
                              (document.Profile.Level is { } level ? $" ({level})" : string.Empty);

                report.AppendLine($"| `{name}` | {document.EmbeddedFileName} | {document.Syntax} " +
                                  $"| {profile} | {invoice.Id} | read |");
            }
            catch (UnsupportedDocumentException exception)
            {
                report.AppendLine($"| `{name}` | — | — | — | — | {Summarise(exception.Message)} |");
            }
        }

        Corpus.WriteReport("pdf-report", report.ToString());
    }

    private static string Summarise(string message)
    {
        var first = message.Split('.')[0].Replace('|', '/');
        return first.Length > 90 ? first[..90] + "…" : first;
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
