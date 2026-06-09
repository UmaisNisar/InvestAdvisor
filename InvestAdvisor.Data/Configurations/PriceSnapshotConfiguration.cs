using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class PriceSnapshotConfiguration : IEntityTypeConfiguration<PriceSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceSnapshot> b)
    {
        b.ToTable("PriceSnapshot");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(32).IsRequired();
        b.Property(x => x.AssetClass).HasConversion<int>();
        b.Property(x => x.Price).HasPrecision(18, 8);
        b.Property(x => x.PreviousClose).HasPrecision(18, 8);
        b.Property(x => x.PercentChange).HasPrecision(8, 4);
        b.Property(x => x.Currency).HasMaxLength(8).IsRequired();

        b.HasIndex(x => new { x.Ticker, x.FetchedAtUtc }).IsDescending(false, true);
    }
}
