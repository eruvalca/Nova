using Nova.Features.Shared;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Players;

/// <summary>
/// Maps the minimal API endpoints for player creation and permanent-profile editing.
/// </summary>
internal static class PlayerManagementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the player management endpoints under the players group with ClubAdmin authorization.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapPlayerManagementEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(PlayerEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            // Create a new player and enroll them in all Active campaigns atomically.
            group.MapPost(PlayerEndpoints.CreateRelative, CreatePlayerHandler)
                .Produces<PlayerDto>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreatePlayer");

            // Update a player's permanent profile fields.
            group.MapPut(PlayerEndpoints.UpdateRelative, UpdatePlayerHandler)
                .Produces<PlayerDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdatePlayer");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST /api/players — creates a new player and enrolls them in all Active campaigns.
    /// </summary>
    private static async Task<IResult> CreatePlayerHandler(
        CreatePlayerInput input,
        IPlayerManagementService playerManagementService,
        CancellationToken cancellationToken)
    {
        var result = await playerManagementService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(player => TypedResults.Created((string?)null, player));
    }

    /// <summary>
    /// Handles PUT /api/players/{playerId} — updates a player's permanent profile fields.
    /// </summary>
    private static async Task<IResult> UpdatePlayerHandler(
        long playerId,
        UpdatePlayerInput input,
        IPlayerManagementService playerManagementService,
        CancellationToken cancellationToken)
    {
        // Ensure the route parameter and body agree on the target player.
        if (playerId != input.PlayerId)
        {
            return ServiceProblem.BadRequest("The player identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await playerManagementService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
