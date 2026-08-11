using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps the campaign evaluation note add, edit, and delete endpoints.
/// </summary>
internal static class EvaluationNoteEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign evaluation note endpoints under the shared campaign route with club-member authorization.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignEvaluationNoteEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapPost(CampaignEndpoints.AddEvaluationNoteRelative, AddEvaluationNoteHandler)
                .Produces<EvaluationNoteMutationSuccess>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.AddEvaluationNoteRouteName);

            group.MapPut(CampaignEndpoints.EditEvaluationNoteRelative, EditEvaluationNoteHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.EditEvaluationNoteRouteName);

            group.MapDelete(CampaignEndpoints.DeleteEvaluationNoteRelative, DeleteEvaluationNoteHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.DeleteEvaluationNoteRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Adds one evaluation note to a campaign participation.
    /// </summary>
    /// <param name="input">The target participation and note content.</param>
    /// <param name="service">The campaign evaluation note service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 201 response containing the created note identifier, or ProblemDetails.</returns>
    private static async Task<IResult> AddEvaluationNoteHandler(
        AddEvaluationNoteInput input,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(input, cancellationToken);
        return result.ToHttpResult(success => TypedResults.Created((string?)null, success));
    }

    /// <summary>
    /// Edits one evaluation note.
    /// </summary>
    /// <param name="noteId">The evaluation note identifier to edit.</param>
    /// <param name="input">The updated note content.</param>
    /// <param name="service">The campaign evaluation note service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> EditEvaluationNoteHandler(
        long noteId,
        EditEvaluationNoteInput input,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.EditAsync(input with { NoteId = noteId }, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Deletes one evaluation note.
    /// </summary>
    /// <param name="noteId">The evaluation note identifier to delete.</param>
    /// <param name="service">The campaign evaluation note service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> DeleteEvaluationNoteHandler(
        long noteId,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(noteId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
}
