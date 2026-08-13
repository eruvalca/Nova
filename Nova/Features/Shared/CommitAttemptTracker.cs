namespace Nova.Features.Shared;

/// <summary>
/// Tracks whether a mutation attempt reached its commit, scoping ambiguous-commit verification
/// to attempts that could actually have applied the mutation.
/// </summary>
internal sealed class CommitAttemptTracker
{
    private int _attempted;

    /// <summary>Gets a value indicating whether the current attempt reached its commit.</summary>
    public bool Attempted => Volatile.Read(ref _attempted) == 1;

    /// <summary>Clears the flag at the start of an execution-strategy attempt.</summary>
    public void Reset() => Volatile.Write(ref _attempted, 0);

    /// <summary>Marks that the current attempt is about to commit.</summary>
    public void MarkAttempted() => Volatile.Write(ref _attempted, 1);
}
