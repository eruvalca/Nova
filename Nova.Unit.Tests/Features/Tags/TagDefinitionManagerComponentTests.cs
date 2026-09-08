using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Tags.Components;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Component-level tests for the tag-definition management island: create, edit, archive, restore, and
/// the interactive render mode declared by its host page.
/// </summary>
public sealed class TagDefinitionManagerComponentTests : BunitContext
{
    [Fact]
    public void CreateSubmitsCreateInputAndShowsConfirmation()
    {
        var managementService = Substitute.For<ITagDefinitionService>();
        var created = CreateActiveTag(id: 3, name: "Forward");
        managementService.CreateAsync(Arg.Any<CreateTagDefinitionInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TagDefinitionDto>(created)));

        RegisterServices(tags: [], managementService: managementService);

        var cut = Render<TagDefinitionManager>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No tags found."));

        cut.Find("button.btn-primary").Click();
        cut.Markup.ShouldContain("New tag");

        cut.Find("input#tag-name").Change("Forward");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Created tag \"Forward\"."));
        _ = managementService.Received(1).CreateAsync(
            Arg.Is<CreateTagDefinitionInput>(input => input.Name == "Forward"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditSubmitsUpdateInputAndShowsConfirmation()
    {
        var original = CreateActiveTag(id: 7, name: "Defensive");
        var managementService = Substitute.For<ITagDefinitionService>();
        var updated = original with { Name = "Pressing" };
        managementService.UpdateAsync(Arg.Any<UpdateTagDefinitionInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TagDefinitionDto>(updated)));

        RegisterServices(tags: [original], managementService: managementService);

        var cut = Render<TagDefinitionManager>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Defensive"));

        var editButton = cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Edit", StringComparison.Ordinal));
        editButton.Click();
        cut.Markup.ShouldContain("Edit tag");

        cut.Find("input#tag-name").Change("Pressing");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Updated tag \"Pressing\"."));
        _ = managementService.Received(1).UpdateAsync(
            Arg.Is<UpdateTagDefinitionInput>(input => input.TagId == 7 && input.Name == "Pressing"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ArchiveConfirmsThenCallsLifecycleService()
    {
        var tag = CreateActiveTag(id: 9, name: "Sweeper");
        var lifecycleService = Substitute.For<ITagDefinitionLifecycleService>();
        lifecycleService.ArchiveAsync(9, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(tags: [tag], lifecycleService: lifecycleService);

        var cut = Render<TagDefinitionManager>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Sweeper"));

        var archiveButton = cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Archive", StringComparison.Ordinal));
        archiveButton.Click();
        cut.Markup.ShouldContain("Archive \"Sweeper\"?");

        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archived tag \"Sweeper\"."));
        _ = lifecycleService.Received(1).ArchiveAsync(9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RestoreConfirmsThenCallsLifecycleService()
    {
        var tag = CreateArchivedTag(id: 11, name: "Legacy");
        var lifecycleService = Substitute.For<ITagDefinitionLifecycleService>();
        lifecycleService.RestoreAsync(11, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(tags: [tag], lifecycleService: lifecycleService);

        var cut = Render<TagDefinitionManager>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Legacy"));

        var restoreButton = cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Restore", StringComparison.Ordinal));
        restoreButton.Click();
        cut.Markup.ShouldContain("Restore \"Legacy\"?");

        cut.Find("button.btn-success").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restored tag \"Legacy\"."));
        _ = lifecycleService.Received(1).RestoreAsync(11, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClubAdminRouteDeclaresInteractiveAutoRenderMode()
    {
        var repoRoot = FindRepoRoot();
        var razorPath = Path.Combine(repoRoot, "Nova.UI", "Features", "Clubs", "Pages", "ClubAdmin.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode=\"InteractiveAuto\"");
    }

    [Fact]
    public void LoadFailureShowsErrorButNotTheEmptyState()
    {
        var queryService = Substitute.For<ITagDefinitionQueryService>();
        queryService.GetManagementListAsync(Arg.Any<GetTagDefinitionsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TagDefinitionListResult>(
                ServiceProblem.ServerError("Failed to load tag definitions."))));

        RegisterServices(queryService: queryService);

        var cut = Render<TagDefinitionManager>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Failed to load tag definitions."));
        cut.Markup.ShouldNotContain("No tags found.");
    }

    [Fact]
    public void ListDoesNotShowTruncationNoticeWhenExactlyAtTheCap()
    {
        var tags = Enumerable.Range(1, TagDefinitionLimits.MaxTagDefinitions)
            .Select(i => CreateActiveTag(id: i, name: $"Tag{i}"))
            .ToList();

        RegisterServices(tags: tags, hasMore: false);

        var cut = Render<TagDefinitionManager>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag1"));
        cut.Markup.ShouldNotContain("Showing the first");
    }

    [Fact]
    public void ListShowsTruncationNoticeWhenMoreRowsExistBeyondTheCap()
    {
        var tags = Enumerable.Range(1, TagDefinitionLimits.MaxTagDefinitions)
            .Select(i => CreateActiveTag(id: i, name: $"Tag{i}"))
            .ToList();

        RegisterServices(tags: tags, hasMore: true);

        var cut = Render<TagDefinitionManager>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Showing the first"));
    }

    private void RegisterServices(
        IReadOnlyList<TagDefinitionDto>? tags = null,
        bool hasMore = false,
        ITagDefinitionQueryService? queryService = null,
        ITagDefinitionService? managementService = null,
        ITagDefinitionLifecycleService? lifecycleService = null)
    {
        tags ??= [CreateActiveTag()];

        if (queryService is null)
        {
            queryService = Substitute.For<ITagDefinitionQueryService>();
            queryService.GetManagementListAsync(Arg.Any<GetTagDefinitionsInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<TagDefinitionListResult>(
                    new TagDefinitionListResult { Items = tags.ToList().AsReadOnly(), HasMore = hasMore })));
        }

        managementService ??= Substitute.For<ITagDefinitionService>();
        lifecycleService ??= Substitute.For<ITagDefinitionLifecycleService>();

        Services.AddSingleton(queryService);
        Services.AddSingleton(managementService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton(Substitute.For<NavigationManager>());
    }

    private static TagDefinitionDto CreateActiveTag(long id = 1, string name = "Forward")
        => new() { PlayerTagId = id, Name = name, Color = "#0D6EFD", LifecycleStatus = LifecycleStatus.Active };

    private static TagDefinitionDto CreateArchivedTag(long id = 1, string name = "Legacy")
        => new() { PlayerTagId = id, Name = name, Color = "#6C757D", LifecycleStatus = LifecycleStatus.Archived };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for ClubAdmin route assertion.");
    }
}
