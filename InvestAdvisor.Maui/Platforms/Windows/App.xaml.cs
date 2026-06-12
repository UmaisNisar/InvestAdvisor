namespace InvestAdvisor.Maui.WinUI;

/// <summary>WinUI entry point; defers to the shared MauiProgram composition.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
