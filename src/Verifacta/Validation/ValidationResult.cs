namespace Verifacta.Validation;

public enum RuleSet
{
    En16931,
    PeppolBisBilling3,
    XRechnung,
}

public enum ValidationSeverity
{
    /// <summary>Advisory only. The German rules use flag="information" for these.</summary>
    Information,
    Warning,
    Error,
}

public sealed class ValidationFinding
{
    internal ValidationFinding(
        string ruleId,
        ValidationSeverity severity,
        string? flag,
        string message,
        string location,
        string rawLocation,
        string? test,
        IReadOnlyList<string> businessTerms,
        string rulePack,
        string artefact)
    {
        RuleId = ruleId;
        Severity = severity;
        Flag = flag;
        Message = message;
        Location = location;
        RawLocation = rawLocation;
        Test = test;
        BusinessTerms = businessTerms;
        RulePack = rulePack;
        Artefact = artefact;
    }

    public string RuleId { get; }

    public ValidationSeverity Severity { get; }

    /// <summary>The raw SVRL flag, kept so a verdict can be traced back to the artefact.</summary>
    public string? Flag { get; }

    public string Message { get; }

    public string Location { get; }

    public string RawLocation { get; }

    public string? Test { get; }

    public IReadOnlyList<string> BusinessTerms { get; }

    public string RulePack { get; }

    /// <summary>
    /// The artefact that produced this finding, which is what separates a CEN core rule from a
    /// national or CIUS rule layered on top of it.
    /// </summary>
    public string Artefact { get; }

    public override string ToString() => $"[{Severity}] {RuleId} at {Location}: {Message}";
}

public sealed class ValidationResult
{
    internal ValidationResult(
        RuleSet ruleSet,
        IReadOnlyList<string> rulePacks,
        IReadOnlyList<ValidationFinding> findings,
        bool schemaValid)
    {
        RuleSet = ruleSet;
        RulePacks = rulePacks;
        Findings = findings;
        SchemaValid = schemaValid;
    }

    public RuleSet RuleSet { get; }

    public IReadOnlyList<string> RulePacks { get; }

    public IReadOnlyList<ValidationFinding> Findings { get; }

    /// <summary>
    /// False when the document does not conform to its XML Schema. The business rules are not run
    /// in that case, so an empty rule-finding list does not mean the invoice is otherwise sound.
    /// </summary>
    public bool SchemaValid { get; }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<ValidationFinding> Errors =>
        Findings.Where(finding => finding.Severity == ValidationSeverity.Error).ToList();

    public IReadOnlyList<ValidationFinding> Warnings =>
        Findings.Where(finding => finding.Severity == ValidationSeverity.Warning).ToList();

    public IReadOnlyList<ValidationFinding> Information =>
        Findings.Where(finding => finding.Severity == ValidationSeverity.Information).ToList();
}

public sealed class ValidationException : Exception
{
    public ValidationException(string message, Exception inner) : base(message, inner)
    {
    }
}
