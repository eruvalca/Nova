using Nova.Entities;
using Nova.Shared.Features.Tags;

namespace Nova.Extensions.Tags;

/// <summary>
/// Provides mapping extension members for <see cref="PlayerTagEntity"/>.
/// </summary>
internal static class PlayerTagEntityExtensions
{
    extension(PlayerTagEntity tag)
    {
        /// <summary>
        /// Maps this <see cref="PlayerTagEntity"/> to a <see cref="TagDefinitionDto"/>.
        /// </summary>
        /// <returns>A <see cref="TagDefinitionDto"/> populated from the tag definition's permanent profile.</returns>
        public TagDefinitionDto ToTagDefinitionDto()
            => new()
            {
                PlayerTagId = tag.PlayerTagId,
                Name = tag.Name,
                Color = tag.Color,
                LifecycleStatus = tag.LifecycleStatus
            };
    }
}
