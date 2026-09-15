using System.Text.Json;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignPlaceBrowserTests
{
    [Fact]
    public async Task RetainedPlacementStorageRejectsMalformedCommandsAndPreservesEvidenceAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var input = new UpdateCampaignPlacementInput(301, PlacementOutcome.Assigned, 90,
            Guid.Parse("abcdef01-2345-4678-9abc-def012345678"), Guid.CreateVersion7());

        var errors = await context.Pages[0].EvaluateAsync<string[]>(StorageContractProbe, new
        {
            scope = "validation:" + Guid.NewGuid().ToString("N"),
            payload = JsonSerializer.Serialize(input, JsonSerializerOptions.Web)
        });

        errors.ShouldBeEmpty();
    }

    private const string StorageContractProbe = """
        async ({scope, payload}) => {
            const module = await import('/_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js');
            const key = 'nova:placement:' + scope;
            const valid = JSON.parse(payload);
            const errors = [];
            const invalid = [null, [], {},
                {...valid, operationId: 'not-a-guid'},
                {...valid, operationId: '11111111-2222-4333-8444-555555555555'},
                {...valid, operationId: '11111111-2222-7333-4444-555555555555'},
                {...valid, operationId: 7},
                {...valid, operationId: 'ffffffff-ffff-7fff-bfff-ffffffffffff'},
                {...valid, expectedConcurrencyToken: 'invalid'},
                {...valid, expectedConcurrencyToken: true},
                {...valid, expectedConcurrencyToken: '00000000-0000-0000-0000-000000000000'},
                {...valid, playerCampaignAssignmentId: 0}, {...valid, playerCampaignAssignmentId: -1},
                {...valid, playerCampaignAssignmentId: 1.5}, {...valid, playerCampaignAssignmentId: '301'},
                {...valid, playerCampaignAssignmentId: Number.MAX_SAFE_INTEGER + 1},
                {...valid, outcome: 0}, {...valid, outcome: 4}, {...valid, outcome: 'Assigned'},
                {...valid, teamId: null}, {...valid, teamId: 0}, {...valid, teamId: -1},
                {...valid, teamId: 1.5}, {...valid, teamId: '90'},
                {...valid, teamId: Number.MAX_SAFE_INTEGER + 1},
                {...valid, outcome: 2}, {...valid, outcome: 3},
                {...valid, outcome: 2, teamId: undefined}];
            try {
                for (const [index, value] of invalid.entries()) {
                    const raw = JSON.stringify(value);
                    sessionStorage.setItem(key, raw);
                    try { module.readPending(scope); errors.push('read accepted invalid case ' + index); } catch {}
                    if (sessionStorage.getItem(key) !== raw) errors.push('read changed evidence ' + index);
                    if (module.readRecovery(scope).invalidValue !== raw) errors.push('invalid diagnosis lost original bytes ' + index);
                    if (module.discardInvalidPending(scope, raw + 'changed')) errors.push('discard accepted different bytes ' + index);
                    if (!module.discardInvalidPending(scope, raw)) errors.push('explicit discard failed ' + index);
                    sessionStorage.removeItem(key);
                    try { module.writePending(scope, value); errors.push('write accepted invalid case ' + index); } catch {}
                    if (sessionStorage.getItem(key) !== null) errors.push('write retained invalid case ' + index);
                }
                sessionStorage.setItem(key, '{');
                try { module.readPending(scope); errors.push('read accepted malformed JSON'); } catch {}
                if (sessionStorage.getItem(key) !== '{') errors.push('malformed JSON evidence removed');
                const originalNow = Date.now;
                const now = originalNow();
                const at = offset => {
                    const time = (now + offset).toString(16).padStart(12, '0');
                    return time.slice(0, 8) + '-' + time.slice(8) + '-7aaa-8bbb-ccccddddffff';
                };
                try {
                    Date.now = () => now;
                    sessionStorage.removeItem(key);
                    const boundary = {...valid, operationId: at(60_000)};
                    module.writePending(scope, boundary);
                    if (module.readPending(scope).operationId !== boundary.operationId) errors.push('inclusive future allowance rejected');
                    sessionStorage.removeItem(key);
                    const future = {...valid, operationId: at(60_001)};
                    try { module.writePending(scope, future); errors.push('future command dispatched'); } catch {}
                    if (sessionStorage.getItem(key) !== null) errors.push('future command persisted');
                    const raw = JSON.stringify(future);
                    sessionStorage.setItem(key, raw);
                    if (module.readRecovery(scope).invalidValue !== raw) errors.push('future evidence not retained as invalid');
                    Date.now = () => now + 60_001;
                    if (module.discardInvalidPending(scope, raw)) errors.push('discard removed an operation that became valid');
                    if (sessionStorage.getItem(key) !== raw) errors.push('clock advance erased pending evidence');
                } finally { Date.now = originalNow; sessionStorage.removeItem(key); }
                for (const input of [valid, {...valid, outcome: 2, teamId: null}, {...valid, outcome: 3, teamId: null}]) {
                    // A C# deserialize/serialize round trip normalizes GUID casing and property order.
                    const reordered = Object.fromEntries(Object.entries(input).reverse());
                    reordered.operationId = input.operationId.toUpperCase();
                    reordered.expectedConcurrencyToken = input.expectedConcurrencyToken.toUpperCase();
                    sessionStorage.setItem(key, JSON.stringify(reordered));
                    module.writePending(scope, input);
                    if (JSON.stringify(module.readPending(scope)) !== JSON.stringify(input)) errors.push('equivalent replay changed command');
                    module.writePending(scope, reordered);
                    const retained = sessionStorage.getItem(key);
                    const changed = [
                        {...input, operationId: '00000000-0000-7333-8444-555555555555'},
                        {...input, expectedConcurrencyToken: '11111111-2222-4333-8444-555555555555'},
                        {...input, playerCampaignAssignmentId: input.playerCampaignAssignmentId + 1},
                        input.outcome === 1 ? {...input, teamId: input.teamId + 1} : {...input, outcome: input.outcome === 2 ? 3 : 2},
                        input.outcome === 1 ? {...input, outcome: 2, teamId: null} : {...input, outcome: 1, teamId: 90}];
                    for (const [index, change] of changed.entries()) {
                        try { module.writePending(scope, change); errors.push('replaced different command ' + input.outcome + ':' + index); } catch {}
                        if (sessionStorage.getItem(key) !== retained) errors.push('rejected replacement changed evidence');
                    }
                    module.clearPending(scope, input.operationId);
                    if (module.readPending(scope) !== null) errors.push('case-normalized settlement retained operation');
                }
                for (const value of [valid, {...valid, outcome: 2, teamId: null}, {...valid, outcome: 3, teamId: null},
                    {...valid, operationId: valid.operationId.toUpperCase()},
                    {...valid, operationId: '00000000-0000-7000-8000-000000000001'}]) {
                    sessionStorage.removeItem(key);
                    module.writePending(scope, value);
                    if (module.discardInvalidPending(scope, JSON.stringify(value))) errors.push('discard removed valid operation');
                    if (JSON.stringify(module.readPending(scope)) !== JSON.stringify(value)) errors.push('valid payload changed');
                    module.clearPending(scope, value.operationId);
                    if (module.readPending(scope) !== null) errors.push('settled operation retained');
                }
                return errors;
            } finally { sessionStorage.removeItem(key); }
        }
        """;
}
