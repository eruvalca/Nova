using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Nova;

/// <summary>
/// Represents the Cookie Security Scheme Transformer.
/// </summary>
/// <param name="authenticationSchemeProvider">The authentication Scheme Provider.</param>
internal sealed class CookieSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    /// <summary>
    /// Executes the Transform Async operation.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation Token.</param>
    /// <returns>The operation result.</returns>
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();

        if (authenticationSchemes.Any(scheme => string.Equals(scheme.Name, IdentityConstants.ApplicationScheme, StringComparison.Ordinal)))
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
            {
                [IdentityConstants.ApplicationScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = ".AspNetCore.Identity.Application",
                    Description = "ASP.NET Core Identity cookie authentication. Login via /Account/Login to obtain the cookie."
                }
            };

            if (document.Paths is not null)
            {
                foreach (var operations in document.Paths.Values.Select(pathItem => pathItem.Operations))
                {
                    if (operations is null)
                    {
                        continue;
                    }

                    foreach (var operation in operations.Values)
                    {
                        operation.Security ??= [];
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(IdentityConstants.ApplicationScheme, document)] = []
                        });
                    }
                }
            }
        }
    }
}
