using System.Linq.Expressions;
using Bunit;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Components.Account.Shared;
using Nova.Entities;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for the shared account components: status strip, panel wall, external picker, passkey submit,
/// recovery codes board, and the reusable form primitives.
/// </summary>
public class AccountSharedComponentsTests
{
    private const string StatusCookieName = "Identity.StatusMessage";

    private sealed class FakeAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("request-token", "__RequestVerificationToken");
    }

    private sealed class FakeAuthenticationHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private static UserManager<NovaUserEntity> CreateUserManager()
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        return Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());
    }

    private static SignInManager<NovaUserEntity> CreateSignInManager(
        IEnumerable<AuthenticationScheme>? schemes = null)
    {
        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            CreateUserManager(),
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        signInManager.GetExternalAuthenticationSchemesAsync()
            .Returns(Task.FromResult(schemes ?? []));
        return signInManager;
    }

    // ---- StatusMessage ----

    [Fact]
    public void StatusMessage_RendersSuccessAlert_WhenMessageIsPassed()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<StatusMessage>(parameters => parameters
            .Add(p => p.Message, "Your password was changed.")
            .AddCascadingValue(new DefaultHttpContext()));

        cut.Markup.ShouldContain("alert-success");
        cut.Markup.ShouldContain("Your password was changed.");
    }

    [Fact]
    public void StatusMessage_RendersDangerAlert_WhenMessageStartsWithError()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<StatusMessage>(parameters => parameters
            .Add(p => p.Message, "Error: The provided token has expired.")
            .AddCascadingValue(new DefaultHttpContext()));

        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("Error: The provided token has expired.");
    }

    [Fact]
    public void StatusMessage_ReadsCookieAndDeletesIt_WhenNoMessageParameter()
    {
        using var testContext = new BunitContext();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{StatusCookieName}={Uri.EscapeDataString("Profile updated")}";

        var cut = testContext.Render<StatusMessage>(parameters => parameters
            .AddCascadingValue(httpContext));

        cut.Markup.ShouldContain("Profile updated");
        httpContext.Response.Headers["Set-Cookie"].ToString().ShouldContain($"{StatusCookieName}=;");
    }

    // ---- ManageNavMenu ----

    [Fact]
    public void ManageNavMenu_RendersAllPanels_WhenNoExternalLogins()
    {
        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => CreateSignInManager());

        var cut = testContext.Render<ManageNavMenu>();

        cut.Markup.ShouldContain("href=\"Account/Manage\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/Email\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/ChangePassword\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/TwoFactorAuthentication\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/Passkeys\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/ProfilePhoto\"");
        cut.Markup.ShouldContain("href=\"Account/Manage/PersonalData\"");
        cut.Markup.ShouldNotContain("External logins");
    }

    [Fact]
    public void ManageNavMenu_RendersExternalLoginsPanel_WhenSchemesConfigured()
    {
        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => CreateSignInManager(
            [new AuthenticationScheme("Google", "Google", typeof(FakeAuthenticationHandler))]));

        var cut = testContext.Render<ManageNavMenu>();

        cut.Markup.ShouldContain("href=\"Account/Manage/ExternalLogins\"");
        cut.Markup.ShouldContain("External logins");
    }

    // ---- ExternalLoginPicker ----

    [Fact]
    public void ExternalLoginPicker_RendersEmptyState_WhenNoSchemes()
    {
        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => CreateSignInManager());
        testContext.Services.AddScoped<AntiforgeryStateProvider>(_ => new FakeAntiforgeryStateProvider());

        var cut = testContext.Render<ExternalLoginPicker>();

        cut.Markup.ShouldContain("There are no external authentication services configured");
    }

    [Fact]
    public void ExternalLoginPicker_RendersProviderButtons_WithContractAttributes()
    {
        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => CreateSignInManager(
            [new AuthenticationScheme("Google", "Google", typeof(FakeAuthenticationHandler))]));
        testContext.Services.AddScoped<AntiforgeryStateProvider>(_ => new FakeAntiforgeryStateProvider());

        var cut = testContext.Render<ExternalLoginPicker>();

        cut.Markup.ShouldContain("action=\"Account/PerformExternalLogin\"");
        cut.Markup.ShouldContain("name=\"provider\"");
        cut.Markup.ShouldContain("value=\"Google\"");
        cut.Markup.ShouldContain("name=\"ReturnUrl\"");
        cut.Markup.ShouldContain("Google");
        cut.Markup.ShouldContain("btn-outline-secondary");
    }

    // ---- PasskeySubmit ----

    [Fact]
    public void PasskeySubmit_RendersButtonAndCustomElement_WithTokens()
    {
        using var testContext = new BunitContext();

        var httpContext = new DefaultHttpContext();
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.GetTokens(httpContext)
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", "RequestVerificationToken"));
        testContext.Services.AddScoped(_ => antiforgery);

        var cut = testContext.Render<PasskeySubmit>(parameters => parameters
            .Add(p => p.Operation, PasskeyOperation.Request)
            .Add(p => p.Name, "Input.Passkey")
            .Add(p => p.EmailName, "Input.Email")
            .Add(p => p.ChildContent, "Log in with a passkey")
            .AddCascadingValue(httpContext));

        cut.Markup.ShouldContain("name=\"__passkeySubmit\"");
        cut.Markup.ShouldContain("operation=\"Request\"");
        cut.Markup.ShouldContain("name=\"Input.Passkey\"");
        cut.Markup.ShouldContain("email-name=\"Input.Email\"");
        cut.Markup.ShouldContain("request-token-name=\"RequestVerificationToken\"");
        cut.Markup.ShouldContain("request-token-value=\"request-token\"");
    }

    // ---- ShowRecoveryCodes ----

    [Fact]
    public void ShowRecoveryCodes_RendersCodesAndStatusMessage()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<ShowRecoveryCodes>(parameters => parameters
            .Add(p => p.RecoveryCodes, new[] { "CODE-ONE", "CODE-TWO" })
            .Add(p => p.StatusMessage, "Recovery codes were generated.")
            .AddCascadingValue(new DefaultHttpContext()));

        cut.Markup.ShouldContain("Recovery codes were generated.");
        cut.Markup.ShouldContain("CODE-ONE");
        cut.Markup.ShouldContain("CODE-TWO");
        cut.Markup.ShouldContain("recovery-code");
    }

    // ---- Form primitives ----

    [Fact]
    public void AccountSubmitButton_RendersPrimaryFullWidth_ByDefault()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<AccountSubmitButton>(parameters => parameters
            .Add(p => p.Text, "Save changes"));

        cut.Markup.ShouldContain("type=\"submit\"");
        cut.Markup.ShouldContain("btn-primary");
        cut.Markup.ShouldContain("w-100");
        cut.Markup.ShouldContain("Save changes");
    }

    [Fact]
    public void AccountSubmitButton_RendersSecondaryInline_WhenRequested()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<AccountSubmitButton>(parameters => parameters
            .Add(p => p.Text, "Cancel")
            .Add(p => p.Block, false)
            .Add(p => p.Variant, AccountSubmitButton.AccountButtonKind.Secondary));

        cut.Markup.ShouldContain("btn-secondary");
        cut.Markup.ShouldNotContain("w-100");
    }

    [Fact]
    public void AccountFormLabel_RendersForAndOptionalHint()
    {
        using var testContext = new BunitContext();

        var cut = testContext.Render<AccountFormLabel>(parameters => parameters
            .Add(p => p.Text, "Email")
            .Add(p => p.For, "Input.Email")
            .Add(p => p.Optional, true));

        cut.Markup.ShouldContain("for=\"Input.Email\"");
        cut.Markup.ShouldContain("Email");
        cut.Markup.ShouldContain("(optional)");
    }

    [Fact]
    public void AccountFormField_RendersLabelControlAndHelpText()
    {
        using var testContext = new BunitContext();

        var model = new TestModel();
        var editContext = new EditContext(model);

        var cut = testContext.Render<AccountFormField<string>>(parameters => parameters
            .Add(p => p.For, () => model.Name)
            .Add(p => p.FieldId, "DisplayName")
            .Add(p => p.Label, "Display name")
            .Add(p => p.HelpText, "Shown on your profile.")
            .AddCascadingValue(editContext)
            .Add(p => p.ChildContent, builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "id", "DisplayName");
                builder.AddAttribute(2, "class", "form-control");
                builder.AddAttribute(3, "value", "");
                builder.CloseElement();
            }));

        cut.Markup.ShouldContain("for=\"DisplayName\"");
        cut.Markup.ShouldContain("Display name");
        cut.Markup.ShouldContain("Shown on your profile.");
        cut.Markup.ShouldContain("id=\"DisplayName\"");
    }

    [Fact]
    public async Task AccountValidationMessage_ShowsError_WhenEditContextFieldIsInvalid()
    {
        using var testContext = new BunitContext();

        var model = new TestModel { Name = string.Empty };
        var editContext = new EditContext(model);

        var cut = testContext.Render<EditForm>(parameters => parameters
            .Add(p => p.EditContext, editContext)
            .Add(p => p.ChildContent, (RenderFragment<EditContext>)(_ => builder =>
            {
                builder.OpenComponent<DataAnnotationsValidator>(1);
                builder.CloseComponent();
                builder.OpenComponent<AccountFormField<string>>(2);
                builder.AddAttribute(3, "For", (Expression<Func<string>>)(() => model.Name));
                builder.AddAttribute(4, "ChildContent", (RenderFragment)(fieldBuilder =>
                {
                    fieldBuilder.OpenComponent<InputText>(5);
                    fieldBuilder.AddAttribute(6, "Value", model.Name);
                    fieldBuilder.AddAttribute(7, "ValueExpression", (Expression<Func<string>>)(() => model.Name));
                    fieldBuilder.CloseComponent();
                }));
                builder.CloseComponent();
            })));

        var validated = await cut.InvokeAsync(() => editContext.Validate());
        validated.ShouldBeFalse();
        cut.Render();

        cut.Markup.ShouldContain("The Name field is required.");
    }

    private sealed class TestModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; set; } = string.Empty;
    }
}
