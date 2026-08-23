using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Core.Agent;
using InvestAdvisor.Core.Notifications;
using InvestAdvisor.Core.Options;
using InvestAdvisor.Data.Agent;
using InvestAdvisor.Data.Events;
using InvestAdvisor.Data.Notifications;
using InvestAdvisor.Data.Providers.Anthropic;
using InvestAdvisor.Data.Providers.CoinGecko;
using InvestAdvisor.Data.Providers.Finnhub;
using InvestAdvisor.Data.Queries;
using InvestAdvisor.Data.RateLimiting;
using InvestAdvisor.Data.Runs;
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
/// <c>InvestAdvisor.Server</c> (Blazor Server) and <c>InvestAdvisor.Maui</c> (Blazor Hybrid)
/// so they share identical wiring.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string UserAgent = "InvestAdvisor/1.0";

    /// <summary>Typed client against the Finnhub host (the option's BaseUrl minus its /api/v1 path — providers add that).</summary>
    private static IHttpClientBuilder AddFinnhubClient<TClient>(this IServiceCollection services)
        where TClient : class =>
        services.AddHttpClient<TClient>(ConfigureFinnhub);

    private static IHttpClientBuilder AddFinnhubClient<TService, TImpl>(this IServiceCollection services)
        where TService : class where TImpl : class, TService =>
        services.AddHttpClient<TService, TImpl>(ConfigureFinnhub);

    private static void ConfigureFinnhub(IServiceProvider sp, HttpClient http)
    {
        var opts = sp.GetRequiredService<IOptions<FinnhubOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl.Replace("/api/v1", string.Empty));
        http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
    }

    /// <summary>
    /// A social source: its own typed HttpClient configured from <typeparamref name="TOptions"/>,
    /// plus an <see cref="ISocialFeedProvider"/> registration so the refresh service sees it in
    /// the set. Providers self-disable when not configured, so registering all is harmless.
    /// </summary>
    private static void AddSocialFeedProvider<TProvider, TOptions>(
        this IServiceCollection services,
        Func<TOptions, int> timeoutSeconds,
        Func<TOptions, string>? baseUrl = null,
        string? userAgent = UserAgent)
        where TProvider : class, ISocialFeedProvider
        where TOptions : class
    {
        services.AddHttpClient<TProvider>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<TOptions>>().Value;
            if (baseUrl is not null) http.BaseAddress = new Uri(baseUrl(opts));
            http.Timeout = TimeSpan.FromSeconds(timeoutSeconds(opts));
            if (userAgent is not null) http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.AddScoped<ISocialFeedProvider>(sp => sp.GetRequiredService<TProvider>());
    }

    public static IServiceCollection AddInvestAdvisor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<FinnhubOptions>(configuration.GetSection(FinnhubOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<StockTwitsOptions>(configuration.GetSection(StockTwitsOptions.SectionName));
        services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
        services.Configure<BlueskyOptions>(configuration.GetSection(BlueskyOptions.SectionName));
        services.Configure<HackerNewsOptions>(configuration.GetSection(HackerNewsOptions.SectionName));

        var connStr = configuration.GetConnectionString("Default")
                      ?? InvestAdvisorDbContext.GetDefaultConnectionString();
        services.AddDbContextFactory<InvestAdvisorDbContext>(opt => opt.UseSqlite(connStr));

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IFxRateProvider, Providers.Fx.FxRateProvider>();
        services.AddSingleton<IRuntimeSettingsStore, RuntimeSettingsStore>();
        services.AddSingleton<IRateLimiter, TokenBucketRateLimiter>();
        services.AddSingleton<IRunEventBus, RunEventBus>();
        services.AddSingleton<INotificationCenter, NotificationCenter>();
        services.AddSingleton<IRunManager, RunManager>();
        services.AddSingleton<ITriggerEvaluator, TriggerEvaluator>();
        services.AddSingleton<CryptoSymbolRouter>();
        services.AddSingleton<ISmtpClient, MailKitSmtpClient>();

        // LLM providers: both concrete clients are always registered; LlmClientRouter picks one per
        // call from RuntimeSettings (Settings → AI Provider), so switching needs no restart.
        services.AddHttpClient<AnthropicClient>((sp, http) =>
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
        // No BaseAddress/auth here — the endpoint (Gemini vs custom) is resolved per request.
        services.AddHttpClient<Providers.OpenAiCompat.OpenAiCompatibleClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });
        services.AddScoped<ILlmClient, Providers.LlmClientRouter>();

        // US equities/ETFs (Finnhub) + non-US listings (Yahoo) + crypto (CoinGecko) behind one
        // IMarketDataProvider that routes to the right source.
        services.AddFinnhubClient<FinnhubMarketDataProvider>();
        services.AddHttpClient<Providers.Yahoo.YahooQuoteProvider>(http =>
        {
            http.BaseAddress = new Uri("https://query1.finance.yahoo.com");
            http.Timeout = TimeSpan.FromSeconds(12);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (InvestAdvisor)");
        });
        // One composite instance backs both quote and history reads (it routes each to the right source).
        services.AddScoped<Providers.CompositeMarketDataProvider>();
        services.AddScoped<IMarketDataProvider>(sp => sp.GetRequiredService<Providers.CompositeMarketDataProvider>());
        services.AddScoped<IPriceHistoryProvider>(sp => sp.GetRequiredService<Providers.CompositeMarketDataProvider>());

        // News sources. Each concrete provider gets its own typed HttpClient; both are exposed as
        // INewsProvider so NewsRefreshService resolves them as an ordered set: Finnhub first,
        // Yahoo second — the first provider with coverage wins per ticker, so Yahoo only answers
        // for the (non-US) tickers Finnhub's free tier returns nothing for.
        services.AddFinnhubClient<FinnhubNewsProvider>();
        services.AddHttpClient<Providers.Yahoo.YahooNewsProvider>(http =>
        {
            http.BaseAddress = new Uri("https://feeds.finance.yahoo.com");
            http.Timeout = TimeSpan.FromSeconds(12);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (InvestAdvisor)");
        });
        services.AddScoped<INewsProvider>(sp => sp.GetRequiredService<FinnhubNewsProvider>());
        services.AddScoped<INewsProvider>(sp => sp.GetRequiredService<Providers.Yahoo.YahooNewsProvider>());

        services.AddFinnhubClient<IScreenerDataProvider, FinnhubScreenerProvider>();

        services.AddHttpClient<ICryptoMarketProvider, CoinGeckoProvider>(http =>
        {
            http.BaseAddress = new Uri("https://api.coingecko.com");
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("InvestAdvisor/1.0");
        });

        // Social sentiment sources. Each concrete provider gets its own typed HttpClient; both are
        // exposed as ISocialFeedProvider so SocialRefreshService resolves them as a set. Providers
        // self-disable (return empty) when not enabled/configured, so registering both is harmless.
        services.AddSocialFeedProvider<Providers.StockTwits.StockTwitsProvider, StockTwitsOptions>(
            o => o.TimeoutSeconds, o => o.BaseUrl);
        services.AddSocialFeedProvider<Providers.Reddit.RedditProvider, RedditOptions>(
            o => o.TimeoutSeconds, userAgent: null);
        services.AddSocialFeedProvider<Providers.Bluesky.BlueskyProvider, BlueskyOptions>(
            o => o.TimeoutSeconds);
        services.AddSocialFeedProvider<Providers.HackerNews.HackerNewsProvider, HackerNewsOptions>(
            o => o.TimeoutSeconds, o => o.BaseUrl);
        services.AddScoped<ISocialRefreshService, SocialRefreshService>();

        services.AddScoped<IContextAssembler, ContextAssembler>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<ICostService, CostService>();
        services.AddScoped<IPriceRefreshService, PriceRefreshService>();
        services.AddScoped<INewsRefreshService, NewsRefreshService>();

        services.AddScoped<IStockUniverseSeeder, StockUniverseSeeder>();
        services.AddScoped<IScreenerSyncService, ScreenerSyncService>();
        services.AddScoped<ISentimentScoringService, SentimentScoringService>();
        services.AddScoped<IScreenerScoringService, ScreenerScoringService>();
        services.AddScoped<IDailyRecommendationService, DailyRecommendationService>();

        // Swing (short-horizon) module: pure scorer + orchestration (read models registered below).
        services.AddSingleton<ISwingScoringService, SwingScoringService>();
        services.AddScoped<ISwingService, SwingService>();

        // Momentum (high-volatility breakout) module: pure scorer + orchestration.
        services.AddSingleton<IMomentumScoringService, InvestAdvisor.Core.Momentum.MomentumScoringService>();
        services.AddScoped<IMomentumService, MomentumService>();

        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();

        services.AddScoped<IPortfolioQueries, PortfolioQueries>();
        services.AddScoped<IScreenerQueries, ScreenerQueries>();
        services.AddScoped<ISwingQueries, SwingQueries>();
        services.AddScoped<IMomentumQueries, MomentumQueries>();
        services.AddScoped<IHoldingsService, HoldingsService>();
        services.AddScoped<IHoldingsImportService, HoldingsImportService>();
        services.AddScoped<IActivityImportService, ActivityImportService>();
        services.AddScoped<IRealizedLotsService, RealizedLotsService>();
        services.AddScoped<ITickerSearchService, TickerSearchService>();
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ITenantContext, Identity.TenantContext>();

        return services;
    }

    /// <summary>
    /// Adds friendly env-var aliases (GEMINI_API_KEY, ANTHROPIC_API_KEY, FINNHUB_API_KEY,
    /// SMTP_PASSWORD, …) so Linux deployments don't need a section-style env-var convention.
    /// </summary>
    public static IConfigurationBuilder AddInvestAdvisorEnvAliases(this IConfigurationBuilder configBuilder)
    {
        var dict = new Dictionary<string, string?>();
        Map(dict, "ANTHROPIC_API_KEY", $"{AnthropicOptions.SectionName}:ApiKey");
        Map(dict, "GEMINI_API_KEY", $"{LlmOptions.SectionName}:GeminiApiKey");
        Map(dict, "LLM_API_KEY", $"{LlmOptions.SectionName}:CustomApiKey");
        Map(dict, "LLM_BASE_URL", $"{LlmOptions.SectionName}:CustomBaseUrl");
        Map(dict, "FINNHUB_API_KEY", $"{FinnhubOptions.SectionName}:ApiKey");
        Map(dict, "SMTP_PASSWORD", $"{SmtpOptions.SectionName}:Password");
        Map(dict, "STOCKTWITS_ACCESS_TOKEN", $"{StockTwitsOptions.SectionName}:AccessToken");
        Map(dict, "REDDIT_CLIENT_ID", $"{RedditOptions.SectionName}:ClientId");
        Map(dict, "REDDIT_CLIENT_SECRET", $"{RedditOptions.SectionName}:ClientSecret");
        Map(dict, "BLUESKY_IDENTIFIER", $"{BlueskyOptions.SectionName}:Identifier");
        Map(dict, "BLUESKY_APP_PASSWORD", $"{BlueskyOptions.SectionName}:AppPassword");
        if (dict.Count > 0) configBuilder.AddInMemoryCollection(dict);
        return configBuilder;
    }

    private static void Map(Dictionary<string, string?> dict, string envName, string key)
    {
        var val = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(val)) dict[key] = val;
    }
}
