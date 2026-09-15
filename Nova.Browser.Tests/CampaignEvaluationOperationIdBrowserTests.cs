using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignEvaluationCaptureBrowserTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("01993998-1234-7234-c234-123456789abc", false)]
    [InlineData("01993998-1234-4234-8234-123456789abc", true)]
    public async Task InvalidEvaluationOperationStaysBlockedAndRetainedAsync(string operationId, bool wasm)
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await EvaluationInteractionHelpers.AssertComposerAttachedAsync(page);
        var key = $"nova:evaluation:v1:{seed.EvaluatorUserId}:{seed.ClubId}:{seed.CampaignId}:{seed.AssignmentIds[1]}";
        var retained = JsonSerializer.Serialize(new
        {
            revision = 1,
            draft = "Retained original operation",
            traitSearch = string.Empty,
            editingNoteId = (long?)null,
            editContent = string.Empty,
            editOriginal = string.Empty,
            editVersion = Guid.Empty,
            pending = new { kind = "add", operationId, assignmentId = seed.AssignmentIds[1], subjectId = (long?)null, version = Guid.Empty, text = "Retained original operation" }
        });
        await page.EvaluateAsync("([key, value]) => sessionStorage.setItem(key, value)", new[] { key, retained });
        var mutationRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.Request += (_, request) => { if (request.Method is "POST" or "PUT" or "DELETE" && request.Url.Contains("/api/", StringComparison.Ordinal)) { mutationRequests.Enqueue(request.Url); } };
        if (wasm) { await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => EvaluationInteractionHelpers.AssertRecoveryBlockedAttachedAsync(page)); }
        else { await page.ReloadAsync(); await EvaluationInteractionHelpers.AssertRecoveryBlockedAttachedAsync(page); }
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry storage", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-save")).ToBeDisabledAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry original operation", Exact = true })).ToHaveCountAsync(0);
        (await page.EvaluateAsync<string>("key => sessionStorage.getItem(key)", key)).ShouldBe(retained);
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to campaigns", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Leave and keep recovery data", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        mutationRequests.ShouldBeEmpty();
        (await page.EvaluateAsync<string>("key => sessionStorage.getItem(key)", key)).ShouldBe(retained);
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task EvaluationStorageValidatesOperationUuidVersionAndVariantOnReadAndWriteAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await EvaluationInteractionHelpers.AssertComposerAttachedAsync(page);
        var results = await page.EvaluateAsync<bool[]>("""
            async () => {
                const module = await import('/_content/Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js');
                const root = document.createElement('section');
                document.body.append(root);
                const owner = crypto.randomUUID(), lease = crypto.randomUUID(), key = `nova:evaluation:v1:${owner}`;
                const snapshot = {revision:1, draft:'retained', traitSearch:'', editingNoteId:null,
                    editContent:'', editOriginal:'', editVersion:'00000000-0000-0000-0000-000000000000',
                    pending:{kind:'add', operationId:'00000000-0000-7000-8000-000000000000', assignmentId:1,
                        subjectId:null, version:'00000000-0000-0000-0000-000000000000', text:'retained'}};
                const results = [];
                module.attach(root, owner, lease, owner, owner, {invokeMethodAsync: async () => {}});
                try {
                    for (const variant of ['8','9','a','b','A','B']) {
                        snapshot.pending.operationId = `00000000-0000-7000-${variant}000-000000000000`;
                        results.push(module.write(root, owner, lease, snapshot));
                        results.push(module.read(root, owner, lease).pending.operationId === snapshot.pending.operationId);
                    }
                    const valid = sessionStorage.getItem(key);
                    for (const id of ['00000000-0000-7000-0000-000000000000', '00000000-0000-7000-c000-000000000000',
                        '00000000-0000-4000-8000-000000000000', '00000000-0000-0000-0000-000000000000']) {
                        snapshot.pending.operationId = id;
                        sessionStorage.setItem(key, valid);
                        try { module.write(root, owner, lease, snapshot); results.push(false); } catch { results.push(true); }
                        results.push(sessionStorage.getItem(key) === valid);
                        const malformed = JSON.stringify(snapshot);
                        sessionStorage.setItem(key, malformed);
                        try { module.read(root, owner, lease); results.push(false); } catch { results.push(true); }
                        results.push(sessionStorage.getItem(key) === malformed);
                    }
                    return results;
                } finally { module.detach(root, lease); root.remove(); sessionStorage.removeItem(key); }
            }
            """);
        results.Length.ShouldBe(28);
        results.ShouldAllBe(result => result);
    }
}
