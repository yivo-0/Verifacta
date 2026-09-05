using System.Text;

namespace Verifacta.Tests;

/// <summary>
/// Invoices arrive from whoever sent them, so a host has to be able to say how much one document is
/// allowed to cost before it is refused.
/// </summary>
public class DocumentLimitsTests
{
    [Fact]
    public void Refuses_a_document_over_the_size_limit()
    {
        var xml = Encoding.UTF8.GetBytes(Padded(4096));

        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Load(new MemoryStream(xml), new DocumentLimits { MaxBytes = 1024 }));

        Assert.Contains("larger than", exception.Message);
        Assert.Contains("MaxBytes", exception.Message);
    }

    [Fact]
    public void Stops_reading_at_the_limit_rather_than_after()
    {
        // A stream that would never end: the limit has to be enforced as bytes arrive, not once the
        // whole thing is in memory.
        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Load(new EndlessStream(), new DocumentLimits { MaxBytes = 256 * 1024 }));

        Assert.Contains("larger than", exception.Message);
    }

    [Fact]
    public void Accepts_a_document_inside_the_limit()
    {
        var document = InvoiceDocument.Load(
            new MemoryStream(Encoding.UTF8.GetBytes(Padded(16))),
            new DocumentLimits { MaxBytes = 64 * 1024 });

        Assert.Equal(Detection.InvoiceSyntax.Ubl, document.Syntax);
    }

    [Fact]
    public void Default_limits_are_generous_enough_for_a_real_invoice()
    {
        var document = Fixture.Load("xrechnung-ubl.xml");

        Assert.Equal(Detection.InvoiceSyntax.Ubl, document.Syntax);
        Assert.True(DocumentLimits.Default.MaxBytes >= 16 * 1024 * 1024);
        Assert.True(DocumentLimits.Default.MaxAttachmentBytes >= 8 * 1024 * 1024);
    }

    [Fact]
    public void The_name_tree_cap_is_configurable()
    {
        Assert.Equal(4096, DocumentLimits.Default.MaxPdfNameTreeNodes);
        Assert.Equal(8, new DocumentLimits { MaxPdfNameTreeNodes = 8 }.MaxPdfNameTreeNodes);
    }

    private static string Padded(int comments) =>
        """<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">""" +
        string.Concat(Enumerable.Repeat("<!-- padding padding padding padding -->", comments)) +
        "</Invoice>";

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)' ', offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
