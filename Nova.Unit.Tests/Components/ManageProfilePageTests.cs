using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Entities;
using NSubstitute;
using Shouldly;
using ManageProfile = Nova.Components.Account.Pages.Manage.Index;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Verifies the static-SSR manage profile form and its preserved Identity form contract.
/// </summary>
public class ManageProfilePageTests : BunitContext
{
    /// <summary>
    /// Verifies that the profile form renders the established account primitives while keeping
    /// the username read-only and the phone number editable.
    /// </summary>
    [Fact]
    public void Render_ShowsAccountFormPrimitives_WithPreservedProfileFields()
    {
        var user = new NovaUserEntity
        {
            Id = 42,
            FirstName = "Nova",
            LastName = "Member",
            UserName = "member@example.com",
            PhoneNumber = "+1 512 555 0142",
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            IdentityConstants.ApplicationScheme));
        var httpContext = new DefaultHttpContext { User = principal };
        var userManager = CreateUserManager();
        userManager.GetUserAsync(principal).Returns(user);
        userManager.GetUserNameAsync(user).Returns(user.UserName);
        userManager.GetPhoneNumberAsync(user).Returns(user.PhoneNumber);

        Services.AddSingleton(userManager);
        Services.AddSingleton(CreateSignInManager(userManager));
        Services.AddSingleton(serviceProvider => new IdentityRedirectManager(
            serviceProvider.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()));

        var cut = Render<ManageProfile>(parameters => parameters.AddCascadingValue(httpContext));

        cut.Find("h1").TextContent.Trim().ShouldBe("Profile");
        cut.Markup.ShouldContain("manage-profile-form");
        cut.Markup.ShouldContain("account-form-field");

        var username = cut.Find("#username");
        username.GetAttribute("value").ShouldBe(user.UserName);
        username.HasAttribute("disabled").ShouldBeTrue();

        var phoneNumber = cut.FindAll("input").Single(element =>
            element.GetAttribute("id") == "Input.PhoneNumber");
        phoneNumber.GetAttribute("value").ShouldBe(user.PhoneNumber);
        phoneNumber.GetAttribute("autocomplete").ShouldBe("tel");

        var form = cut.Find("form");
        form.GetAttribute("method").ShouldBe("post");
        cut.Find("button[type='submit']").TextContent.Trim().ShouldBe("Save");
    }

    /// <summary>
    /// Creates a substitute user manager for the profile page's Identity queries.
    /// </summary>
    /// <returns>A configured user manager substitute.</returns>
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

    /// <summary>
    /// Creates a substitute sign-in manager required by the profile page.
    /// </summary>
    /// <param name="userManager">The user manager shared with the page.</param>
    /// <returns>A sign-in manager substitute.</returns>
    private static SignInManager<NovaUserEntity> CreateSignInManager(UserManager<NovaUserEntity> userManager) =>
        Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());
}
