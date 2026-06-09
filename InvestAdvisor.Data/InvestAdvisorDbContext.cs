using InvestAdvisor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestAdvisor.Data;

public sealed class InvestAdvisorDbContext(DbContextOptions<InvestAdvisorDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<RuntimeSettings> RuntimeSettings => Set<RuntimeSettings>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<AdviceLog> AdviceLogs => Set<AdviceLog>();
    public DbSet<AlertDelivery> AlertDeliveries => Set<AlertDelivery>();

    // Screener (market-scanning) entities.
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<StockMetric> StockMetrics => Set<StockMetric>();
    public DbSet<AnalystRating> AnalystRatings => Set<AnalystRating>();
    public DbSet<InsiderTrade> InsiderTrades => Set<InsiderTrade>();
    public DbSet<StockAnalysis> StockAnalyses => Set<StockAnalysis>();
    public DbSet<ScreenerScore> ScreenerScores => Set<ScreenerScore>();
    public DbSet<DailyRecommendation> DailyRecommendations => Set<DailyRecommendation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvestAdvisorDbContext).Assembly);
    }

    /// <summary>
    /// Default SQLite file location, cross-platform:
    /// Windows -> %LOCALAPPDATA%\InvestAdvisor\app.db
    /// macOS   -> ~/Library/Application Support/InvestAdvisor/app.db
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InvestAdvisor");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "app.db");
    }

    public static string GetDefaultConnectionString()
        => $"Data Source={GetDefaultDatabasePath()}";
}
