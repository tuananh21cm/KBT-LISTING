using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="OrderFulfilAttempt"/>.</summary>
public class OrderFulfilAttemptConfiguration : IEntityTypeConfiguration<OrderFulfilAttempt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderFulfilAttempt> builder)
    {
        builder.ToTable("OrderFulfilAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Outcome).HasMaxLength(20);
        builder.Property(a => a.SessionLabel).HasMaxLength(60);
        builder.Property(a => a.CustomerKey).HasMaxLength(80);

        // Một phiên chỉ được có đúng một lượt đang mở trên một đơn. Không unique theo OrderId
        // đơn lẻ: nhiều profile của cùng người giữ chung một đơn là hợp lệ.
        builder.HasIndex(a => new { a.OrderId, a.SessionId })
            .IsUnique()
            .HasFilter("\"ClosedAt\" IS NULL");

        // Đếm/đóng các phiên đang mở của một đơn (ref-count khi nhả, sweep hết hạn).
        builder.HasIndex(a => new { a.OrderId, a.ClosedAt });
        builder.HasIndex(a => new { a.UserId, a.StartedAt });
        builder.HasIndex(a => a.BatchId);
        builder.HasIndex(a => a.ExpiresAt);

        // Khớp billno theo dấu Đã mua + suy người mua theo CustomerKey.
        builder.HasIndex(a => new { a.CustomerKey, a.CompletedAt })
            .HasDatabaseName("IX_OrderFulfilAttempts_CustomerKey_CompletedAt")
            .IsDescending(false, true)
            .HasFilter("\"Outcome\" = 'Purchased' AND \"CustomerKey\" IS NOT NULL");
        builder.HasIndex(a => new { a.OrderId, a.Outcome })
            .HasDatabaseName("IX_OrderFulfilAttempts_OrderId_Outcome");

        builder.HasOne(a => a.Order)
            .WithMany()
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
