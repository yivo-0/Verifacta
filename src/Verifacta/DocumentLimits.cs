namespace Verifacta;

/// <summary>
/// Bounds on what a single document may cost to read. Invoices arrive from whoever sent them, so a
/// host processing inbound mail needs a document that refuses to be read rather than one that takes
/// the worker with it. The defaults are far above any real invoice and are meant to stop abuse, not
/// to be tuned for normal traffic.
/// </summary>
public sealed class DocumentLimits
{
    public static DocumentLimits Default { get; } = new();

    /// <summary>The largest file or stream that will be read at all. 64 MB.</summary>
    public long MaxBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>The largest attachment that will be taken out of a hybrid PDF. 32 MB.</summary>
    public long MaxAttachmentBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>
    /// The most that will be extracted from one document in total. Without it, a per-attachment
    /// limit bounds nothing useful: a PDF may carry as many embedded files as it likes, each one
    /// just under the individual cap. 64 MB.
    /// </summary>
    public long MaxTotalAttachmentBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// How far the embedded-files name tree of a PDF will be walked. A real hybrid invoice uses a
    /// handful of nodes; the cap is what stops a tree built only to be walked.
    /// </summary>
    public int MaxPdfNameTreeNodes { get; init; } = 4096;
}
