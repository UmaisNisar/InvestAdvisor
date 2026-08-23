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
        b.Property(x => x.LlmProvider).HasMaxLength(32).IsRequired();
        b.Property(x => x.LlmModel).HasMaxLength(128).IsRequired();
        b.Property(x => x.LlmRoutineModel).HasMaxLength(128).IsRequired();
        b.Property(x => x.LlmCustomBaseUrl).HasMaxLength(512);
        b.Property(x => x.SmtpHost).HasMaxLength(255);
        b.Property(x => x.SmtpFrom).HasMaxLength(255);
        b.Property(x => x.SmtpTo).HasMaxLength(255);

        // The singleton row. Every other column takes the entity's C# default so there is exactly
        // one place a default lives; the fixed UpdatedAtUtc keeps the seed deterministic for EF.
        b.HasData(new RuntimeSettings
        {
            Id = RuntimeSettings.SingletonId,
            UpdatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
