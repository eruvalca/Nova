namespace Nova.Entities;

/// <summary>Identifies the command path that originally created a season.</summary>
public enum SeasonCreationKind
{
    /// <summary>The season was created atomically with its first campaign.</summary>
    InlineCampaign = 0,

    /// <summary>The season was created by the standalone first-season command.</summary>
    Standalone = 1,

    /// <summary>The season was created by advancing from an existing current season.</summary>
    Advancement = 2
}
