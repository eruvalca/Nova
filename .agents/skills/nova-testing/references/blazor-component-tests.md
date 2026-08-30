# Blazor component tests (bUnit)

Component tests live in `Nova.Unit.Tests` and use **bUnit** with **NSubstitute** and **Shouldly**.
See `Nova.Unit.Tests\Components\*.cs`, `Nova.Unit.Tests\Clubs\ClubComponentsTests.cs`, and
`Nova.Unit.Tests\Players\PlayerComponentsTests.cs`.

## Rendering with substituted services

Register every service the component injects into the bUnit context before rendering; the component
resolves them from DI exactly as it does at runtime.

```csharp
var service = Substitute.For<IClubMemberService>();
var navigationManager = Substitute.For<NavigationManager>();

using var testContext = new BunitContext();
testContext.Services.AddScoped(_ => service);
testContext.Services.AddScoped(_ => navigationManager);

var cut = testContext.Render<AssignClubAdminPanel>();

cut.Markup.ShouldContain("Persisted Member");
```

Do not pass `null!` for a required dependency — supply a `Substitute.For<T>()` or a lightweight real
implementation.

## Asserting an `EventCallback` fired

Create the callback with `EventCallback.Factory` and assert the flag after triggering the DOM event:

```csharp
var callbackInvoked = false;

var cut = Render<PendingJoinRequestCard>(parameters =>
{
    parameters.Add(p => p.Request, rejectedRequest);
    parameters.Add(p => p.OnSearchAgainRequested, EventCallback.Factory.Create(this, async () =>
    {
        callbackInvoked = true;
        await Task.CompletedTask;
    }));
});

cut.Find("button.btn-primary").Click();

callbackInvoked.ShouldBeTrue();
```

## Asserting a server error reaches a child string parameter

When a parent passes server feedback to a child `string` parameter, configure the substituted
service to return recognizable text, submit through the rendered UI, and assert both sides:

```csharp
cut.Markup.ShouldContain("A team with that name and graduation year already exists.");
cut.Markup.ShouldNotContain("_formError");
```

The negative assertion catches `ErrorMessage="_formError"`, which compiles but passes literal text
instead of the backing-field value. Use
`TeamComponentsTests.Teams_ShowsServerErrorText_WhenUpdateReturnsConflict` as the canonical example.

## Render-mode assertion (required for interactive pages)

**bUnit invokes callbacks regardless of the deployed render mode.** A green callback test therefore
does *not* prove the button works in the app — a page missing `@rendermode` renders correct markup,
passes every component test, and does nothing in the browser.

`@rendermode X` compiles to a compiler-generated attribute deriving from `RenderModeAttribute`, so
assert it by reflection over the page type:

```csharp
[Fact]
public void PlayersPage_DeclaresInteractiveAutoRenderMode()
{
    var attribute = typeof(Players)
        .GetCustomAttributes(inherit: false)
        .OfType<RenderModeAttribute>()
        .SingleOrDefault();

    attribute.ShouldNotBeNull();
    attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
}
```

`GetCustomAttributes` returns the attribute for a page declaring `@rendermode InteractiveAuto` and
nothing for a static SSR page such as `ClubDetail`, so the same shape asserts either intent.

Add this whenever a page or component gains its first event handler. For flows where interactivity
must be proven end to end (auth/claims propagation, role-gated controls), add a
[browser suite](browser-suite.md) scenario, or use the one-off
[Aspire + Playwright validation](../../aspire-playwright-validation/SKILL.md) pass.

## Testing prerender/persisted-state behavior

`[PersistentState]` values are not restored by bUnit. To exercise the restore path, derive a test-only
subclass that seeds the persisted properties before calling the base initializer, then assert the
service was never called:

```csharp
private sealed class PersistedStateAssignClubAdminPanel(
    IClubMemberService clubMemberService,
    NavigationManager navigationManager)
    : AssignClubAdminPanel(clubMemberService, navigationManager)
{
    [Parameter]
    public bool StartInitialized { get; set; }

    [Parameter]
    public IReadOnlyList<ClubMemberDto>? PersistedMembers { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (StartInitialized)
        {
            Initialized = true;
            Members = PersistedMembers ?? [];
        }

        return base.OnInitializedAsync();
    }
}
```

```csharp
service.DidNotReceive().GetClubMembersAsync(Arg.Any<CancellationToken>());
```

## Conventions

- Name tests `Subject_Outcome_Condition`.
- Assert on rendered markup (`cut.Markup`, `cut.Find(...)`) and on substituted-service interactions —
  not on private component fields.
- Build culture-sensitive expected strings (dates, numbers) with the same culture the component uses;
  do not hard-code an English rendering unless the product contract fixes that culture.
- Keep component tests in `Nova.Unit.Tests`; they need no database harness.
