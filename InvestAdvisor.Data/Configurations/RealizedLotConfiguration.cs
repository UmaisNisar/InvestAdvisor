using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class RealizedLotConfiguration : IEntityTypeConfiguration<RealizedLot>
{
    public void Configure(EntityTypeBuilder<RealizedLot> b)
    {
        b.ToTable("RealizedLot");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(255).IsRequired();
        b.Property(x => x.AssetClass).HasConversion<int>();
        b.Property(x => x.AccountType).HasConversion<int>();
        b.Property(x => x.Quantity).HasPrecision(18, 8);
        b.Property(x => x.Proceeds).HasPrecision(18, 4);
        b.Property(x => x.CostBasis).HasPrecision(18, 4);
        b.Property(x => x.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        b.Property(x => x.SourceHash).HasMaxLength(64).IsRequired().HasDefaultValue("");

        // Idempotent imports: an Activity row maps to one lot. Re-uploading the same (or an
        // overlapping) export must not duplicate. Filtered so blank hashes (manual entries) don't collide.
        b.HasIndex(x => new { x.TenantId, x.SourceHash })
            .IsUnique()
            .HasFilter("\"SourceHash\" <> ''");
        b.HasIndex(x => new { x.TenantId, x.Ticker });
    }
}
