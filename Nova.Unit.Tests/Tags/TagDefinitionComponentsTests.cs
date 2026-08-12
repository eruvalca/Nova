using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.UI.Features.Tags.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Tags;

/// <summary>
/// Component-level tests for the club-admin tag-definition management panel and the
/// read-only active tag definitions panel, covering render-mode hosting, persisted-state
/// hydration, forbidden navigation, and admin-only mutation flows.
/// </summary>
public sealed class TagDefinitionComponentsTests : BunitContext
{
    private sealed class PersistedStateTagDefinitionManagementPanel(
        ITagDefinitionService tagDefinitionService,
        NavigationManager navigationManager)
        : TagDefinitionManagementPanel(tagDefinitionService, navigationManager)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public IReadOnlyList<TagDefinitionSummary>? PersistedActive { get; set; }

        [Parameter]
        public IReadOnlyList<TagDefinitionSummary>? PersistedArchived { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                Active = PersistedActive ?? [];
                Archived = PersistedArchived ?? [];
            }

            return base.OnInitializedAsync();
        }
    }

    private sealed class PersistedStateActiveTagDefinitionsPanel(ITagDefinitionService tagDefinitionService)
        : ActiveTagDefinitionsPanel(tagDefinitionService)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public IReadOnlyList<TagDefinitionSummary>? PersistedActive { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                Active = PersistedActive ?? [];
            }

            return base.OnInitializedAsync();
        }
    }

    [Fact]
    public void TagDefinitionManagementPanel_DeclaresInteractiveAutoRenderMode_OnClubAdminPage()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Clubs", "Pages", "ClubAdmin.razor");
        File.ReadAllText(razorPath).ShouldContain("TagDefinitionManagementPanel @rendermode=\"InteractiveAuto\"");
    }

    [Fact]
    public void ActiveTagDefinitionsPanel_IsHostedReadOnly_OnClubDetailPage()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Clubs", "Pages", "ClubDetail.razor");
        File.ReadAllText(razorPath).ShouldContain("<ActiveTagDefinitionsPanel />");
    }

    [Fact]
    public void ManagementPanel_OnInitialized_RendersActiveAndArchivedLists()
    {
        var service = CreateTagService(
            active: [Tag(1, "MVP", LifecycleStatus.Active), Tag(2, "Starter", LifecycleStatus.Active)],
            archived: [Tag(3, "Legacy", LifecycleStatus.Archived, new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero))]);
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("MVP"));
        cut.Markup.ShouldContain("Starter");
        cut.Markup.ShouldContain("Legacy");
        cut.Markup.ShouldContain("Active tag definitions");
        cut.Markup.ShouldContain("Archived tag definitions");
    }

    [Fact]
    public void ManagementPanel_OnInitialized_ShowsLoadError_WhenFetchFails()
    {
        var service = Substitute.For<ITagDefinitionService>();
        service.GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(ServiceProblem.ServerError("Tags unavailable."))));
        service.GetArchivedAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(Array.Empty<TagDefinitionSummary>())));
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tags unavailable."));
        cut.Markup.ShouldContain("alert-danger");
    }

    [Fact]
    public void ManagementPanel_OnInitialized_NavigatesToAccessDenied_WhenForbidden()
    {
        var service = Substitute.For<ITagDefinitionService>();
        service.GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(ServiceProblem.Forbidden("Not authorized."))));
        service.GetArchivedAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(ServiceProblem.Forbidden("Not authorized."))));
        RegisterServices(service);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TagDefinitionManagementPanel>();

        cut.WaitForAssertion(() =>
                navigationManager.Uri.ShouldContain("/Account/AccessDenied"),
            timeout: TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ManagementPanel_AddTagDefinitionClick_ShowsCreateForm()
    {
        RegisterServices(CreateTagService());

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add tag definition"));

        cut.Find("button.btn-primary.btn-sm").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("tag-create-name"));
        cut.Markup.ShouldContain("Create");
    }

    [Fact]
    public void ManagementPanel_CreateTagDefinition_OnValidSubmit_CallsServiceAndShowsSuccess()
    {
        var service = CreateTagService();
        service.CreateAsync(Arg.Any<CreateTagDefinitionInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<TagDefinitionMutationSuccess>)new TagDefinitionMutationSuccess { TagDefinitionId = 9 }));
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add tag definition"));
        cut.Find("button.btn-primary.btn-sm").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("tag-create-name"));

        cut.Find("#tag-create-name").Change("U16 Blue");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => service.Received(1).CreateAsync(
            Arg.Is<CreateTagDefinitionInput>(i => i.Name == "U16 Blue"),
            Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag definition \"U16 Blue\" created."));
    }

    [Fact]
    public void ManagementPanel_CreateTagDefinition_OnConflict_ShowsMutationError()
    {
        var service = CreateTagService();
        service.CreateAsync(Arg.Any<CreateTagDefinitionInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<TagDefinitionMutationSuccess>)ServiceProblem.Conflict("A tag definition with that name already exists.")));
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add tag definition"));
        cut.Find("button.btn-primary.btn-sm").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("tag-create-name"));

        cut.Find("#tag-create-name").Change("MVP");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A tag definition with that name already exists."));
    }

    [Fact]
    public void ManagementPanel_EditClick_ShowsEditFormWithSave()
    {
        RegisterServices(CreateTagService(active: [Tag(1, "MVP", LifecycleStatus.Active)]));

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit"));

        cut.Find("button.btn-outline-secondary.btn-sm").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("tag-edit-name-1"));
        cut.Markup.ShouldContain("Save");
    }

    [Fact]
    public void ManagementPanel_Archive_OnConfirm_CallsServiceAndShowsSuccess()
    {
        var service = CreateTagService(active: [Tag(1, "MVP", LifecycleStatus.Active)]);
        service.ArchiveAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<TagDefinitionMutationSuccess>)new TagDefinitionMutationSuccess { TagDefinitionId = 1 }));
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive"));

        cut.Find("button.btn-outline-danger.btn-sm").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Yes, archive"));

        cut.Find("button.btn-danger.btn-sm").Click();

        cut.WaitForAssertion(() => service.Received(1).ArchiveAsync(1, Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag definition archived."));
    }

    [Fact]
    public void ManagementPanel_Restore_OnConfirm_CallsServiceAndShowsSuccess()
    {
        var service = CreateTagService(archived: [Tag(5, "Legacy", LifecycleStatus.Archived, new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero))]);
        service.RestoreAsync(5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<TagDefinitionMutationSuccess>)new TagDefinitionMutationSuccess { TagDefinitionId = 5 }));
        RegisterServices(service);

        var cut = Render<TagDefinitionManagementPanel>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restore"));

        cut.Find("button.btn-outline-success.btn-sm").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Yes, restore"));

        cut.Find("button.btn-success.btn-sm").Click();

        cut.WaitForAssertion(() => service.Received(1).RestoreAsync(5, Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag definition restored."));
    }

    [Fact]
    public void ManagementPanel_DoesNotFetch_WhenPersistedStateInitialized()
    {
        var service = Substitute.For<ITagDefinitionService>();
        RegisterServices(service);

        var cut = Render<PersistedStateTagDefinitionManagementPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedActive, new[] { Tag(1, "MVP", LifecycleStatus.Active) })
            .Add(p => p.PersistedArchived, new[] { Tag(2, "Legacy", LifecycleStatus.Archived, new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero)) }));

        service.DidNotReceive().GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>());
        service.DidNotReceive().GetArchivedAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("MVP"));
        cut.Markup.ShouldContain("Legacy");
        cut.Markup.ShouldContain("Edit");
        cut.Markup.ShouldContain("Archive");
    }

    [Fact]
    public void ActivePanel_OnInitialized_RendersActiveTagBadges()
    {
        var service = CreateTagService(active: [Tag(1, "MVP", LifecycleStatus.Active), Tag(2, "Starter", LifecycleStatus.Active)]);
        RegisterServices(service);

        var cut = Render<ActiveTagDefinitionsPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("MVP"));
        cut.Markup.ShouldContain("Starter");
        cut.Markup.ShouldContain("badge");
        cut.Markup.ShouldNotContain("Edit");
        cut.Markup.ShouldNotContain("Archive");
        cut.Markup.ShouldNotContain("Add tag definition");
    }

    [Fact]
    public void ActivePanel_ShowsEmptyState_WhenNoActiveTags()
    {
        RegisterServices(CreateTagService());

        var cut = Render<ActiveTagDefinitionsPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No active tag definitions."));
    }

    [Fact]
    public void ActivePanel_DegradesToEmptyState_WhenLoadFails()
    {
        var service = Substitute.For<ITagDefinitionService>();
        service.GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(ServiceProblem.ServerError("boom"))));
        RegisterServices(service);

        var cut = Render<ActiveTagDefinitionsPanel>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No active tag definitions."));
        cut.Markup.ShouldNotContain("boom");
    }

    [Fact]
    public void ActivePanel_DoesNotFetch_WhenPersistedStateInitialized()
    {
        var service = Substitute.For<ITagDefinitionService>();
        RegisterServices(service);

        var cut = Render<PersistedStateActiveTagDefinitionsPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedActive, new[] { Tag(1, "MVP", LifecycleStatus.Active) }));

        service.DidNotReceive().GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("MVP"));
    }

    private void RegisterServices(ITagDefinitionService tagDefinitionService)
    {
        Services.AddSingleton(tagDefinitionService);
    }

    private static ITagDefinitionService CreateTagService(
        IReadOnlyList<TagDefinitionSummary>? active = null,
        IReadOnlyList<TagDefinitionSummary>? archived = null)
    {
        var service = Substitute.For<ITagDefinitionService>();
        var activeDefinitions = new List<TagDefinitionSummary>(active ?? []);
        var archivedDefinitions = new List<TagDefinitionSummary>(archived ?? []);
        service.GetActiveAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<TagDefinitionSummary>>)activeDefinitions));
        service.GetArchivedAsync(Arg.Any<GetTagDefinitionsInput?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<TagDefinitionSummary>>)archivedDefinitions));
        return service;
    }

    private static TagDefinitionSummary Tag(long id, string name, LifecycleStatus status, DateTimeOffset? archivedAt = null) => new()
    {
        TagDefinitionId = id,
        Name = name,
        Color = "#4F46E5",
        LifecycleStatus = status,
        CreatedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ArchivedAt = archivedAt,
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Join(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for tag component render-mode assertion.");
    }
}
