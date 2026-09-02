using System.Net.Http.Json;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services.Clubs;

/// <summary>
/// WebAssembly client implementation of <see cref="IClubMemberService"/> that calls the server's
/// minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpClubMemberService(HttpClient http) : IClubMemberService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubMemberDto>>> GetClubMembersAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(ClubEndpoints.GetMembers, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<ClubMemberDto>>(
            "The server returned an invalid club member list response.",
            members => members.All(IsValidMember),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<ClubMemberDto>>>(
            members => members.AsReadOnly(),
            problem => problem);
    }

    /// <inheritdoc />
    public Task<ServiceResult<OneOf.Types.Success>> PromoteMemberAsync(long memberUserId, CancellationToken cancellationToken = default)
        => SendMutationAsync(HttpMethod.Post, ClubEndpoints.PromoteMemberUrl(memberUserId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<OneOf.Types.Success>> DemoteMemberAsync(long memberUserId, CancellationToken cancellationToken = default)
        => SendMutationAsync(HttpMethod.Post, ClubEndpoints.DemoteMemberUrl(memberUserId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<OneOf.Types.Success>> RemoveMemberAsync(long memberUserId, CancellationToken cancellationToken = default)
        => SendMutationAsync(HttpMethod.Delete, ClubEndpoints.RemoveMemberUrl(memberUserId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<OneOf.Types.Success>> LeaveClubAsync(CancellationToken cancellationToken = default)
        => SendMutationAsync(HttpMethod.Delete, ClubEndpoints.LeaveClub, cancellationToken);

    private async Task<ServiceResult<OneOf.Types.Success>> SendMutationAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new OneOf.Types.Success()
            : await response.ToServiceProblemAsync(cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a club-member success payload.
    /// </summary>
    /// <param name="member">The member to validate.</param>
    /// <returns><see langword="true"/> when the member is structurally valid.</returns>
    private static bool IsValidMember(ClubMemberDto member)
        => member is not null
            && member.UserId > 0
            && !string.IsNullOrWhiteSpace(member.FullName);
}
