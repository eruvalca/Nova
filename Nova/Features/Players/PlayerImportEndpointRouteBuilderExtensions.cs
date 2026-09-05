using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Nova.Features.Shared;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;

namespace Nova.Features.Players;

/// <summary>Maps administrator-only CSV template, preview, and confirmed commit endpoints.</summary>
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
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
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

            group.MapPost(PlayerEndpoints.ImportCommitRelative, CommitHandler)
                .Produces<PlayerImportCompletion>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithMetadata(
                    new RequestSizeLimitAttribute(PlayerImportConstraints.MaxRequestBytes),
                    new RequestFormLimitsAttribute
                    {
                        MultipartBodyLengthLimit = PlayerImportConstraints.MaxRequestBytes,
                        ValueLengthLimit = PlayerImportConstraints.MaxConfirmationTokenCharacters
                    })
                .DisableValidation()
                .DisableAntiforgery()
                .WithName("CommitPlayerImport");

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
        var upload = await ReadUploadAsync(file, cancellationToken);
        return await upload.Match(
            async input => (await playerImportService.PreviewAsync(input, cancellationToken)).ToHttpResult(),
            problem => Task.FromResult(problem.ToHttpResult()));
    }

    /// <summary>Accepts explicit confirmation and the original file for commit or receipt recovery.</summary>
    /// <param name="file">The original CSV upload.</param>
    /// <param name="operationId">The preview identity.</param>
    /// <param name="confirmationToken">The opaque protected preview.</param>
    /// <param name="request">The multipart envelope, checked for ambiguous duplicate fields.</param>
    /// <param name="playerImportService">The authoritative import service.</param>
    /// <param name="cancellationToken">Cancels request processing.</param>
    /// <returns>The completion or trace-correlated problem.</returns>
    private static async Task<IResult> CommitHandler(
        [FromForm] IFormFile? file,
        [FromForm] Guid? operationId,
        [FromForm] string? confirmationToken,
        HttpRequest request,
        IPlayerImportService playerImportService,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.Count > 1
            || form[PlayerImportConstraints.OperationIdFormFieldName].Count > 1
            || form[PlayerImportConstraints.ConfirmationTokenFormFieldName].Count > 1)
        {
            return ServiceProblem.Validation("input", "Submit exactly one file, operation ID, and confirmation token.").ToHttpResult();
        }

        var upload = await ReadUploadAsync(file, cancellationToken);
        return await upload.Match(
            async content =>
            {
                var input = new PlayerImportCommitInput
                {
                    Upload = content,
                    OperationId = operationId ?? Guid.Empty,
                    ConfirmationToken = confirmationToken ?? string.Empty
                };
                var errors = InputValidator.Validate(input);
                return errors.Count > 0
                    ? ServiceProblem.Validation(errors).ToHttpResult()
                    : (await playerImportService.CommitAsync(input, cancellationToken)).ToHttpResult();
            },
            problem => Task.FromResult(problem.ToHttpResult()));
    }

    /// <summary>Reads a CSV using the same metadata and hard byte bounds for preview and commit.</summary>
    /// <param name="file">The untrusted multipart file.</param>
    /// <param name="cancellationToken">Cancels the bounded stream read.</param>
    /// <returns>The original bytes or a validation problem.</returns>
    private static async Task<ServiceResult<PlayerImportUploadInput>> ReadUploadAsync(
        IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ServiceProblem.Validation("file", "A CSV file is required.");
        }

        if (file.Length is <= 0 or > PlayerImportConstraints.MaxFileBytes)
        {
            return ServiceProblem.Validation(
                    "file",
                    $"The CSV file must be between 1 and {PlayerImportConstraints.MaxFileBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(file.FileName)
            || file.FileName.Contains('\r')
            || file.FileName.Contains('\n')
            || !string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceProblem.Validation("file", "The uploaded file must have a .csv extension.");
        }

        if (!string.IsNullOrEmpty(file.ContentType)
            && (!MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType)
                || contentType.MediaType is null
                || !PlayerImportConstraints.AllowedContentTypes.Contains(contentType.MediaType)))
        {
            return ServiceProblem.Validation("file", "The uploaded file type is not supported.");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            var chunk = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + count > PlayerImportConstraints.MaxFileBytes)
                {
                    return ServiceProblem.Validation("file", "The CSV file exceeds the upload limit.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            content = buffer.ToArray();
        }

        return new PlayerImportUploadInput
        {
            Content = content,
            FileName = file.FileName,
            ContentType = file.ContentType
        };
    }
}
