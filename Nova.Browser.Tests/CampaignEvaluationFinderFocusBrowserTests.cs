using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignEvaluationCaptureBrowserTests
{
    [Fact]
    public async Task FinderRestorationPreservesDeliberateResultLinkFocusAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, search: "60");
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
