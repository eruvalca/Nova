using Microsoft.AspNetCore.Components.Forms;
using Nova.UI.Features.Campaigns.Components;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Verifies contextual validation snapshots and subscriptions remain owned by the current form.</summary>
public sealed class ServerValidationMessagesTests
{
    /// <summary>Verifies correction clears contextual failures without erasing another validator's messages.</summary>
    [Fact]
    public void ServerMessages_ClearAfterEdit_WithoutReinstallingTheSameSnapshot()
    {
        var context = new EditContext(new object());
        var annotations = new ValidationMessageStore(context);
        annotations.Add(context.Field("Name"), "Local validation remains.");
        using var messages = new ServerValidationMessages();
        messages.Attach(context);
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Name was rejected."],
            ["StartDate"] = ["The submitted dates conflict."]
        };
        messages.Apply(errors);
        context.GetValidationMessages().Count().ShouldBe(3);

        context.NotifyFieldChanged(context.Field("Name"));
        messages.Apply(errors);

        context.GetValidationMessages().ShouldBe(["Local validation remains."]);
    }

    /// <summary>Verifies a new snapshot is authoritative even when its text matches a cleared earlier failure.</summary>
    [Fact]
    public void ServerMessages_InstallNewSnapshot_AndClearWhenParentRemovesErrors()
    {
        var context = new EditContext(new object());
        using var messages = new ServerValidationMessages();
        messages.Attach(context);
        messages.Apply(new Dictionary<string, string[]> { ["Name"] = ["Rejected."] });
        context.NotifyFieldChanged(context.Field("Name"));

        messages.Apply(new Dictionary<string, string[]> { ["Name"] = ["Rejected."] });

        context.GetValidationMessages().ShouldBe(["Rejected."]);
        messages.Apply(null);
        context.GetValidationMessages().ShouldBeEmpty();
    }

    /// <summary>Verifies replacing a model neither revives an old snapshot nor leaves its context subscribed.</summary>
    [Fact]
    public void ServerMessages_RebindContext_WithoutRestoringOldErrorsOrOldSubscriptions()
    {
        var oldContext = new EditContext(new object());
        var currentContext = new EditContext(new object());
        using var messages = new ServerValidationMessages();
        messages.Attach(oldContext);
        var oldErrors = new Dictionary<string, string[]> { ["Name"] = ["Old model rejected."] };
        messages.Apply(oldErrors);

        messages.Attach(currentContext);
        messages.Apply(oldErrors);

        currentContext.GetValidationMessages().ShouldBeEmpty();
        messages.Apply(new Dictionary<string, string[]> { ["Name"] = ["Current model rejected."] });
        oldContext.NotifyFieldChanged(oldContext.Field("Name"));
        currentContext.GetValidationMessages().ShouldBe(["Current model rejected."]);
        currentContext.NotifyFieldChanged(currentContext.Field("Name"));
        currentContext.GetValidationMessages().ShouldBeEmpty();
    }

    /// <summary>Verifies disposal prevents further field notifications from mutating a detached context.</summary>
    [Fact]
    public void ServerMessages_StopRespondingToEdits_AfterDisposal()
    {
        var context = new EditContext(new object());
        var messages = new ServerValidationMessages();
        messages.Attach(context);
        messages.Apply(new Dictionary<string, string[]> { ["Name"] = ["Rejected."] });
        var notifications = 0;
        context.OnValidationStateChanged += (_, _) => notifications++;

        messages.Dispose();
        context.NotifyFieldChanged(context.Field("Name"));

        notifications.ShouldBe(0);
        context.GetValidationMessages().ShouldBe(["Rejected."]);
    }
}
