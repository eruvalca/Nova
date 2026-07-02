using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Clubs;

/// <summary>Request body for demoting a ClubAdmin to a regular club member.</summary>
public sealed record DemoteAdminInput
{
    /// <summary>The ID of the user to demote from ClubAdmin.</summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long TargetUserId { get; init; }
}
