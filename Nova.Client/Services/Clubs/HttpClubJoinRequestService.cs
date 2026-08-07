using System.Net.Http.Json;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IClubJoinRequestService"/> that calls the server's
/// minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpClubJoinRequestService(HttpClient http) : IClubJoinRequestService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> GetCurrentUserPendingRequestAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(ClubEndpoints.PendingRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubJoinRequestDto>(
            "The server returned an invalid pending join request response.",
            IsValidJoinRequest,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> CreateJoinRequestAsync(
        long clubId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.CreateJoinRequestUrl(clubId);
        // POST with empty body — clubId is in the route
        using var response = await http.PostAsJsonAsync(url, new { }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubJoinRequestDto>(
            "The server returned an invalid join request response.",
            request => IsValidJoinRequest(request)
                && request.Status == RequestStatus.Pending
                && request.ClubId == clubId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> CancelJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.CancelJoinRequestUrl(requestId);
        using var response = await http.DeleteAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubJoinRequestDto>>> GetClubJoinRequestsAsync(
        long clubId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.AdminJoinRequestsUrl(clubId);
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<ClubJoinRequestDto>>(
            "The server returned an invalid join request list response.",
            requests => requests.All(request =>
                IsValidJoinRequest(request)
                    && request.Status == RequestStatus.Pending
                    && request.ClubId == clubId)
                && requests.Zip(requests.Skip(1)).All(pair =>
                    pair.First.ClubJoinRequestId < pair.Second.ClubJoinRequestId),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<ClubJoinRequestDto>>>(
            requests => requests.AsReadOnly(),
            problem => problem);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ApproveJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.ApproveJoinRequestUrl(requestId);
        using var response = await http.PostAsync(url, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RejectJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.RejectJoinRequestUrl(requestId);
        using var response = await http.PostAsync(url, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    /// <summary>
    /// Validates the portable invariants of a club join-request success payload.
    /// </summary>
    /// <param name="request">The join request to validate.</param>
    /// <returns><see langword="true"/> when the request is structurally valid.</returns>
    private static bool IsValidJoinRequest(ClubJoinRequestDto request)
        => request is not null
            && request.ClubJoinRequestId > 0
            && request.ClubId > 0
            && !string.IsNullOrWhiteSpace(request.ClubName)
            && request.RequestingUserId > 0
            && !string.IsNullOrWhiteSpace(request.RequestingUserName)
            && request.Status is RequestStatus.Pending
                or RequestStatus.Approved
                or RequestStatus.Rejected
            && request.CreatedAt != default;
}
