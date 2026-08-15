namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reports the new concurrency token after a placement mutation succeeds.
/// </summary>
/// <param name="ConcurrencyToken">The token callers must use for the next mutation.</param>
public readonly record struct PlacementMutationSuccess(Guid ConcurrencyToken);
