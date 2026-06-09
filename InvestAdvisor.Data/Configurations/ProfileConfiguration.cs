using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> b)
    {
        b.ToTable("Profile");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.GoalsText).HasMaxLength(4000).IsRequired();
        b.Property(x => x.RiskTolerance).HasConversion<int>();
        b.Property(x => x.TimeHorizon).HasConversion<int>();
        b.Property(x => x.DriftPctThreshold).HasPrecision(8, 4);
        b.Property(x => x.SingleDayMovePctThreshold).HasPrecision(8, 4);
        b.Property(x => x.SystemPromptOverride).HasMaxLength(8000);

        b.HasData(new Profile
        {
            Id = Profile.SingletonId,
            GoalsText = "Long-term growth with disciplined rebalancing.",
            RiskTolerance = RiskTolerance.Moderate,
            TimeHorizon = TimeHorizon.LongTerm,
            DriftPctThreshold = 5m,
            SingleDayMovePctThreshold = 7m,
            RebalanceCadenceHours = 24,
            SystemPromptOverride = null,
            UpdatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
