using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignEvaluationCaptureBrowserTests
{
    [Fact]
    public async Task FinderRestorationPreservesNativeTraitSummaryFocusAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 1440, Height = 1000 });
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.ArchivedTagApplicationAssignmentId);
        await EvaluationInteractionHelpers.AssertComposerAttachedAsync(page);
        var summary = page.Locator("summary.evaluation-trait").First;
        await Expect(summary).ToBeVisibleAsync();
        await Expect(page.Locator("#evaluation-search")).ToBeVisibleAsync();

        var preserved = await page.EvaluateAsync<bool>("""
            async () => {
                const module = await import('/_content/Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js');
                const root = document.querySelector('[data-evaluation-workspace]');
                const summary = root.querySelector('summary.evaluation-trait');
                summary.focus();
                module.restoreFinder(root, 'summary-focus-regression', root.querySelector('#evaluation-search'));
                return document.activeElement === summary;
            }
            """);

        preserved.ShouldBeTrue();
        await Expect(summary).ToBeFocusedAsync();
        await summary.PressAsync("Enter");
        await Expect(page.Locator("details.evaluation-trait-details").First).ToHaveAttributeAsync("open", string.Empty);
    }

    [Fact]
    public async Task FinderRestorationPreservesDeliberateResultLinkFocusAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, search: "60");
        // Initial finder focus is applied only after interactive module attachment.
        await InteractionHelpers.ActUntilAsync(page, () => Task.CompletedTask,
            () => page.Locator("#evaluation-search").EvaluateAsync<bool>("input => document.activeElement === input"));
        var result = page.Locator("a[data-eval-result]");
        await Expect(result).ToHaveCountAsync(1);

        var preserved = await page.EvaluateAsync<bool>("""
            async () => {
                const module = await import('/_content/Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js');
                const root = document.querySelector('[data-evaluation-workspace]');
                const input = root.querySelector('#evaluation-search');
                const link = root.querySelector('a[data-eval-result]');
                input.focus();
                input.blur();
                module.restoreFinder(root, 'finder-focus-regression', input);
                const restored = document.activeElement === input;
                link.focus();
                module.restoreFinder(root, 'finder-focus-regression', input);
                return restored && document.activeElement === link;
            }
            """);

        preserved.ShouldBeTrue();
        await Expect(result).ToBeFocusedAsync();
        await result.PressAsync("Enter");
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
    }
}
