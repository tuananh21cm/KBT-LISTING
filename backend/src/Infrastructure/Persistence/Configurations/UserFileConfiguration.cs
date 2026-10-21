using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for user files.
/// </summary>
public class UserFileConfiguration : IEntityTypeConfiguration<UserFile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserFile> builder)
    {
        builder.ToTable("UserFiles");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(f => f.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.PublicUrl).HasMaxLength(2000);
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(f => f.HashAlgorithm).HasMaxLength(20).IsRequired();
        builder.Property(f => f.Metadata).HasColumnType("jsonb");
        builder.Property(f => f.LinkedEntityType).HasMaxLength(50);

        builder.HasIndex(f => new { f.OwnerId, f.FileHash }).IsUnique();
        builder.HasIndex(f => f.OwnerId);

        builder.HasOne(f => f.Owner)
            .WithMany()
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
