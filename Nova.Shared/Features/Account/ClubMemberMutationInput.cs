using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Account;

/// <summary>Identifies the club member targeted by an administrative membership mutation.</summary>
public sealed record ClubMemberMutationInput
{
    /// <summary>Gets the identity user id of the targeted member.</summary>
    [Range(1, long.MaxValue)]
    public long MemberUserId { get; init; }
}
