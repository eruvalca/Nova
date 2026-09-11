using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Exercises the shipped guard with controlled Navigation API event and promise ordering.</summary>
/// <param name="fixture">The shared app and Chromium fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class EvaluationNavigationGuardBrowserTests(BrowserSuiteFixture fixture)
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("commit-first")]
    [InlineData("popstate-first")]
    [InlineData("input")]
    [InlineData("input-trait")]
    [InlineData("pending")]
    [InlineData("input-after-accept")]
    [InlineData("input-after-commit")]
    [InlineData("reject")]
    [InlineData("throw")]
    [InlineData("detach")]
    [InlineData("replace")]
    [InlineData("duplicate")]
    [InlineData("current")]
    [InlineData("finished-rejects")]
    [InlineData("cancel-release")]
    public async Task HistoryReplayRequiresOwnedUnrevokedAcceptanceAsync(string scenario)
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];
        // A script document has the app's origin without starting the Blazor router. The real module
        // runs against a controlled Navigation API; ordinary native history is covered by capture tests.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/_content/Nova.UI/js/evaluationNavigationGuard.js").ToString());
        (await page.EvaluateAsync<bool>(ReplayScenario, scenario)
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    private const string ReplayScenario = """
        async scenario => {
            const guard = await import('/_content/Nova.UI/js/evaluationNavigationGuard.js');
            const descriptor = Object.getOwnPropertyDescriptor(window, 'navigation');
            const fake = new EventTarget();
            fake.currentEntry = { key: 'origin' };
            Object.defineProperty(window, 'navigation', { configurable: true, value: fake });
            const root = document.createElement('div');
            root.innerHTML = scenario === 'input-trait'
                ? '<input id="evaluation-trait-search" data-evidence-original="" />'
                : '<textarea data-evidence-original=""></textarea>';
            document.body.append(root);
            const input = root.querySelector('textarea,input');
            const calls = [], notices = [];
            const receiver = { invokeMethodAsync: (...args) => { notices.push(args); return Promise.resolve(); } };
            const deferred = () => {
                let resolve, reject;
                const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
                return { promise, resolve, reject };
            };
            const commit = deferred(), finish = deferred();
            const check = (value, message) => { if (!value) throw new Error(`${scenario}: ${message}`); };
            const move = key => {
                fake.currentEntry = { key };
                fake.dispatchEvent(new Event('currententrychange'));
                window.dispatchEvent(new PopStateEvent('popstate'));
            };
            fake.traverseTo = key => {
                calls.push(key);
                if (key === 'origin') {
                    move(key);
                    return { committed: Promise.resolve(), finished: Promise.resolve() };
                }
                if (scenario === 'throw') throw new DOMException('Aborted', 'AbortError');
                return { committed: commit.promise, finished: finish.promise };
            };
            try {
                guard.attachGuard(root, 'owner', 'lease', receiver);
                guard.releaseNavigation(root, 'owner', 'lease');
                if (scenario === 'cancel-release') {
                    input.value = 'New draft';
                    const departure = () => {
                        const event = new Event('beforeunload', { cancelable: true });
                        window.dispatchEvent(event);
                        return event.defaultPrevented;
                    };
                    check(!departure(), 'release must initially allow departure');
                    guard.cancelNavigation(root, 'old-owner', 'lease');
                    check(!departure(), 'foreign cancellation must not change current owner');
                    guard.cancelNavigation(root, 'owner', 'lease');
                    check(departure(), 'abandoned release must protect the new draft');
                    check(!await guard.resumeHistory(root, 'owner', 'lease', 'target'), 'canceled release cannot replay');
                    return true;
                }
                if (scenario === 'current') {
                    check(await guard.resumeHistory(root, 'owner', 'lease', 'origin'), 'current entry must settle');
                    check(calls.length === 0, 'current entry must not traverse');
                    return true;
                }
                const result = guard.resumeHistory(root, 'owner', 'lease', 'target');
                let settled = false;
                void result.then(() => { settled = true; });
                if (scenario === 'duplicate') {
                    check(!await guard.resumeHistory(root, 'owner', 'lease', 'target'), 'duplicate must not steal permit');
                    check(calls.length === 1, 'duplicate must not traverse');
                }
                if (scenario === 'commit-first' || scenario === 'input-after-commit') {
                    commit.resolve();
                    await new Promise(resolve => setTimeout(resolve, 0));
                    check(!settled, 'commit alone is not acceptance');
                }
                if (scenario === 'popstate-first' || scenario === 'input-after-accept') {
                    move('target');
                    await new Promise(resolve => setTimeout(resolve, 0));
                    check(!settled, 'popstate alone is not commitment');
                }
                const revoked = ['input', 'input-trait', 'pending', 'input-after-accept', 'input-after-commit', 'detach', 'replace'].includes(scenario);
                if (scenario.startsWith('input')) {
                    input.value = 'New evidence';
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    const departure = new Event('beforeunload', { cancelable: true });
                    window.dispatchEvent(departure);
                    check(departure.defaultPrevented, 'native input must protect departure before any .NET rerender');
                } else if (scenario === 'pending') guard.markPending(root, true);
                else if (scenario === 'detach') guard.detachGuard(root);
                else if (scenario === 'replace') {
                    guard.attachGuard(root, 'new-owner', 'new-lease', receiver);
                    guard.markPending(root, true);
                } else if (scenario === 'reject') commit.reject(new DOMException('Aborted', 'AbortError'));
                if (revoked || scenario === 'reject' || scenario === 'throw') {
                    check(!await result, 'revocation/abort must settle false without waiting for commit');
                    if (scenario === 'input' || scenario === 'input-trait' || scenario === 'pending' || scenario === 'input-after-commit') {
                        move('target');
                        check(fake.currentEntry.key === 'origin', 'revoked traversal must restore original entry');
                        check(notices.length === 1 && notices[0][4] === 'target', 'restoration must request a new prompt');
                    }
                    if (scenario === 'replace') {
                        check(!await guard.resumeHistory(root, 'new-owner', 'new-lease', 'target'), 'old completion must not release new pending owner');
                    }
                    commit.resolve();
                    return true;
                }
                if (scenario !== 'popstate-first') move('target');
                commit.resolve();
                if (scenario === 'finished-rejects') finish.reject(new DOMException('Enhanced fetch canceled', 'AbortError'));
                check(await result, 'accepted committed traversal must succeed');
                check(notices.length === 0, 'accepted traversal must not prompt');
                return true;
            } finally {
                commit.resolve(); finish.resolve();
                guard.detachGuard(root);
                root.remove();
                if (descriptor) Object.defineProperty(window, 'navigation', descriptor);
                else delete window.navigation;
            }
        }
        """;
}
