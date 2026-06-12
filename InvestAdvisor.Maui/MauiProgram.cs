using System.Globalization;
using InvestAdvisor.Core.Abstractions;
using InvestAdvisor.Data.Composition;
using InvestAdvisor.Data.HostedServices;
using InvestAdvisor.Maui.HostServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace InvestAdvisor.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Same fixed culture as the server host: all displayed dollar figures are USD.
        var appCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = appCulture;
        CultureInfo.DefaultThreadCurrentUICulture = appCulture;

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var configuration = BuildConfiguration();
        builder.Services.AddSingleton<IConfiguration>(configuration);

        builder.Logging.AddConfiguration(configuration.GetSection("Logging"));
        builder.Logging.AddDebug();

        builder.Services.AddInvestAdvisor(configuration);
        builder.Services.AddMudServices();
        builder.Services.AddMauiBlazorWebView();

        // Desktop is single-user; the tenant seam still runs so the same Ui pages work here
        // and against the multi-tenant server.
        builder.Services.AddScoped<ICurrentUserAccessor, LocalUserAccessor>();

        // The desktop app is launched explicitly, so the credit-spending workers default on; set
        // Scheduler:WorkerEnabled=false to run the UI without the agent loop. The holdings
        // importer has no LLM cost, so it always runs — same split as the server host.
        if (configuration.GetValue($"{InvestAdvisor.Core.Options.SchedulerOptions.SectionName}:WorkerEnabled", true))
        {
            builder.Services.AddHostedService<InvestAdvisorWorker>();
            builder.Services.AddHostedService<ScreenerWorker>();
        }
        builder.Services.AddHostedService<HoldingsImportWorker>();

        builder.Services.AddSingleton<EngineRunner>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(MauiProgram).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInvestAdvisorEnvAliases()
            .Build();
}
