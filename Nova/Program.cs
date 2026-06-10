using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Nova.Components;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<ClubMembershipClaimRefresher>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();

var novaDbConnectionString = builder.Configuration.GetConnectionString("novadb");
var tenantInterceptor = new TenantSaveChangesInterceptor();

// Tenant-scoped contexts. Factories are scoped so the ICurrentUserProvider dependency
// resolves from the current request/circuit scope.
builder.Services.AddDbContextFactory<NovaDbContext>(
    options => options.UseNpgsql(novaDbConnectionString).AddInterceptors(tenantInterceptor),
    ServiceLifetime.Scoped);
builder.Services.AddDbContextFactory<NovaReadDbContext>(
    options => options.UseNpgsql(novaDbConnectionString),
    ServiceLifetime.Scoped);
builder.Services.AddDbContextFactory<NovaAdminDbContext>(
    options => options.UseNpgsql(novaDbConnectionString).AddInterceptors(tenantInterceptor),
    ServiceLifetime.Scoped);

builder.EnrichNpgsqlDbContext<NovaDbContext>();
builder.EnrichNpgsqlDbContext<NovaReadDbContext>();
builder.EnrichNpgsqlDbContext<NovaAdminDbContext>();

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<NovaUserEntity>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole<long>>()
    .AddEntityFrameworkStores<NovaAdminDbContext>()
    .AddClaimsPrincipalFactory<NovaUserClaimsPrincipalFactory>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.RequireAdmin, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(Policies.RequireClubAdmin, policy => policy.RequireRole(Roles.ClubAdmin, Roles.Admin))
    .AddPolicy(Policies.RequireClubMember, policy => policy.RequireAuthenticatedUser().RequireClaim(NovaClaimTypes.ClubId));

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IEmailSender<NovaUserEntity>, IdentityNoOpEmailSender>();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<CookieSecuritySchemeTransformer>());

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrEmpty(traceId))
        {
            context.ProblemDetails.Extensions["traceId"] = traceId;
        }
    };
});

builder.Services.AddValidation();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// For API routes, use ProblemDetails for error responses
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    appBuilder =>
    {
        appBuilder.UseExceptionHandler();
        appBuilder.UseStatusCodePages();
    });

// For non-API routes, use the not-found page
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    appBuilder => appBuilder.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Nova.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // Migrations are attributed to NovaDbContext, so it must be the context that applies them.
        var context = services.GetRequiredService<NovaDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<long>>>();

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            if (app.Environment.IsDevelopment())
            {
                await context.Database.MigrateAsync();
            }

            string[] roles = [Roles.Admin, Roles.ClubAdmin, Roles.StandardUser];

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var roleResult = await roleManager.CreateAsync(new IdentityRole<long>(role));
                    if (!roleResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create role '{role}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
                    }
                }
            }
        });
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

await app.RunAsync();

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

        if (authenticationSchemes.Any(scheme => scheme.Name == IdentityConstants.ApplicationScheme))
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
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
                foreach (var pathItem in document.Paths.Values)
                {
                    if (pathItem.Operations is null)
                    {
                        continue;
                    }

                    foreach (var operation in pathItem.Operations)
                    {
                        operation.Value.Security ??= [];
                        operation.Value.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(IdentityConstants.ApplicationScheme, document)] = []
                        });
                    }
                }
            }
        }
    }
}
