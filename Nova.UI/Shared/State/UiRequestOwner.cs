namespace Nova.UI.Shared.State;

/// <summary>Owns one page-selected request lane without prescribing its loading, mutation, or reset behavior.</summary>
internal sealed class UiRequestOwner
{
    /// <summary>The current generation within this independently owned request lane.</summary>
    private long _generation;

    /// <summary>Supersedes earlier work in this lane and captures the currently applied identity.</summary>
    /// <param name="scope">The resolved identity that owns the new request.</param>
    /// <returns>The lease required for result, error, and cleanup publication.</returns>
    public UiRequestLease Begin(UiScopeLease scope) => new(this, ++_generation, scope);

    /// <summary>Joins the current lane generation without superseding its related work.</summary>
    /// <param name="scope">The resolved identity that owns the work.</param>
    /// <returns>A lease on the lane's current generation.</returns>
    public UiRequestLease Capture(UiScopeLease scope) => new(this, _generation, scope);

    /// <summary>Invalidates work at the page's existing route, resource, retry, or disposal boundary.</summary>
    public void Invalidate() => ++_generation;

    /// <summary>Checks lane and identity ownership together before asynchronous work publishes any effects.</summary>
    /// <param name="lease">The ownership captured before starting the work.</param>
    /// <returns>Whether both the operation lane and its applied identity are still current.</returns>
    public bool Owns(UiRequestLease lease)
        => ReferenceEquals(lease.Owner, this) && lease.Generation == _generation && lease.Scope.IsCurrent;
}

/// <summary>Captures both a request lane and the resolved identity that own asynchronous UI publication.</summary>
/// <param name="Owner">The request lane that issued this lease.</param>
/// <param name="Generation">The lane generation captured for the request.</param>
/// <param name="Scope">The applied identity captured for the request.</param>
internal readonly record struct UiRequestLease(UiRequestOwner? Owner, long Generation, UiScopeLease Scope);
