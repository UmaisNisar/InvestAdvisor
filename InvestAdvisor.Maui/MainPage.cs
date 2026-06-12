using Microsoft.AspNetCore.Components.WebView.Maui;

namespace InvestAdvisor.Maui;

/// <summary>Single page hosting the whole Blazor UI from the shared InvestAdvisor.Ui RCL.</summary>
public class MainPage : ContentPage
{
    public MainPage()
    {
        var webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
        webView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(InvestAdvisor.Ui.Root),
        });
        Content = webView;
    }
}
