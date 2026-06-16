using Microsoft.AspNetCore.Components;
using Nova.Shared.Account;
using Nova.UI.Components;

namespace Nova.UI.Features.Account.Components;

/// <summary>
/// Interactive panel that lets the current ClubAdmin promote another club member to ClubAdmin
/// before deleting their own account. On successful assignment, triggers a full page reload
/// so the parent page re-evaluates the deletion scenario.
/// </summary>
public partial class AssignClubAdminPanel(IClubMemberService clubMemberService, NavigationManager navigationManager) : NovaComponentBase
{
    private IReadOnlyList<ClubMemberDto> _members = [];
    private long? _selectedUserId;
    private bool _loading = true;
    private bool _submitting;
    private string? _submitError;

    /// <summary>
    /// The current list of club members available for admin assignment.
    /// Persisted across prerender → interactive attach to avoid duplicate initial fetch.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<ClubMemberDto> Members
    {
        get => _members;
        set => _members = value ?? [];
    }

    /// <summary>
    /// The initial-load error message shown when club members cannot be fetched.
    /// Persisted across prerender → interactive attach.
    /// </summary>
    [PersistentState]
    public string? Error { get; set; }

    /// <summary>
    /// Whether initial data has already been loaded during prerendering.
    /// Persisted to prevent duplicate initial API calls after hydration.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _loading = false;
            return;
        }

        var result = await clubMemberService.GetClubMembersAsync(ComponentCancellationToken);
        result.Switch(
            members => Members = members,
            problem => Error = problem.Detail ?? "Failed to load club members.");
        Initialized = true;
        _loading = false;
    }

    /// <summary>Handles the "Make this person a club admin" button click.</summary>
    /// <returns>A task that completes when the assignment attempt finishes.</returns>
    private async Task AssignAsync()
    {
        if (_selectedUserId is null)
        {
            return;
        }

        _submitting = true;
        _submitError = null;

        var result = await clubMemberService.AssignClubAdminAsync(
            new AssignAdminInput { TargetUserId = _selectedUserId.Value },
            ComponentCancellationToken);
        result.Switch(
            _ => navigationManager.Refresh(forceReload: true),
            problem =>
            {
                _submitError = problem.Detail ?? "Failed to assign admin. Please try again.";
                _submitting = false;
            });
    }
}
