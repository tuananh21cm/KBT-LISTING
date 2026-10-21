using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for receiving card sources.</summary>
public class ReceivingCardSourceConfiguration : IEntityTypeConfiguration<ReceivingCardSource>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReceivingCardSource> builder)
    {
        builder.ToTable("ReceivingCardSources");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.IsActive);
    }
}
