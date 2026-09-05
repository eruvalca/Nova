using Nova.UI.Shared.State;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>Verifies authentication ordering, applied identity preservation, and independently owned UI request lanes.</summary>
public sealed class UiOwnershipTests
{
    /// <summary>Verifies a first empty identity is applied rather than mistaken for uninitialized defaults.</summary>
    [Fact]
    public void IdentityScope_AppliesEmptyIdentity_AndRejectsOvertakenStartup()
    {
        using var scope = new UiIdentityScope();
        var startup = scope.BeginAuthentication();
        var latest = scope.BeginAuthentication();

        scope.TryApply(latest, default, out var changed).ShouldBeTrue();

        changed.ShouldBeTrue();
        scope.HasAppliedIdentity.ShouldBeTrue();
        scope.Capture().IsCurrent.ShouldBeTrue();
        scope.TryApply(startup, new UiIdentitySnapshot("101", "42", true), out changed).ShouldBeFalse();
        changed.ShouldBeFalse();
        scope.Current.ShouldBe(default);
    }

    /// <summary>Verifies pending and unchanged notifications preserve the currently applied identity's work.</summary>
    [Fact]
    public void IdentityScope_PreservesOwnedWork_DuringPendingAndUnchangedAuthentication()
    {
        using var scope = new UiIdentityScope();
        var identity = new UiIdentitySnapshot("101", "42", true);
        scope.TryApply(scope.BeginAuthentication(), identity, out _).ShouldBeTrue();
        var requestOwner = new UiRequestOwner();
        var request = requestOwner.Begin(scope.Capture());

        var notification = scope.BeginAuthentication();

        requestOwner.Owns(request).ShouldBeTrue();
        scope.TryApply(notification, identity, out var changed).ShouldBeTrue();
        changed.ShouldBeFalse();
        requestOwner.Owns(request).ShouldBeTrue();
        scope.StorageKey.ShouldBe("101:42:True");
    }

    /// <summary>Verifies only applying a changed identity invalidates every lane owned by the former scope.</summary>
    /// <param name="userId">The replacement user claim.</param>
    /// <param name="clubId">The replacement club claim.</param>
    /// <param name="canManage">The replacement management authority.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("102", "42", true)]
    [InlineData("101", "43", true)]
    [InlineData("101", "42", false)]
    public void IdentityScope_InvalidatesAllOldLanes_WhenChangedIdentityApplies(string userId, string clubId, bool canManage)
    {
        using var scope = new UiIdentityScope();
        scope.TryApply(scope.BeginAuthentication(), new UiIdentitySnapshot("101", "42", true), out _).ShouldBeTrue();
        var loading = new UiRequestOwner();
        var mutation = new UiRequestOwner();
        var oldLoad = loading.Begin(scope.Capture());
        var oldMutation = mutation.Begin(scope.Capture());

        scope.TryApply(scope.BeginAuthentication(), new UiIdentitySnapshot(userId, clubId, canManage), out var changed).ShouldBeTrue();

        changed.ShouldBeTrue();
        loading.Owns(oldLoad).ShouldBeFalse();
        mutation.Owns(oldMutation).ShouldBeFalse();
        loading.Owns(loading.Begin(scope.Capture())).ShouldBeTrue();
    }

    /// <summary>Verifies replacing or invalidating one lane cannot invalidate another lane's cleanup ownership.</summary>
    [Fact]
    public void RequestOwner_KeepsLanesIndependent_WhileRejectingSupersededAndForeignLeases()
    {
        using var scope = new UiIdentityScope();
        scope.TryApply(scope.BeginAuthentication(), new UiIdentitySnapshot("101", "42", true), out _).ShouldBeTrue();
        var loading = new UiRequestOwner();
        var mutation = new UiRequestOwner();
        var oldLoad = loading.Begin(scope.Capture());
        var activeMutation = mutation.Begin(scope.Capture());

        var newLoad = loading.Begin(scope.Capture());

        loading.Owns(oldLoad).ShouldBeFalse();
        loading.Owns(newLoad).ShouldBeTrue();
        loading.Owns(activeMutation).ShouldBeFalse();
        mutation.Owns(activeMutation).ShouldBeTrue();
        loading.Invalidate();
        loading.Owns(newLoad).ShouldBeFalse();
        mutation.Owns(activeMutation).ShouldBeTrue();
        mutation.Capture(scope.Capture()).ShouldBe(activeMutation);
    }

    /// <summary>Verifies disposal invalidates pending authentication and captured requests, including default leases.</summary>
    [Fact]
    public void IdentityScope_RejectsAuthenticationAndPublication_AfterDisposal()
    {
        var scope = new UiIdentityScope();
        var identity = new UiIdentitySnapshot("101", "42", true);
        scope.TryApply(scope.BeginAuthentication(), identity, out _).ShouldBeTrue();
        var owner = new UiRequestOwner();
        var request = owner.Begin(scope.Capture());
        var authentication = scope.BeginAuthentication();

        scope.Dispose();

        owner.Owns(request).ShouldBeFalse();
        owner.Owns(default).ShouldBeFalse();
        scope.TryApply(authentication, identity, out var changed).ShouldBeFalse();
        changed.ShouldBeFalse();
        scope.Capture().IsCurrent.ShouldBeFalse();
    }
}
