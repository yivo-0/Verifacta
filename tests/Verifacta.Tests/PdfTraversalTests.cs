using System.Text;

namespace Verifacta.Tests;

/// <summary>
/// PDFs arrive from whoever sent the invoice. A name tree whose /Kids points back at an ancestor
/// used to recurse until the process died on a stack overflow — 424 bytes, and .NET gives no caller
/// the chance to catch it, so a hosted validator lost the worker rather than the document.
/// </summary>
public class PdfTraversalTests
{
    [Fact]
    public void Survives_a_name_tree_that_points_at_itself()
    {
        var pdf = Build("<< /Kids [4 0 R] >>");

        var exception = Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Load(Stream(pdf)));

        Assert.Contains("no embedded files", exception.Message);
    }

    [Fact]
    public void Survives_a_cycle_between_two_nodes()
    {
        var pdf = Build("<< /Kids [5 0 R] >>", "<< /Kids [4 0 R] >>");

        Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Load(Stream(pdf)));
    }

    [Fact]
    public void Survives_a_node_that_is_its_own_grandchild()
    {
        var pdf = Build("<< /Kids [5 0 R] >>", "<< /Kids [6 0 R] >>", "<< /Kids [4 0 R] >>");

        Assert.Throws<UnsupportedDocumentException>(() => InvoiceDocument.Load(Stream(pdf)));
    }

    [Fact]
    public void Refuses_a_tree_that_is_only_there_to_be_walked()
    {
        // 6000 nodes chained end to end: acyclic, so a visited set alone would still walk all of it.
        var nodes = Enumerable.Range(0, 6000)
            .Select(index => index == 5999 ? "<< >>" : $"<< /Kids [{index + 5} 0 R] >>")
            .ToArray();

        var exception = Assert.Throws<UnsupportedDocumentException>(
            () => InvoiceDocument.Load(Stream(Build(nodes))));

        Assert.Contains("more than", exception.Message);
    }

    private static MemoryStream Stream(byte[] pdf) => new(pdf);

    /// <summary>
    /// A minimal PDF whose /Names /EmbeddedFiles points at object 4; the arguments become objects
    /// 4, 5, 6 … so a test can wire the tree into whatever shape it needs.
    /// </summary>
    private static byte[] Build(params string[] treeNodes)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
        };
        objects.AddRange(treeNodes);

        var pdf = new MemoryStream();
        Write(pdf, "%PDF-1.7\n");

        var offsets = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Position);
            Write(pdf, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var startXref = pdf.Position;
        Write(pdf, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(pdf, $"{offset:D10} 00000 n \n");
        }

        Write(pdf, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static void Write(MemoryStream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
