using Verifacta.Model;

namespace Verifacta.Reading;

public enum ReadFindingKind
{
    UnparsableNumber,
    UnparsableDate,
    UnexpectedValue,
}

public sealed class ReadFinding
{
    internal ReadFinding(ReadFindingKind kind, string path, string message)
    {
        Kind = kind;
        Path = path;
        Message = message;
    }

    public ReadFindingKind Kind { get; }

    public string Path { get; }

    public string Message { get; }

    public override string ToString() => $"{Kind} at {Path}: {Message}";
}

public sealed class ReadResult
{
    internal ReadResult(EInvoice invoice, IReadOnlyList<ReadFinding> findings)
    {
        Invoice = invoice;
        Findings = findings;
    }

    public EInvoice Invoice { get; }

    public IReadOnlyList<ReadFinding> Findings { get; }

    public bool HasFindings => Findings.Count > 0;
}
