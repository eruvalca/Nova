using System.Net.Http.Json;
using Nova.Shared.Account;
using Nova.Shared.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services;

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
    public async Task<ServiceResult<bool>> AssignClubAdminAsync(
        AssignAdminInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(ClubEndpoints.AssignAdmin, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<bool>(
            "The server returned an invalid administrator assignment response.",
            assigned => assigned,
            cancellationToken);
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
