using InvestAdvisor.Maui.HostServices;

namespace InvestAdvisor.Maui;

public class App : Application
{
    private readonly EngineRunner _engine;

    public App(EngineRunner engine)
    {
        _engine = engine;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Migrate + start workers before the UI renders, like the Photino host did. Task.Run
        // escapes the UI SynchronizationContext so blocking here can't deadlock.
        Task.Run(_engine.StartAsync).GetAwaiter().GetResult();

        var window = new Window(new MainPage())
        {
            Title = "InvestAdvisor",
            Width = 1400,
            Height = 900,
        };
        window.Destroying += (_, _) => Task.Run(_engine.StopAsync).GetAwaiter().GetResult();
        return window;
    }
}
