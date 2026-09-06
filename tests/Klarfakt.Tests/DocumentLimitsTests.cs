using System.Text;

namespace Klarfakt.Tests;

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
    public void Abandons_an_attachment_that_inflates_past_the_limit()
    {
        // 64 MB of zeros in about 64 KB of deflate. Checking the limit after decompressing would
        // charge the whole 64 MB before refusing it, and the ratio scales as far as the sender
        // likes — that is the entire trick of a compression bomb.
        var pdf = PdfWithFlateAttachment(64 * 1024 * 1024);
        Assert.True(pdf.Length < 200 * 1024, $"the bomb should be small on disk, was {pdf.Length:N0}");

        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Load(new MemoryStream(pdf), new DocumentLimits { MaxAttachmentBytes = 1024 * 1024 }));

        Assert.Contains("past the", exception.Message);
        Assert.Contains("factur-x.xml", exception.Message);
    }

    [Fact]
    public void Refuses_a_document_whose_attachments_add_up()
    {
        // Eight attachments, each comfortably inside the per-attachment limit and together well
        // past the total. A cap on one attachment bounds nothing without this.
        var pdf = PdfWithFlateAttachments(Enumerable.Range(0, 8).Select(_ => 4 * 1024 * 1024).ToArray());

        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Load(new MemoryStream(pdf), new DocumentLimits
            {
                MaxAttachmentBytes = 8 * 1024 * 1024,
                MaxTotalAttachmentBytes = 12 * 1024 * 1024,
            }));

        Assert.Contains("come to over", exception.Message);
        Assert.Contains("MaxTotalAttachmentBytes", exception.Message);
    }

    [Fact]
    public void Reads_a_compressed_attachment_that_fits()
    {
        var pdf = PdfWithFlateAttachment(0, Encoding.UTF8.GetBytes(Padded(4)));

        var document = InvoiceDocument.Load(new MemoryStream(pdf));

        Assert.Equal("factur-x.xml", document.EmbeddedFileName);
        Assert.Equal(Detection.InvoiceSyntax.Ubl, document.Syntax);
    }

    /// <summary>A PDF whose only attachment is a Flate stream of <paramref name="zeros"/> bytes.</summary>
    private static byte[] PdfWithFlateAttachment(int zeros, byte[]? content = null) =>
        PdfWithFlateAttachments([zeros], content);

    /// <summary>
    /// A PDF carrying one Flate attachment per size given. Object numbering: 1-3 are the catalogue
    /// and page, 4 is the name tree, then a file specification and a stream for each attachment.
    /// </summary>
    private static byte[] PdfWithFlateAttachments(int[] sizes, byte[]? content = null)
    {
        var payloads = sizes.Select(size => Deflate(content ?? new byte[size])).ToArray();
        var first = 5;

        // The first is named as the invoice so a single-attachment document is a hybrid invoice;
        // the rest are only there to add up.
        var fileNames = sizes.Select((_, i) => i == 0 ? "factur-x.xml" : $"attachment-{i}.xml").ToArray();
        var names = string.Join(" ", fileNames.Select((name, i) => $"({name}) {first + i * 2} 0 R"));

        var objects = new List<byte[]>
        {
            "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles 4 0 R >> >>"u8.ToArray(),
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"u8.ToArray(),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"u8.ToArray(),
            Encoding.ASCII.GetBytes($"<< /Names [{names}] >>"),
        };

        for (var index = 0; index < sizes.Length; index++)
        {
            var file = fileNames[index];
            objects.Add(Encoding.ASCII.GetBytes(
                $"<< /Type /Filespec /F ({file}) /UF ({file}) /EF << /F {first + index * 2 + 1} 0 R >> >>"));
            objects.Add([]);   // placeholder; the stream object is written separately below
        }

        var pdf = new MemoryStream();
        Append(pdf, "%PDF-1.7\n");

        var offsets = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Position);
            Append(pdf, $"{index + 1} 0 obj\n");

            if (objects[index].Length == 0)
            {
                var payload = payloads[(index - first) / 2];
                Append(pdf, $"<< /Type /EmbeddedFile /Filter /FlateDecode /Length {payload.Length} >>\nstream\n");
                pdf.Write(payload);
                Append(pdf, "\nendstream");
            }
            else
            {
                pdf.Write(objects[index]);
            }

            Append(pdf, "\nendobj\n");
        }

        var startXref = pdf.Position;
        Append(pdf, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Append(pdf, $"{offset:D10} 00000 n \n");
        }

        Append(pdf, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] Deflate(byte[] content)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return compressed.ToArray();
    }

    private static void Append(MemoryStream stream, string text) =>
        stream.Write(Encoding.ASCII.GetBytes(text));

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
