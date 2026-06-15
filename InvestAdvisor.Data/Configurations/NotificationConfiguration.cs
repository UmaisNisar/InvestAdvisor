using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notification");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Severity).HasConversion<int>();
        b.Property(x => x.LinkUrl).HasMaxLength(256);

        // Bell queries are always "this tenant's most recent" and "this tenant's unread count".
        b.HasIndex(x => new { x.TenantId, x.CreatedUtc });
        b.HasIndex(x => new { x.TenantId, x.ReadUtc });
    }
}
