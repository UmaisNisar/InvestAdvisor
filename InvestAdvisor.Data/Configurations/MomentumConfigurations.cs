using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class MomentumCandidateConfiguration : IEntityTypeConfiguration<MomentumCandidate>
{
    public void Configure(EntityTypeBuilder<MomentumCandidate> b)
    {
        b.ToTable("MomentumCandidate");
        b.HasKey(x => x.Id);
        b.Property(x => x.Ticker).HasMaxLength(16).IsRequired();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Rationale).HasMaxLength(512);
        b.Property(x => x.Kind).HasConversion<int>();
        foreach (var prop in new[] { nameof(MomentumCandidate.EntryLow), nameof(MomentumCandidate.EntryHigh),
                     nameof(MomentumCandidate.EntryReference), nameof(MomentumCandidate.StopLoss), nameof(MomentumCandidate.Target),
                     nameof(MomentumCandidate.RewardRiskRatio), nameof(MomentumCandidate.PositionSizePct),
                     nameof(MomentumCandidate.TargetGainPct), nameof(MomentumCandidate.CompositeScore),
                     nameof(MomentumCandidate.AtrPercent), nameof(MomentumCandidate.BreakoutStrength),
                     nameof(MomentumCandidate.RelativeVolume) })
            b.Property(prop).HasPrecision(18, 4);
        b.HasIndex(x => x.GeneratedAtUtc);
        // One candidate row per ticker per session — makes a re-scan idempotent within the day.
        b.HasIndex(x => new { x.Ticker, x.GeneratedAtUtc }).IsUnique();
    }
}

public sealed class MomentumBacktestResultConfiguration : IEntityTypeConfiguration<MomentumBacktestResult>
{
    public void Configure(EntityTypeBuilder<MomentumBacktestResult> b)
    {
        b.ToTable("MomentumBacktestResult");
        b.HasKey(x => x.Id);
        foreach (var prop in new[] { nameof(MomentumBacktestResult.WinRatePct), nameof(MomentumBacktestResult.AverageR),
                     nameof(MomentumBacktestResult.ExpectancyR), nameof(MomentumBacktestResult.ProfitFactor),
                     nameof(MomentumBacktestResult.MaxDrawdownR), nameof(MomentumBacktestResult.AverageHoldingDays) })
            b.Property(prop).HasPrecision(18, 4);
        b.HasIndex(x => x.GeneratedAtUtc);
    }
}
