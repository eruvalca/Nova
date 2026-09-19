using Bunit;
using Nova.UI.Features.Players.Components;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Component-level tests for the shared archive confirmation's subject-scoped acknowledgement.
/// </summary>
public sealed class PlayerLifecycleConfirmationTests : BunitContext
{
    /// <summary>An acknowledgement given for one player never authorizes a different player's archive.</summary>
    [Fact]
    public void PlayerLifecycleConfirmationRequiresAFreshAcknowledgementForAnotherPlayer()
    {
        var cut = Render<PlayerLifecycleConfirmation>(parameters => parameters
            .Add(component => component.PlayerId, 7L)
            .Add(component => component.DisplayName, "Avery Johnson"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("#archive-commit").HasAttribute("disabled").ShouldBeFalse();

        // The directory reuses this instance when the archive subject changes.
        cut.Render(parameters => parameters
            .Add(component => component.PlayerId, 8L)
            .Add(component => component.DisplayName, "Jordan Lane"));

        cut.Find("#archive-confirm-checkbox").HasAttribute("checked").ShouldBeFalse();
        cut.Find("#archive-commit").HasAttribute("disabled").ShouldBeTrue();
    }
}
