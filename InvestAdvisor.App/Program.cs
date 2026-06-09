using InvestAdvisor.Data;
using InvestAdvisor.Data.Composition;
using InvestAdvisor.Data.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Photino.Blazor;

namespace InvestAdvisor.App;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        ConfigureServices(builder);
        builder.RootComponents.Add<InvestAdvisor.Ui.Root>("#app");

        var app = builder.Build();

        await MigrateDatabaseAsync(app.Services);

        var workerCts = new CancellationTokenSource();
        var hostedServices = app.Services.GetServices<IHostedService>().ToArray();
        foreach (var svc in hostedServices)
            await svc.StartAsync(workerCts.Token);

        app.MainWindow
            .SetTitle("InvestAdvisor")
            .SetUseOsDefaultSize(false)
            .SetSize(1400, 900);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.ExceptionObject);
        };

        app.Run();

        // Shutdown path — Photino's Run returned after the window closed.
        workerCts.Cancel();
        foreach (var svc in hostedServices)
        {
            try { await svc.StopAsync(CancellationToken.None); }
            catch { /* swallow on shutdown */ }
        }
    }

    private static void ConfigureServices(PhotinoBlazorAppBuilder builder)
    {
        var configuration = BuildConfiguration();
        builder.Services.AddSingleton<IConfiguration>(configuration);

        builder.Services.AddLogging(b =>
        {
            b.AddConfiguration(configuration.GetSection("Logging"));
            b.AddConsole();
            b.AddDebug();
        });

        builder.Services.AddInvestAdvisor(configuration);
        builder.Services.AddMudServices();
        builder.Services.AddHostedService<InvestAdvisorWorker>();
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInvestAdvisorEnvAliases()
            .Build();

    private static async Task MigrateDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InvestAdvisorDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }
}
