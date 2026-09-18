using Microsoft.Extensions.DependencyInjection;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players;

/// <summary>Registers the manual intake board's browser boundary for both hosts.</summary>
public static class PlayerIntakeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the owner-scoped creation recovery store and the uncommitted-departure guard
    /// implementation used by <see cref="Components.PlayerIntakeBoard"/>.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddPlayerIntakeInterop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IPlayerIntakeInterop, PlayerCreationRecoveryStore>();
        return services;
    }
}
