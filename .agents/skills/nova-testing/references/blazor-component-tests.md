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
`TeamComponentsTests.TeamsShowsServerErrorTextWhenUpdateReturnsConflict` as the canonical example.

## Transition coverage

Select the rows affected by the changed behavior; do not add unrelated cases to every edit. Follow
the implementation rules in the Blazor lifecycle/form references rather than copying an entire
page. Test through the boundary the claim depends on: a rendered form for resubmission, real HTTP
for serialization, and the browser for composed DOM/focus. Directly calling a submit callback or
seeing the first validation error does not prove a corrected retry succeeds.

| Changed behavior | Required evidence when applicable |
| --- | --- |
| Server validation | Submit through the form → contextual error → edit → unchanged parent rerender → successful second submission with corrected payload. |
| Identity/permissions | Initial identity, same-role club change, role-only change, and the first clubless notification. Clear old rows, derived state, panels, confirmation, and feedback before replacement work finishes. |
| Async ownership | Complete an old success, failure, and cleanup after newer work or disposal. They cannot publish data/feedback, navigate, or clear the newer operation's busy state. |
| Recoverable mutations | Failed storage on every dispatch path prevents mutation; uncertain result → retained ID/payload → reload/replay; partial cleanup cannot conceal committed effects or authorize stale context. |
| URL-backed state | Rendered state and query agree after reset, permission change, reload/history, and return navigation. |
| HTTP contracts | Producer guarantees, required JSON fields, nested relationships/bounds, client validation, and rendered consequences agree; use the [contract check](../../add-feature-slice/references/wasm-client.md#producer-to-ui-contract-check). |
| Composed UI | Semantics, focus, and the design system's applicable touch targets hold in the actual browser DOM, including nested forms and responsive tables. |

Examples prove specific invariants, not complete pages:

- **Validation store lifetime:** `CampaignCreateForm.razor.cs` with
  `CampaignComponentsTests.CampaignCreateFormResubmitsCorrectedFieldWithUnchangedParentErrorSnapshot`;
  its sibling `CampaignMetadataForm.razor.cs` with
  `CampaignEntryTests.CampaignEntryResubmitsMetadataAfterCorrectingServerValidation`.
- **Identity and late ownership:** `Players.razor.cs` with
  `PlayerComponentsTests.PlayersAppliesEmptyIdentityWhenItOvertakesStartupAsync` and
  `PlayersIgnoresPreviousClubArchiveCompletionAsync`; `Teams.razor.cs` with
  `TeamComponentsTests.TeamsReenablesMutationControlsWhenClubChangesDuringInFlightMutation`.
- **Recovery gates and partial cleanup:** `CampaignEntry.razor.cs` with
  `CampaignEntryTests.CampaignEntryRetriesStorageBeforeOpeningWithTheSameOperation` and
  `CampaignEntryConcealsDraftWhenRecoveryLosesAccessAndStorageRemovalFails`;
  `NewCampaign.razor.cs` with `NewCampaignRecoveryTests.NewCampaignRetainsPendingRequestWhenSuccessfulFormCleanupFails`.
- **URL/permissions and browser history:** `Campaigns.razor.cs` with
  `CampaignComponentsTests.CampaignsNormalizesDraftViewWhenAdministratorRoleIsRemovedAsync`;
  `CampaignWorkspace.razor.cs` with
  `CampaignEvaluationBrowserTests.UrlStateSurvivesReloadAndBackForwardRestoresDrawer`.
- **Composed semantics, touch, and opening focus:** the Draft journey's pages and child forms with
  `CampaignDraftBrowserTests.DraftOpensIntoRosterAfterCreationAndCorrectionRoundTrips`.

Use controlled `TaskCompletionSource` instances for ordering rather than timing-based sleeps;
observe the cleared/loading state before releasing replacement work. Reproduce the failing behavior
before applying a defect fix when practical. Record any missing boundary evidence explicitly.

For query-backed component tests:

- Supply `[SupplyParameterFromQuery]` values through the test `NavigationManager`, not
  `parameters.Add(...)`; the installed bUnit rejects direct query-parameter assignment.

## Render-mode assertion (required for interactive pages)

**bUnit invokes callbacks regardless of the deployed render mode.** A green callback test therefore
does *not* prove the button works in the app. Verify the effective mode through the actual host and
call sites, including inherited or per-instance modes. A missing local `@rendermode` does not by
itself mean that a child component is static SSR.

When a page or component owns its render-mode declaration, `@rendermode X` compiles to a
compiler-generated attribute deriving from `RenderModeAttribute`. Assert that local declaration
by reflection over its type, as with `Players`:

```csharp
[Fact]
public void PlayersPageDeclaresInteractiveAutoRenderMode()
{
    var attribute = typeof(Players)
        .GetCustomAttributes(inherit: false)
        .OfType<RenderModeAttribute>()
        .SingleOrDefault();

    attribute.ShouldNotBeNull();
    attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
}
```

`GetCustomAttributes` verifies only the local declaration. Its absence on `ClubDetail` is consistent
with that page's intended static SSR behavior; it does not prove the effective mode of a component
whose host or call site supplies interactivity. For such children, inspect and verify that composition
instead of requiring an attribute on the child.

When adding the first browser event handler, revisit the
[render-mode decision](../../add-blazor-ui/references/render-mode-decision.md) and verify the mode
at its owner. For flows where interactivity must be proven end to end (auth/claims propagation,
role-gated controls), add a
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

## Testing independent regions

For a page whose regions load and recover independently, inspect `ClubOverview.razor.cs` alongside
`ClubOverviewComponentTests.RenderPreservesEverySuccessfulRegionWhenAnyCombinationFails` and
`RetryIdentityReloadsOnlyIdentityAndPreservesSuccessfulRegions`. Cover every meaningful failure combination,
assert that successful regions remain visible, prove a regional retry calls only its own service,
and seed persisted state to prove interactive attach performs no duplicate startup requests. If the
loader catches transport cancellation, also protect the distinction between component-token
cancellation and a recoverable transport failure.

## Testing authentication changes

Use the identity and ownership cases in the transition matrix. Seed a persisted error with its
original club id as well as a successful snapshot; a null payload still has tenant ownership.
`ClubOverviewComponentTests.RenderInvalidatesPersistedStateWhenClubMembershipChanges` is the
scoped example. Browser focus and DOM replacement behavior belongs in the
[browser suite](browser-suite.md), not a bUnit JS mock.

## Conventions

- Name tests `SubjectOutcomeCondition` (append `Async` for async methods).
- Assert on rendered markup (`cut.Markup`, `cut.Find(...)`) and on substituted-service interactions —
  not on private component fields.
- Build culture-sensitive expected strings (dates, numbers) with the same culture the component uses;
  do not hard-code an English rendering unless the product contract fixes that culture.
- Keep component tests in `Nova.Unit.Tests`; they need no database harness.
