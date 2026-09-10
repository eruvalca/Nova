namespace Nova.UI.Features.Campaigns.Components;

/// <summary>One bounded, deliberately selectable search identity.</summary>
public sealed record EvaluationFinderRow(long Id, string Name, int GraduationYear, int? TryoutNumber, string PlacementContext);
