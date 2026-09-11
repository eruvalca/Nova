using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Exercises retained capture validation and ordering in the actual browser module.</summary>
/// <param name="fixture">The shared application and Chromium fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class EvaluationCaptureStorageBrowserTests(BrowserSuiteFixture fixture)
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("missing-pending")]
    [InlineData("false-pending")]
    [InlineData("zero-pending")]
    [InlineData("empty-pending")]
    [InlineData("unknown-kind")]
    [InlineData("invalid-version")]
    [InlineData("invalid-subject")]
    [InlineData("missing-trait-search")]
    [InlineData("null-trait-search")]
    [InlineData("invalid-trait-search")]
    public async Task InvalidRetainedCaptureStaysProtectedAndPreservesOriginalBytesAsync(string scenario)
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/_content/Nova.UI/js/evaluationNavigationGuard.js").ToString());
        (await page.EvaluateAsync<bool>(StorageScenario, scenario)
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task FinderReattachmentPreservesPendingStateAndRejectsOlderWritesAsync()
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/_content/Nova.UI/js/evaluationNavigationGuard.js").ToString());
        (await page.EvaluateAsync<bool>(StorageScenario, "reattach")
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    private const string StorageScenario = """
        async scenario => {
            const module = await import('/_content/Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js');
            const root = document.createElement('div');
            document.body.append(root);
            const scope = 'storage-test:1:2';
            const key = `nova:evaluation:v1:${scope}`;
            const receiver = { invokeMethodAsync: () => Promise.resolve() };
            const check = (condition, message) => { if (!condition) throw new Error(`${scenario}: ${message}`); };
            const protectedDeparture = () => {
                const event = new Event('beforeunload', { cancelable: true });
                window.dispatchEvent(event);
                return event.defaultPrevented;
            };
            const pending = { kind: 'add', operationId: '01993eed-653c-7000-8000-000000000001', assignmentId: 2,
                subjectId: null, version: '00000000-0000-0000-0000-000000000000', text: 'Retained observation' };
            const valid = { revision: 2, draft: 'Retained observation', editingNoteId: null, editContent: '',
                editOriginal: '', editVersion: '00000000-0000-0000-0000-000000000000', traitSearch: '', pending };
            try {
                module.attach(root, 'owner', 'lease', 'finder-1', scope, receiver);
                if (scenario === 'reattach') {
                    check(module.write(root, 'owner', 'lease', valid), 'new revision must persist');
                    const original = sessionStorage.getItem(key);
                    module.attach(root, 'owner', 'lease', 'finder-2', scope, receiver);
                    check(protectedDeparture(), 'finder attachment must retain pending protection');
                    check(!module.write(root, 'owner', 'lease', { ...valid, revision: 1, draft: 'Old draft', pending: null }),
                        'older delayed write must be rejected after finder attachment');
                    check(sessionStorage.getItem(key) === original, 'newer exact payload must remain');
                    return true;
                }
                let invalid = structuredClone(valid);
                switch (scenario) {
                    case 'null': invalid = null; break;
                    case 'missing-pending': delete invalid.pending; break;
                    case 'false-pending': invalid.pending = false; break;
                    case 'zero-pending': invalid.pending = 0; break;
                    case 'empty-pending': invalid.pending = ''; break;
                    case 'unknown-kind': invalid.pending.kind = 'unknown'; break;
                    case 'invalid-version': invalid.pending.version = null; break;
                    case 'invalid-subject': invalid.pending.subjectId = 0; break;
                    case 'missing-trait-search': delete invalid.traitSearch; break;
                    case 'null-trait-search': invalid.traitSearch = null; break;
                    case 'invalid-trait-search': invalid.traitSearch = 1; break;
                }
                const original = scenario === 'empty' ? '' : JSON.stringify(invalid);
                sessionStorage.setItem(key, original);
                let rejected = false;
                try { module.read(root, 'owner', 'lease'); } catch { rejected = true; }
                check(rejected, 'invalid retained bytes must reject');
                check(sessionStorage.getItem(key) === original, 'rejection must not erase or rewrite retained bytes');
                check(protectedDeparture(), 'unknown retained outcome must protect departure');
                sessionStorage.setItem(key, JSON.stringify(valid));
                const recovered = module.read(root, 'owner', 'lease');
                check(JSON.stringify(recovered.pending) === JSON.stringify(pending), 'recovery must retain exact original operation');
                check(protectedDeparture(), 'valid pending recovery must remain protected');
                sessionStorage.setItem(key, JSON.stringify({ ...valid, pending: null }));
                check(module.read(root, 'owner', 'lease').pending === null, 'explicit null is a valid settled capture');
                check(!protectedDeparture(), 'settled capture may leave when no editable draft is present');
                return true;
            } finally {
                module.detach(root, 'lease');
                root.remove();
                sessionStorage.removeItem(key);
            }
        }
        """;
}
