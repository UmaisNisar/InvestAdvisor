using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> b)
    {
        b.ToTable("Stock");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Sector).HasMaxLength(64);
        b.Property(x => x.AssetClass).HasConversion<int>();
        b.Property(x => x.ExternalId).HasMaxLength(64);
        b.HasIndex(x => x.Ticker).IsUnique();
        b.HasIndex(x => x.AssetClass);
    }
}

public sealed class StockMetricConfiguration : IEntityTypeConfiguration<StockMetric>
{
    public void Configure(EntityTypeBuilder<StockMetric> b)
    {
        b.ToTable("StockMetric");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.MarketCap).HasPrecision(18, 4);
        b.Property(x => x.PeRatio).HasPrecision(18, 4);
        b.Property(x => x.RevenueGrowthPct).HasPrecision(18, 4);
        b.Property(x => x.EpsGrowthPct).HasPrecision(18, 4);
        b.Property(x => x.DebtToEquity).HasPrecision(18, 4);
        b.Property(x => x.PriceToFreeCashFlow).HasPrecision(18, 4);
        b.Property(x => x.MomentumShort).HasPrecision(18, 4);
        b.Property(x => x.MomentumLong).HasPrecision(18, 4);
        b.Property(x => x.Beta).HasPrecision(18, 4);
        b.HasIndex(x => new { x.Ticker, x.FetchedAtUtc }).IsDescending(false, true);
    }
}

public sealed class AnalystRatingConfiguration : IEntityTypeConfiguration<AnalystRating>
{
    public void Configure(EntityTypeBuilder<AnalystRating> b)
    {
        b.ToTable("AnalystRating");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.Period).HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.Ticker, x.Period }).IsUnique();
    }
}

public sealed class InsiderTradeConfiguration : IEntityTypeConfiguration<InsiderTrade>
{
    public void Configure(EntityTypeBuilder<InsiderTrade> b)
    {
        b.ToTable("InsiderTrade");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.TransactionCode).HasMaxLength(8);
        b.Property(x => x.Change).HasPrecision(18, 2);
        b.Property(x => x.Shares).HasPrecision(18, 2);
        b.HasIndex(x => new { x.Ticker, x.FilingDate });
    }
}

public sealed class StockAnalysisConfiguration : IEntityTypeConfiguration<StockAnalysis>
{
    public void Configure(EntityTypeBuilder<StockAnalysis> b)
    {
        b.ToTable("StockAnalysis");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.ConvictionLabel).HasMaxLength(16);
        b.Property(x => x.CompositeScore).HasPrecision(18, 4);
        b.HasIndex(x => new { x.Ticker, x.GeneratedAtUtc });
    }
}

public sealed class DailyRecommendationConfiguration : IEntityTypeConfiguration<DailyRecommendation>
{
    public void Configure(EntityTypeBuilder<DailyRecommendation> b)
    {
        b.ToTable("DailyRecommendation");
        b.HasKey(x => x.Id);
        b.Property(x => x.Model).HasMaxLength(64);
        b.HasIndex(x => x.GeneratedAtUtc);
    }
}

public sealed class ScreenerScoreConfiguration : IEntityTypeConfiguration<ScreenerScore>
{
    public void Configure(EntityTypeBuilder<ScreenerScore> b)
    {
        b.ToTable("ScreenerScore");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.CompositeScore).HasPrecision(18, 4);
        b.Property(x => x.Price).HasPrecision(18, 4);
        b.HasIndex(x => new { x.Ticker, x.AsOfDate }).IsUnique();
        b.HasIndex(x => x.AsOfDate);
    }
}
