using Microsoft.Extensions.DependencyInjection;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players;

/// <summary>Registers the manual intake board's browser boundary for both hosts.</summary>
public static class PlayerIntakeServiceCollectionExtensions
{
#pragma warning disable CA1034 // Nested types should not be visible
    extension(IServiceCollection services)
#pragma warning restore CA1034 // Nested types should not be visible
    {
        /// <summary>
        /// Registers the owner-scoped creation recovery store and the uncommitted-departure guard
        /// implementation used by <see cref="Components.PlayerIntakeBoard"/>.
        /// </summary>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddPlayerIntakeInterop()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddScoped<IPlayerIntakeInterop, PlayerCreationRecoveryStore>();
            return services;
        }
    }
}
