using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for <see cref="EvaluationNoteMutationReceiptEntity"/>, the durable
/// mutation receipt that protects ambiguous-commit verification for evaluation notes.
/// </summary>
public class EvaluationNoteMutationReceiptEntityConfiguration : IEntityTypeConfiguration<EvaluationNoteMutationReceiptEntity>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<EvaluationNoteMutationReceiptEntity> builder)
    {
        builder.HasKey(e => e.EvaluationNoteMutationReceiptId);
        builder.Property(e => e.EvaluationNoteMutationReceiptId)
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
