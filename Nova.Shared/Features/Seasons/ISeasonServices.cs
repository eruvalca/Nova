using Nova.Shared.Results;

namespace Nova.Shared.Features.Seasons;

/// <summary>Creates, updates, and advances club seasons.</summary>
public interface ISeasonCommandService
{
    /// <summary>Creates the club's first current season.</summary>
    Task<ServiceResult<SeasonSummary>> CreateAsync(
        CreateSeasonInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Updates mutable metadata for a tenant-visible season.</summary>
    Task<ServiceResult<SeasonSummary>> UpdateAsync(
        long seasonId,
        UpdateSeasonInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically advances the club to a new current season.</summary>
    Task<ServiceResult<StartNextSeasonResult>> StartNextAsync(
        StartNextSeasonInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads tenant-safe season list and detail projections.</summary>
public interface ISeasonQueryService
{
    /// <summary>Gets a bounded page of seasons with the current season first.</summary>
    Task<ServiceResult<SeasonPageResult>> ListAsync(
        GetSeasonListInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Gets season metadata and bounded campaign history.</summary>
    Task<ServiceResult<SeasonDetailResult>> GetAsync(
        GetSeasonDetailInput input,
        CancellationToken cancellationToken = default);
}
