namespace Nova.Shared.Account;

/// <summary>Describes the implications of deleting the current user's account.</summary>
/// <param name="Scenario">Which deletion scenario applies.</param>
/// <param name="ClubName">The club's name when relevant; otherwise <see langword="null"/>.</param>
/// <param name="OtherMemberCount">Number of other members in the club when relevant; otherwise <see langword="null"/>.</param>
public sealed record AccountDeletionPreviewDto(
    AccountDeletionScenario Scenario,
    string? ClubName,
    int? OtherMemberCount);
