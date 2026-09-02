using Microsoft.AspNetCore.Mvc;
using Nova.Features.Shared;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Players;

/// <summary>Maps the administrator-only player CSV template and preview endpoints.</summary>
internal static class PlayerImportEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Maps the player import endpoints.</summary>
        public IEndpointRouteBuilder MapPlayerImportEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(PlayerEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapGet(PlayerEndpoints.ImportTemplateRelative, GetTemplateHandler)
                .Produces(StatusCodes.Status200OK, contentType: PlayerImportConstraints.CsvContentType)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName("GetPlayerImportTemplate");

            group.MapPost(PlayerEndpoints.ImportPreviewRelative, PreviewHandler)
                .Produces<PlayerImportPreview>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithMetadata(
                    new RequestSizeLimitAttribute(PlayerImportConstraints.MaxRequestBytes),
                    new RequestFormLimitsAttribute
                    {
                        MultipartBodyLengthLimit = PlayerImportConstraints.MaxRequestBytes
                    })
                .DisableValidation()
                .DisableAntiforgery()
                .WithName("PreviewPlayerImport");

            return endpoints;
        }
    }

    private static async Task<IResult> GetTemplateHandler(
        IPlayerImportService playerImportService,
        CancellationToken cancellationToken)
    {
        var result = await playerImportService.GetTemplateAsync(cancellationToken);
        return result.ToHttpResult(template => TypedResults.File(
            template.Content,
            template.ContentType,
            template.DownloadFileName));
    }

    private static async Task<IResult> PreviewHandler(
        [FromForm] IFormFile? file,
        IPlayerImportService playerImportService,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ServiceProblem.Validation("file", "A CSV file is required.").ToHttpResult();
        }

        if (file.Length is <= 0 or > PlayerImportConstraints.MaxFileBytes)
        {
            return ServiceProblem.Validation(
                    "file",
                    $"The CSV file must be between 1 and {PlayerImportConstraints.MaxFileBytes} bytes.")
                .ToHttpResult();
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await playerImportService.PreviewAsync(
            new PlayerImportUploadInput
            {
                Content = content,
                FileName = file.FileName,
                ContentType = file.ContentType
            },
            cancellationToken);
        return result.ToHttpResult();
    }
}
