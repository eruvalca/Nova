using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Clubs;
using Nova.Shared.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubService"/>: creates clubs and searches for clubs.
/// </summary>
public sealed partial class ClubService(
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubService> logger) : IClubService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDto>> CreateClubAsync(CreateClubInput input, CancellationToken cancellationToken = default)
    {
        // Validate input
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors["Name"] = ["Club name is required."];
        }
        else if (input.Name.Trim().Length > 200)
        {
            errors["Name"] = ["Club name must be 200 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(input.City))
        {
            errors["City"] = ["City is required."];
        }
        else if (input.City.Trim().Length > 100)
        {
            errors["City"] = ["City must be 100 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(input.State))
        {
            errors["State"] = ["State is required."];
        }
        else if (input.State.Trim().Length > 100)
        {
            errors["State"] = ["State must be 100 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        // Check if current user already belongs to a club
        if (currentUserProvider.ClubId.HasValue)
        {
            return ServiceProblem.Conflict("You already belong to a club.");
        }

        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.ServerError("You must be signed in to create a club.");
        }

        try
        {
            await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

            // Load user
            var user = await db.Users.FindAsync([userId], cancellationToken);
            if (user is null)
            {
                return ServiceProblem.ServerError("The current user could not be found.");
            }

            // Create club
            var club = new ClubEntity
            {
                Name = input.Name.Trim(),
                City = input.City.Trim(),
                State = input.State.Trim(),
                CreatedById = userId
            };

            // NpgsqlRetryingExecutionStrategy requires wrapping user-initiated transactions in
            // CreateExecutionStrategy().ExecuteAsync() so the whole unit can be retried on transient failures.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

                db.Clubs.Add(club);
                await db.SaveChangesAsync(cancellationToken);

                // Now club.ClubId is set, assign it to the user
                user.ClubId = club.ClubId;
                db.Users.Update(user);
                await db.SaveChangesAsync(cancellationToken);

                await tx.CommitAsync(cancellationToken);
            });

            // Add ClubAdmin role outside the transaction. UserManager uses the DI-scoped Identity
            // context, which may already track a NovaUserEntity for this request — passing our
            // factory-context instance would cause an identity-map conflict on Attach. Re-fetch
            // the user through UserManager so the role update uses its own tracked instance.
            var identityUser = await userManager.FindByIdAsync(userId.ToString());
            if (identityUser is null)
            {
                LogClubAdminRoleAssignmentFailed(userId, club.ClubId);
            }
            else
            {
                // If the Identity context tracked a stale copy of the user (loaded before our
                // ClubId update), UserManager's UpdateAsync would persist all of its properties
                // and clobber ClubId back to null — stamp the new value on its instance first.
                identityUser.ClubId = club.ClubId;

                var roleResult = await userManager.AddToRoleAsync(identityUser, Roles.ClubAdmin);
                if (!roleResult.Succeeded)
                {
                    LogClubAdminRoleAssignmentFailed(userId, club.ClubId);
                    // Club is created and user is a member; role failure is logged but not fatal for the user
                }
            }

            LogClubCreated(userId, club.ClubId);
            return club.ToClubDto();
        }
        catch (DbUpdateException ex)
        {
            LogClubCreationFailed(ex, userId);
            return ServiceProblem.ServerError("The club could not be created. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubDto>>> SearchClubsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<ClubEntity> baseQuery = db.Clubs;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchTerm = $"%{query}%";
            baseQuery = baseQuery.Where(c =>
                EF.Functions.ILike(c.Name, searchTerm) ||
                EF.Functions.ILike(c.City, searchTerm) ||
                EF.Functions.ILike(c.State, searchTerm));
        }

        var clubs = await baseQuery
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var dtos = clubs.Select(c => c.ToClubDto()).ToList().AsReadOnly();
        return dtos;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Club created successfully: ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogClubCreated(long userId, long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create club for UserId={UserId}.")]
    private partial void LogClubCreationFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to assign ClubAdmin role to UserId={UserId} for ClubId={ClubId}.")]
    private partial void LogClubAdminRoleAssignmentFailed(long userId, long clubId);
}
