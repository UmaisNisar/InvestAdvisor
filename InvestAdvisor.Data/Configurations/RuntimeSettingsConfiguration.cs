using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class RuntimeSettingsConfiguration : IEntityTypeConfiguration<RuntimeSettings>
{
    public void Configure(EntityTypeBuilder<RuntimeSettings> b)
    {
        b.ToTable("RuntimeSettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        b.Property(x => x.SmtpHost).HasMaxLength(255);
        b.Property(x => x.SmtpFrom).HasMaxLength(255);
        b.Property(x => x.SmtpTo).HasMaxLength(255);

        b.HasData(new RuntimeSettings
        {
            Id = RuntimeSettings.SingletonId,
            TickIntervalSeconds = 300,
            MarketHoursOnly = true,
            TimeZoneId = "America/New_York",
            MaxRunsPerDay = 24,
            MinSecondsBetweenRuns = 900,
            MaxSnapshotAgeForTriggerSeconds = 600,
            MinPriceFreshnessSeconds = 60,
            WeightValuation = 20,
            WeightGrowth = 25,
            WeightQuality = 10,
            WeightAnalyst = 20,
            WeightInsider = 10,
            WeightMomentum = 15,
            EmailEnabled = false,
            SmtpPort = 587,
            SmtpEnableSsl = true,
            UpdatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
