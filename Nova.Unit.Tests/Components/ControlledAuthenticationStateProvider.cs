using Microsoft.AspNetCore.Components.Authorization;

namespace Nova.Unit.Tests.Components;

/// <summary>Publishes controllable authentication tasks so tests can independently order notifications and completions.</summary>
/// <param name="initial">The startup authentication result, optionally held pending by the test.</param>
internal sealed class ControlledAuthenticationStateProvider(Task<AuthenticationState> initial) : AuthenticationStateProvider
{
    /// <summary>Starts with an immediately resolved principal for ordinary identity-transition scenarios.</summary>
    /// <param name="initial">The initial principal.</param>
    public ControlledAuthenticationStateProvider(System.Security.Claims.ClaimsPrincipal initial) : this(Task.FromResult(new AuthenticationState(initial)))
    {
    }

    /// <summary>The latest task published by the provider, also returned to subsequent authentication readers.</summary>
    private Task<AuthenticationState> _current = initial;

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _current;

    /// <summary>Publishes a notification without controlling when its result completes.</summary>
    /// <param name="state">The pending or completed authentication state supplied by the test.</param>
    public void Publish(Task<AuthenticationState> state)
    {
        _current = state;
        NotifyAuthenticationStateChanged(state);
    }

    /// <summary>Publishes an immediately resolved principal while retaining the same task-ordering behavior.</summary>
    /// <param name="principal">The replacement principal.</param>
    public void Publish(System.Security.Claims.ClaimsPrincipal principal) => Publish(Task.FromResult(new AuthenticationState(principal)));
}
