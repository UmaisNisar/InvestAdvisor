using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> b)
    {
        b.ToTable("Holding");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(255).IsRequired();
        b.Property(x => x.AssetClass).HasConversion<int>();
        b.Property(x => x.AccountType).HasConversion<int>();
        b.Property(x => x.Quantity).HasPrecision(18, 8);
        b.Property(x => x.AvgCost).HasPrecision(18, 4);
        b.Property(x => x.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        b.Property(x => x.TargetAllocationPct).HasPrecision(8, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);

        b.HasIndex(x => new { x.Ticker, x.AccountType });
    }
}
