namespace ViewerPrn.Domain;

/// <summary>
/// Thrown when code reaches a requirement that the specification leaves undefined.
/// CLAUDE.md forbids inventing or approximating missing product requirements, so the
/// only correct behaviour for a BLOCKED case is to fail loudly instead of guessing.
/// </summary>
public sealed class BlockedRequirementException : Exception
{
    public BlockedRequirementException(string requirement, string specReference)
        : base($"BLOCKED requirement: {requirement}. Awaiting clarification. See {specReference}.")
    {
        Requirement = requirement;
        SpecReference = specReference;
    }

    public string Requirement { get; }

    public string SpecReference { get; }
}
