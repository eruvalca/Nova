namespace Nova.Shared.Account;

/// <summary>Identifies how a user's account deletion interacts with club ownership.</summary>
public enum AccountDeletionScenario
{
    /// <summary>User has no club, or is a non-admin member of a club. Standard deletion.</summary>
    NoClubOrNonAdmin = 0,

    /// <summary>User is a ClubAdmin and the only member of their club. Club is deleted with the user.</summary>
    OnlyClubMember = 1,

    /// <summary>User is the only ClubAdmin but other members exist. Another admin must be assigned first.</summary>
    SoleClubAdmin = 2,
}
