using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for <see cref="TagDefinitionMutationReceiptEntity"/>, the durable
/// mutation receipt that protects ambiguous-commit verification for tag definitions.
/// </summary>
public class TagDefinitionMutationReceiptEntityConfiguration : IEntityTypeConfiguration<TagDefinitionMutationReceiptEntity>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<TagDefinitionMutationReceiptEntity> builder)
    {
        builder.HasKey(e => e.TagDefinitionMutationReceiptId);
        builder.Property(e => e.TagDefinitionMutationReceiptId)
            .ValueGeneratedOnAdd();

        builder.HasIndex(e => e.ClubId);
        builder.HasIndex(e => e.OperationId)
            .IsUnique();

        builder
            .HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
