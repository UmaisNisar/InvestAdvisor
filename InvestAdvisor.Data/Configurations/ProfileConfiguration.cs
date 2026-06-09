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
        b.Property(x => x.GoalsText).HasMaxLength(4000).IsRequired();
        b.Property(x => x.RiskTolerance).HasConversion<int>();
        b.Property(x => x.TimeHorizon).HasConversion<int>();
        b.Property(x => x.DriftPctThreshold).HasPrecision(8, 4);
        b.Property(x => x.SingleDayMovePctThreshold).HasPrecision(8, 4);
        b.Property(x => x.SystemPromptOverride).HasMaxLength(8000);
        // One profile per tenant (the seeded singleton is gone; the migration creates the owner's).
        b.HasIndex(x => x.TenantId).IsUnique();
    }
}
