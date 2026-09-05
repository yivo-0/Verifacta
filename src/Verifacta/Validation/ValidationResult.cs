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
        string? value,
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
        Value = value;
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

    /// <summary>
    /// The value at <see cref="Location"/> in the document, when the location resolves to a single
    /// node. SVRL does not carry one, and an application turning a rule message into something a
    /// user can act on almost always needs it. Collapsed to one line and truncated; null when the
    /// location is absent, does not resolve, or the node is empty.
    /// </summary>
    public string? Value { get; }

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
        bool schemaValid,
        bool schemaChecked,
        bool profileCovered)
    {
        RuleSet = ruleSet;
        RulePacks = rulePacks;
        Findings = findings;
        SchemaValid = schemaValid;
        SchemaChecked = schemaChecked;
        ProfileCovered = profileCovered;
    }

    public RuleSet RuleSet { get; }

    public IReadOnlyList<string> RulePacks { get; }

    public IReadOnlyList<ValidationFinding> Findings { get; }

    /// <summary>
    /// False when the document does not conform to its XML Schema. The business rules are not run
    /// in that case, so an empty rule-finding list does not mean the invoice is otherwise sound.
    /// It is also true when the schema was never checked — see <see cref="SchemaChecked"/>.
    /// </summary>
    public bool SchemaValid { get; }

    /// <summary>
    /// Whether the XML Schema was actually checked. False when the caller passed
    /// validateSchema: false, which is the only case where <see cref="SchemaValid"/> means
    /// "not checked" rather than "conformant".
    /// </summary>
    public bool SchemaChecked { get; }

    /// <summary>
    /// False when the document declares a specification with no rule set of its own here, so it was
    /// judged against EN 16931 alone. The verdict on the core rules stands; the national or sector
    /// rules the document claims to follow were never applied. Validate in strict mode to have that
    /// reported as an error instead.
    /// </summary>
    public bool ProfileCovered { get; }

    public bool IsValid => !Findings.Any(finding => finding.Severity == ValidationSeverity.Error);

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
