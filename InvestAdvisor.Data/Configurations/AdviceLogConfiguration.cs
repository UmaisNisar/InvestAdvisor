using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestAdvisor.Data.Configurations;

public sealed class AdviceLogConfiguration : IEntityTypeConfiguration<AdviceLog>
{
    public void Configure(EntityTypeBuilder<AdviceLog> b)
    {
        b.ToTable("AdviceLog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Trigger).HasConversion<int>();
        b.Property(x => x.TriggerDetail).HasMaxLength(500).IsRequired();
        b.Property(x => x.Model).HasMaxLength(128).IsRequired();
        b.Property(x => x.ParsedSummary).HasMaxLength(8000).IsRequired();
        b.Property(x => x.SystemPromptUsed).HasMaxLength(16000).IsRequired();

        // Large JSON columns — no length cap (SQLite TEXT)
        b.Property(x => x.StructuredInputJson).IsRequired();
        b.Property(x => x.RawResponseText).IsRequired();
        b.Property(x => x.ParsedFlagsJson).IsRequired();
        b.Property(x => x.ParsedDriftAlertsJson).IsRequired();
        b.Property(x => x.ParsedConsiderationsJson).IsRequired();

        b.HasIndex(x => x.TimestampUtc).IsDescending(true);
        b.HasIndex(x => x.ReplayOfAdviceLogId);
    }
}
