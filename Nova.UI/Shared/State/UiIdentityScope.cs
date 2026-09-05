using System.Security.Claims;
using Nova.Shared.Security;

namespace Nova.UI.Shared.State;

/// <summary>Identifies the user, club, and management authority that own scoped UI state.</summary>
/// <param name="UserId">The identity's user claim.</param>
/// <param name="ClubId">The identity's club claim.</param>
/// <param name="CanManage">Whether the identity has club-administrator authority.</param>
internal readonly record struct UiIdentitySnapshot(string? UserId, string? ClubId, bool CanManage)
{
    /// <summary>Gets the existing recovery-key representation without renaming persisted operations.</summary>
    public string StorageKey => $"{UserId}:{ClubId}:{CanManage}";

    /// <summary>Captures the claims used by the campaign pages without changing their representation.</summary>
    /// <param name="principal">The authenticated principal supplied by the UI authentication provider.</param>
    /// <returns>The user, club, and authority snapshot.</returns>
    public static UiIdentitySnapshot FromPrincipal(ClaimsPrincipal principal) => new(
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        principal.FindFirst(NovaClaimTypes.ClubId)?.Value,
        principal.IsInRole(Roles.ClubAdmin));
}

/// <summary>Orders authentication completions separately from the applied identity that owns UI work.</summary>
/// <remarks>Use on the component renderer. Pending or unchanged authentication does not invalidate applied work.</remarks>
internal sealed class UiIdentityScope : IDisposable
{
    /// <summary>The latest startup or notification sequence allowed to apply an identity.</summary>
    private long _authenticationGeneration;
    /// <summary>The revision of the identity whose resolved snapshot currently owns the page.</summary>
    private long _scopeVersion;
    /// <summary>Whether disposal has permanently invalidated this scope.</summary>
    private bool _isDisposed;

    /// <summary>Gets whether a resolved identity, including an empty identity, has been applied.</summary>
    public bool HasAppliedIdentity { get; private set; }
    /// <summary>Gets the latest applied identity snapshot.</summary>
    public UiIdentitySnapshot Current { get; private set; }
    /// <summary>Gets the existing storage key, or an empty key before authentication first applies.</summary>
    public string StorageKey => HasAppliedIdentity ? Current.StorageKey : string.Empty;

    /// <summary>Starts an ordered authentication read without revoking the currently applied identity.</summary>
    /// <returns>The sequence that must accompany its eventual authentication result.</returns>
    public long BeginAuthentication() => ++_authenticationGeneration;

    /// <summary>Applies only the newest resolved identity and invalidates work only when that identity changes.</summary>
    /// <param name="generation">The sequence captured before awaiting authentication.</param>
    /// <param name="identity">The resolved identity snapshot.</param>
    /// <param name="changed">Whether this is the first applied identity or differs from the previous one.</param>
    /// <returns>Whether this result was current and accepted; stale or disposed results return false.</returns>
    public bool TryApply(long generation, UiIdentitySnapshot identity, out bool changed)
    {
        changed = false;
        if (_isDisposed || generation != _authenticationGeneration)
        {
            return false;
        }

        changed = !HasAppliedIdentity || Current != identity;
        if (changed)
        {
            Current = identity;
            HasAppliedIdentity = true;
            ++_scopeVersion;
        }

        return true;
    }

    /// <summary>Captures the currently applied identity for an asynchronous operation.</summary>
    /// <returns>A lease invalidated by an applied identity change or disposal.</returns>
    public UiScopeLease Capture() => new(this, _scopeVersion);

    /// <summary>Checks whether a captured revision still belongs to this live applied identity.</summary>
    /// <param name="version">The scope revision captured by the operation.</param>
    /// <returns>Whether the operation may still publish under this identity.</returns>
    internal bool Owns(long version) => !_isDisposed && HasAppliedIdentity && version == _scopeVersion;

    /// <summary>Rejects pending authentication results and all operation leases on component disposal.</summary>
    public void Dispose()
    {
        _isDisposed = true;
        ++_authenticationGeneration;
        ++_scopeVersion;
    }
}

/// <summary>Retains the resolved identity revision that owns asynchronous UI work.</summary>
/// <param name="Owner">The identity scope that issued this lease.</param>
/// <param name="Version">The applied identity revision captured for the work.</param>
internal readonly record struct UiScopeLease(UiIdentityScope? Owner, long Version)
{
    /// <summary>Gets whether the lease still belongs to its live, applied identity.</summary>
    public bool IsCurrent => Owner?.Owns(Version) == true;
}
