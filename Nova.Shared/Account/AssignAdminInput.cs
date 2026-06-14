using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Account;

/// <summary>Request body for assigning ClubAdmin to a member.</summary>
/// <param name="TargetUserId">The user to promote to ClubAdmin.</param>
public sealed record AssignAdminInput([property: Required] long TargetUserId);
