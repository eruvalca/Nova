using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Nova User Entity Configuration.
/// </summary>
public class NovaUserEntityConfiguration : IEntityTypeConfiguration<NovaUserEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<NovaUserEntity> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}
