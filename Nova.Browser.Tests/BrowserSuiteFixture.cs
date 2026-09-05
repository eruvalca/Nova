using Nova.Features.Campaigns;
using Nova.Integration.Tests.Data;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Aspire-hosted browser suite fixture: starts the Nova AppHost once per collection, launches a
/// shared Chromium instance, and exposes helpers that seed an authenticated browser session for
/// a club member. Context diagnostics are isolated under the verification artifact directory.
/// </summary>
public sealed class BrowserSuiteFixture : IAsyncLifetime
{
    private readonly NovaAppHostFixture _appHost = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Gets the shared AppHost fixture used for HTTP registration and database seeding.</summary>
    public NovaAppHostFixture AppHost => _appHost;

    /// <summary>Gets the base URI of the running web app.</summary>
    public Uri BaseUri => _appHost.NovaBaseUri;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            await _appHost.InitializeAsync();
            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var headed = Environment.GetEnvironmentVariable("NOVA_BROWSER_HEADED") == "1";
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !headed,
                ArtifactsDir = BrowserTestArtifacts.RunDirectory,
                TracesDir = Path.Combine(BrowserTestArtifacts.RunDirectory, "traces")
            });
        }
        catch (Exception initializationException)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Browser initialization and cleanup both failed.", initializationException, cleanupException);
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var browser = _browser;
        var playwright = _playwright;
        _browser = null;
        _playwright = null;
        try
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                playwright?.Dispose();
            }
            finally
            {
                await _appHost.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Creates a new browser context for the given user, signs them in through the real login
    /// page, and returns the context with one open page.
    /// </summary>
    /// <param name="email">The registered user's e-mail address.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="viewport">The viewport size, or <see langword="null"/> for the default 1280×800.</param>
    /// <param name="javaScriptEnabled">
    /// Whether JavaScript is enabled in the context. When <see langword="false"/>, the sign-in
    /// still works because the SSR <c>EditForm</c> posts natively (no enhanced navigation, no
    /// page JS required); Blazor renders the form fields (antiforgery token + <c>_handler</c>)
    /// into static HTML.
    /// </param>
    /// <returns>The signed-in browser context.</returns>
    public async Task<IBrowserContext> NewSignedInContextAsync(
        string email,
        string password,
        ViewportSize? viewport = null,
        bool javaScriptEnabled = true)
    {
        var context = await NewAnonymousContextAsync(viewport, javaScriptEnabled);
        try
        {
            await SignInAsync(context.Pages[0], email, password);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Creates a new anonymous browser context (no sign-in) with one open page.
    /// </summary>
    /// <param name="viewport">The viewport size, or <see langword="null"/> for the default 1280×800.</param>
    /// <param name="javaScriptEnabled">Whether JavaScript is enabled in the context.</param>
    /// <returns>The anonymous browser context.</returns>
    public async Task<IBrowserContext> NewAnonymousContextAsync(
        ViewportSize? viewport = null,
        bool javaScriptEnabled = true)
    {
        var browser = _browser ?? throw new InvalidOperationException("Playwright has not been started.");
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            JavaScriptEnabled = javaScriptEnabled,
            ViewportSize = viewport ?? new ViewportSize { Width = 1280, Height = 800 }
        });
        try
        {
            await AttachDiagnosticsAsync(context);
            _ = await context.NewPageAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static async Task AttachDiagnosticsAsync(IBrowserContext context)
    {
        var contextId = Guid.NewGuid().ToString("N");
        var logPath = Path.Combine(BrowserTestArtifacts.ForCurrentTest("logs"), $"{contextId}.log");
        var writeLock = new object();
        void Write(string message)
        {
            lock (writeLock)
            {
                File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            }
        }

        Write($"Context {contextId}; test {TestContext.Current.Test?.TestDisplayName}");
        context.Console += (_, message) => Write($"console.{message.Type}: {message.Text}");
        context.WebError += (_, error) => Write($"page-error: {error.Error}");
        context.RequestFailed += (_, request) => Write($"request-failed: {request.Method} {request.Url}: {request.Failure}");
        context.Close += (_, _) => Write("Context closed.");
        if (Environment.GetEnvironmentVariable("NOVA_BROWSER_TRACE") == "1")
        {
            // Named intermediate traces survive context/browser disposal in the explicit TracesDir.
            // Keeping native tracing avoids a second browser-context lifetime wrapper in every test.
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Name = contextId,
                Title = TestContext.Current.Test?.TestDisplayName,
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
            Write($"Trace prefix: {Path.Combine(BrowserTestArtifacts.RunDirectory, "traces", contextId)}");
        }
    }

    /// <summary>
    /// Signs a page in through the real Identity login flow. The registered user's profile photo
    /// is already complete, so the login redirects straight through the photo gate.
    /// </summary>
    /// <param name="page">The page to sign in.</param>
    /// <param name="email">The registered user's e-mail address.</param>
    /// <param name="password">The user's password.</param>
    /// <returns>A task that completes when the login redirect has settled.</returns>
    private async Task SignInAsync(IPage page, string email, string password)
    {
        await page.GotoAsync(new Uri(BaseUri, "/Account/Login").ToString());
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Closes a campaign as a club administrator directly through the server-side lifecycle
    /// service (there is no close UI or HTTP endpoint yet). The simulated user is scoped with
    /// <see cref="NovaAppHostFixture.UseUser"/> so the previous flow-local values are restored on
    /// completion without affecting concurrently running tests.
    /// </summary>
    /// <param name="campaignId">The campaign to close.</param>
    /// <param name="adminUserId">The acting administrator's user identifier.</param>
    /// <param name="clubId">The administrator's club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the campaign is closed.</returns>
    public async Task CloseCampaignAsAdminAsync(
        long campaignId,
        long adminUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        var factory = _appHost.CreateTenantContextFactory();
        var currentUser = _appHost.CurrentUser;
        using var userScope = _appHost.UseUser(adminUserId, clubId, isClubAdmin: true);

        var service = new CampaignLifecycleService(
            factory,
            currentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignLifecycleService>.Instance);
        var result = await service.CloseAsync(campaignId, cancellationToken);
        result.IsT0.ShouldBeTrue();
    }
}

/// <summary>
/// Collection definition that shares one AppHost + browser instance across all browser tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BrowserSuiteCollection : ICollectionFixture<BrowserSuiteFixture>
{
    /// <summary>The collection name used by browser tests.</summary>
    public const string Name = "BrowserSuite";
}
