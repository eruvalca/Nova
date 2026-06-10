using Microsoft.AspNetCore.Identity;

namespace Nova.Data;

/// <summary>
/// Supplies a minimal application service provider that pins the Identity store schema version
/// to the same value configured at runtime in <c>Nova/Program.cs</c>. Identity reads
/// <see cref="IdentityOptions"/> from the application service provider while building the model,
/// so contexts created outside the host (design-time tooling, test harnesses) must attach this
/// provider via <c>UseApplicationServiceProvider</c> to produce the same model as the running app.
/// </summary>
public static class IdentityStoreServiceProvider
{
    /// <summary>
    /// Gets the shared service provider exposing the pinned <see cref="IdentityOptions"/>.
    /// </summary>
    public static IServiceProvider Instance { get; } = new ServiceCollection()
        .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
        .BuildServiceProvider();
}
