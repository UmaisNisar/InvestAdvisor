using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Notifications;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Agent;
using InvestAdvisor.Data.Events;
using InvestAdvisor.Data.Providers.Anthropic;
using InvestAdvisor.Data.Providers.CoinGecko;
using InvestAdvisor.Data.Providers.Finnhub;
using InvestAdvisor.Data.Queries;
using InvestAdvisor.Data.RateLimiting;
using InvestAdvisor.Data.Services;
using InvestAdvisor.Data.Smtp;
using InvestAdvisor.Data.Stores;
using InvestAdvisor.Data.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvestAdvisor.Data.Composition;

/// <summary>
/// Single composition root for InvestAdvisor's domain services. Called by both
/// <c>InvestAdvisor.App</c> (Photino) and <c>InvestAdvisor.Server</c> (Blazor Server)
/// so they share identical wiring.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInvestAdvisor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<FinnhubOptions>(configuration.GetSection(FinnhubOptions.SectionName));
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));
        services.Configure<TriggerOptions>(configuration.GetSection(TriggerOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        var connStr = configuration.GetConnectionString("Default")
                      ?? InvestAdvisorDbContext.GetDefaultConnectionString();
        services.AddDbContextFactory<InvestAdvisorDbContext>(opt => opt.UseSqlite(connStr));

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IFxRateProvider, Providers.Fx.FxRateProvider>();
        services.AddSingleton<IRuntimeSettingsStore, RuntimeSettingsStore>();
        services.AddSingleton<IRateLimiter, TokenBucketRateLimiter>();
        services.AddSingleton<IRunEventBus, RunEventBus>();
        services.AddSingleton<ITriggerEvaluator, TriggerEvaluator>();
        services.AddSingleton<CryptoSymbolRouter>();
        services.AddSingleton<ISmtpClient, MailKitSmtpClient>();

        services.AddHttpClient<IAnthropicClient, AnthropicClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                http.DefaultRequestHeaders.Remove("x-api-key");
                http.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
                http.DefaultRequestHeaders.Remove("anthropic-version");
                http.DefaultRequestHeaders.Add("anthropic-version", opts.ApiVersion);
            }
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        // US equities/ETFs (Finnhub) + non-US listings (Yahoo) + crypto (CoinGecko) behind one
        // IMarketDataProvider that routes to the right source.
        services.AddHttpClient<FinnhubMarketDataProvider>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<FinnhubOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.Replace("/api/v1", string.Empty));
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });
        services.AddHttpClient<Providers.Yahoo.YahooQuoteProvider>(http =>
        {
            http.BaseAddress = new Uri("https://query1.finance.yahoo.com");
            http.Timeout = TimeSpan.FromSeconds(12);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (InvestAdvisor)");
        });
        services.AddScoped<IMarketDataProvider, Providers.CompositeMarketDataProvider>();

        services.AddHttpClient<INewsProvider, FinnhubNewsProvider>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<FinnhubOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.Replace("/api/v1", string.Empty));
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddHttpClient<IScreenerDataProvider, FinnhubScreenerProvider>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<FinnhubOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.Replace("/api/v1", string.Empty));
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddHttpClient<ICryptoMarketProvider, CoinGeckoProvider>(http =>
        {
            http.BaseAddress = new Uri("https://api.coingecko.com");
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("InvestAdvisor/1.0");
        });

        services.AddScoped<IContextAssembler, ContextAssembler>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IPriceRefreshService, PriceRefreshService>();
        services.AddScoped<INewsRefreshService, NewsRefreshService>();

        services.AddScoped<IStockUniverseSeeder, StockUniverseSeeder>();
        services.AddScoped<IScreenerSyncService, ScreenerSyncService>();
        services.AddScoped<IScreenerScoringService, ScreenerScoringService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();
        services.AddScoped<IDailyRecommendationService, DailyRecommendationService>();

        services.AddScoped<INotificationChannel, EmailNotificationChannel>();

        services.AddScoped<IPortfolioQueries, PortfolioQueries>();
        services.AddScoped<IScreenerQueries, ScreenerQueries>();
        services.AddScoped<IHoldingsService, HoldingsService>();
        services.AddScoped<IHoldingsImportService, HoldingsImportService>();
        services.AddScoped<ITickerSearchService, TickerSearchService>();
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ITenantContext, Identity.TenantContext>();

        return services;
    }

    /// <summary>
    /// Adds friendly env-var aliases (ANTHROPIC_API_KEY, FINNHUB_API_KEY, SMTP_PASSWORD)
    /// so Linux deployments don't need a section-style env-var convention.
    /// </summary>
    public static IConfigurationBuilder AddInvestAdvisorEnvAliases(this IConfigurationBuilder configBuilder)
    {
        var dict = new Dictionary<string, string?>();
        Map(dict, "ANTHROPIC_API_KEY", $"{AnthropicOptions.SectionName}:ApiKey");
        Map(dict, "FINNHUB_API_KEY", $"{FinnhubOptions.SectionName}:ApiKey");
        Map(dict, "SMTP_PASSWORD", $"{SmtpOptions.SectionName}:Password");
        if (dict.Count > 0) configBuilder.AddInMemoryCollection(dict);
        return configBuilder;
    }

    private static void Map(Dictionary<string, string?> dict, string envName, string key)
    {
        var val = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(val)) dict[key] = val;
    }
}
