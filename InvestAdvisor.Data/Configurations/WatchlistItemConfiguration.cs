using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class WatchlistItemConfiguration : IEntityTypeConfiguration<WatchlistItem>
{
    public void Configure(EntityTypeBuilder<WatchlistItem> b)
    {
        b.ToTable("WatchlistItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(32).IsRequired();
        b.Property(x => x.AssetClass).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.PriceTargetLow).HasPrecision(18, 4);
        b.Property(x => x.PriceTargetHigh).HasPrecision(18, 4);

        b.HasIndex(x => new { x.Ticker, x.AssetClass }).IsUnique();
    }
}
