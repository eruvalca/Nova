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
    private string? _error;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var result = await clubMemberService.GetClubMembersAsync(ComponentCancellationToken);
        result.Switch(
            members =>
            {
                _members = members;
                _loading = false;
            },
            problem =>
            {
                _error = problem.Detail ?? "Failed to load club members.";
                _loading = false;
            });
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
        _error = null;

        var result = await clubMemberService.AssignClubAdminAsync(_selectedUserId.Value, ComponentCancellationToken);
        result.Switch(
            _ => navigationManager.Refresh(forceReload: true),
            problem =>
            {
                _error = problem.Detail ?? "Failed to assign admin. Please try again.";
                _submitting = false;
            });
    }
}
