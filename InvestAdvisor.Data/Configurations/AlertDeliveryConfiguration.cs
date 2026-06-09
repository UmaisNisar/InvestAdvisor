using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class AlertDeliveryConfiguration : IEntityTypeConfiguration<AlertDelivery>
{
    public void Configure(EntityTypeBuilder<AlertDelivery> b)
    {
        b.ToTable("AlertDelivery");
        b.HasKey(x => x.Id);
        b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);

        b.HasOne(x => x.AdviceLog)
            .WithMany()
            .HasForeignKey(x => x.AdviceLogId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.AdviceLogId, x.Channel });
        b.HasIndex(x => x.Status);
    }
}
