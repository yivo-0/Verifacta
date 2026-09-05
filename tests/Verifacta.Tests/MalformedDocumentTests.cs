using Verifacta.Validation;

namespace Verifacta.Tests;

/// <summary>
/// A corrupt file in an archive is normal, not exceptional. Every entry point has to fail the same
/// way for it, because the commands that process folders decide what to do from the exception type
/// — and an XmlException escaping mid-batch abandoned the whole run and wrote no report at all.
/// </summary>
[Collection("validator")]
public class MalformedDocumentTests(ValidatorFixture fixture)
{
    private const string Truncated = """<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><cbc:ID>""";

    [Fact]
    public void Parsing_truncated_xml_reports_an_unusable_document()
    {
        var exception = Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Parse(Truncated));

        Assert.Contains("could not be parsed", exception.Message);
    }

    [Fact]
    public void Loading_truncated_xml_from_a_stream_reports_an_unusable_document()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Truncated));

        Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Load(stream));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<unclosed>")]
    [InlineData("<a><b/></a><c/>")]
    public void Rejects_anything_that_is_not_a_well_formed_document(string content)
    {
        Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Parse(content));
    }

    [Fact]
    public void A_malformed_file_becomes_a_row_rather_than_ending_the_batch()
    {
        if (!fixture.Available) return;

        var root = Directory.CreateTempSubdirectory("verifacta-malformed");

        try
        {
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrechnung-ubl.xml"),
                Path.Combine(root.FullName, "good.xml"));
            File.WriteAllText(Path.Combine(root.FullName, "broken.xml"), Truncated);

            var files = Batch.Expand([root.FullName], recursive: false);
            var reports = Batch.Run(fixture.Validator!, files, null, validateSchema: true);

            Assert.Equal(2, reports.Count);
            Assert.Equal("error", Assert.Single(reports, report => report.File.EndsWith("broken.xml")).Status);
            Assert.Equal("valid", Assert.Single(reports, report => report.File.EndsWith("good.xml")).Status);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
