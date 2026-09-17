using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class PlayerCreationRecoveryPostgresTests
{
    /// <summary>The real duplicate query narrows by tenant and birth date through the migrated composite index.</summary>
    [Fact]
    public async Task DuplicateLookupUsesTenantBirthDateIndexAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Draft, ct);
        var other = await SeedAsync(CampaignStatus.Draft, ct);
        await SeedLookupRosterAsync(seed, ct);
        await SeedLookupRosterAsync(other, ct);
        ActAs(other);
        (await Service().CreateAsync(Input(other.ClubId), ct)).IsSuccess.ShouldBeTrue();
        ActAs(seed);
        var original = (await Service().CreateAsync(Input(seed.ClubId), ct)).Value;
        await using var db = fixture.CreateAdminContext();
        await db.Database.ExecuteSqlRawAsync("ANALYZE \"Players\"", ct);
        var capture = new DuplicateQueryCapture();
        var result = await Service(capture).CreateAsync(Input(seed.ClubId), ct);
        PlayerCreationProblems.TryGetDuplicate(result.Problem, out var duplicate).ShouldBeTrue();
        duplicate.ShouldNotBeNull().PlayerId.ShouldBe(original.Player.PlayerId);
        capture.CommandText.ShouldNotBeNull();
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Only captured EF-generated SELECT text is explained; all original values remain DbParameters.
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + capture.CommandText;
#pragma warning restore CA2100
        foreach (var captured in capture.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = captured.Name;
            parameter.DbType = captured.Type;
            parameter.Value = captured.Value;
            command.Parameters.Add(parameter);
        }
        using var plan = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull());
        TestContext.Current.TestOutputHelper!.WriteLine(plan.RootElement.ToString());
        var root = plan.RootElement[0].GetProperty("Plan");
        root.GetProperty("Actual Rows").GetDecimal().ShouldBe(1m);
        var index = PlanNodes(root).Single(node => node.TryGetProperty("Index Name", out var name)
            && string.Equals(name.GetString(), "IX_Players_ClubId_DateOfBirth", StringComparison.Ordinal));
        index.GetProperty("Index Cond").GetString().ShouldNotBeNull().ShouldContain("ClubId");
        index.GetProperty("Index Cond").GetString().ShouldNotBeNull().ShouldContain("DateOfBirth");
        index.GetProperty("Actual Rows").GetDecimal().ShouldBe(1m);
        index.TryGetProperty("Filter", out _).ShouldBeFalse();
    }

    private async Task SeedLookupRosterAsync(Seed seed, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        var birthDate = Input(seed.ClubId).DateOfBirth;
        db.Players.AddRange(Enumerable.Range(1, 5000).Select(index => new PlayerEntity
        {
            ClubId = seed.ClubId,
            CreatedById = seed.ActorUserId,
            CreationOperationId = Guid.CreateVersion7(),
            FirstName = "Lookup",
            LastName = "Candidate",
            DateOfBirth = birthDate.AddDays(-1 - (index % 3650)),
            GraduationYear = 2030
        }));
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<JsonElement> PlanNodes(JsonElement node)
    {
        yield return node;
        if (!node.TryGetProperty("Plans", out var children)) { yield break; }
        foreach (var child in children.EnumerateArray())
        {
            foreach (var descendant in PlanNodes(child)) { yield return descendant; }
        }
    }

    private sealed class DuplicateQueryCapture : DbCommandInterceptor
    {
        public string? CommandText { get; private set; }
        public IReadOnlyList<QueryParameter> Parameters { get; private set; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Players\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"DateOfBirth\" =", StringComparison.Ordinal))
            {
                CommandText = command.CommandText;
                Parameters = command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new QueryParameter(parameter.ParameterName, parameter.DbType, parameter.Value!)).ToArray();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed record QueryParameter(string Name, DbType Type, object Value);
}
