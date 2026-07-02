using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Account;

/// <summary>Request body for assigning ClubAdmin to a member.</summary>
public sealed record AssignAdminInput
{
    /// <summary>The ID of the user to promote to ClubAdmin.</summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long TargetUserId { get; init; }
}
