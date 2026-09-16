using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignCloseBrowserTests
{
    [Fact]
    public async Task NativeCloseFiltersPreserveWorkspaceContextAndResetPagingWithoutScriptsAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await PrepareReviewAsync(seed);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 390, Height = 844 }, javaScriptEnabled: false);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&closePage=2&search=Roster%20%26%20context&placementSearch=Place&evalSearch=Evaluate").ToString());
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(10);
        // SelectOption can change an off-screen control; bring the form above the fixed phone navigation first.
        await page.Locator("#close-blocker").ScrollIntoViewIfNeededAsync();
        await page.Locator("#close-blocker").SelectOptionAsync("outcomes");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(4);
        await Expect(page.Locator("#close-blocker")).ToHaveValueAsync("outcomes");
        page.Url.ShouldNotContain("closePage=");
        await page.Locator("#close-blocker").ScrollIntoViewIfNeededAsync();
        await page.Locator("#close-search").FillAsync("Maya");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("No participants match this review.");
        await Expect(page.Locator("#close-blocker")).ToHaveValueAsync("outcomes");
        await Expect(page.Locator("#close-search")).ToHaveValueAsync("Maya");
        await page.Locator("#close-blocker").ScrollIntoViewIfNeededAsync();
        await page.Locator("#close-blocker").SelectOptionAsync("");
        var apply = page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true });
        await AssertPhoneTargetAsync(apply);
        await AssertPhoneTargetAsync(page.Locator("#close-blocker"));
        await apply.FocusAsync();
        await Expect(apply).ToBeFocusedAsync();
        await apply.PressAsync("Enter");
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(1);
        await Expect(page.Locator(".close-roster tbody a")).ToHaveTextAsync("Maya Patel");
        await Expect(page.Locator("#close-search")).ToHaveValueAsync("Maya");
        var query = Uri.UnescapeDataString(new Uri(page.Url).Query.Replace('+', ' '));
        query.ShouldContain("search=Roster & context");
        query.ShouldContain("placementSearch=Place");
        query.ShouldContain("evalSearch=Evaluate");
        query.ShouldContain("closeSearch=Maya");
        await CaptureAsync(page, "phone-native-filters");
        await page.GoBackAsync();
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("No participants match this review.");
        await Expect(page.Locator("#close-blocker")).ToHaveValueAsync("outcomes");
    }
}
