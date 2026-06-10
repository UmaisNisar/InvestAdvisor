using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> b)
    {
        b.ToTable("NewsItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(32);
        b.Property(x => x.Headline).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Source).HasMaxLength(128).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        b.Property(x => x.Channel).HasConversion<int>();
        b.Property(x => x.SentimentScore).HasColumnType("decimal(4,3)");
        b.Property(x => x.SentimentLabel).HasMaxLength(16);

        b.HasIndex(x => x.Url).IsUnique();
        b.HasIndex(x => new { x.Ticker, x.PublishedAtUtc }).IsDescending(false, true);
        // The scorer scans for rows that still need scoring; index keeps that cheap.
        b.HasIndex(x => x.SentimentScoredAtUtc);
    }
}
