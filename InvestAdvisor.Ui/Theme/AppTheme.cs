using MudBlazor;

namespace InvestAdvisor.Ui.Theme;

/// <summary>
/// Central application theme. Follows Apple's iOS 26 (Liquid Glass era) color system with
/// the monochrome tint Apple's own finance apps use (Stocks / Wallet): near-black controls
/// in light mode that invert to near-white in dark mode, so the green/red market data is
/// the only color on screen. Semantic states keep the refreshed system colors, and surfaces,
/// labels and separators follow Apple's dynamic gray ramp. Defined once here so both hosts
/// (Photino desktop + Blazor Server) share it.
/// </summary>
public static class AppTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1C1C1E",            // near-black tint (Stocks/Wallet monochrome)
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#00C3D0",          // systemTeal
            Info = "#0088FF",               // systemBlue
            Success = "#34C759",            // systemGreen
            Warning = "#FF8D28",            // systemOrange
            Error = "#FF383C",              // systemRed
            Background = "#F2F2F7",         // systemGroupedBackground
            BackgroundGray = "#E5E5EA",     // systemGray5
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",   // light bar with dark text, not a colored block
            AppbarText = "#000000",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#3A3A3C",
            DrawerIcon = "#636366",
            TextPrimary = "#000000",        // label
            TextSecondary = "#8A8A8E",      // secondaryLabel flattened over white
            ActionDefault = "#8E8E93",      // systemGray
            LinesDefault = "#C6C6C8",       // opaqueSeparator
            LinesInputs = "#C7C7CC",        // systemGray3
            TableLines = "#C6C6C8",
            Divider = "#C6C6C8",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#F2F2F7",            // near-white tint (monochrome inverts in dark mode)
            PrimaryContrastText = "#000000",
            Secondary = "#40C8E0",          // systemTeal (dark)
            Info = "#0A84FF",               // systemBlue (dark)
            Success = "#30D158",            // systemGreen (dark)
            Warning = "#FF9F0A",            // systemOrange (dark)
            Error = "#FF453A",              // systemRed (dark)
            Background = "#000000",         // systemBackground (dark)
            BackgroundGray = "#1C1C1E",     // secondarySystemBackground
            Surface = "#1C1C1E",
            AppbarBackground = "#1C1C1E",
            AppbarText = "#FFFFFF",
            DrawerBackground = "#1C1C1E",
            DrawerText = "#D1D1D6",
            DrawerIcon = "#8E8E93",
            TextPrimary = "#FFFFFF",        // label (dark)
            TextSecondary = "#8D8D93",      // secondaryLabel flattened over black
            ActionDefault = "#8E8E93",
            LinesDefault = "#38383A",       // opaqueSeparator (dark)
            LinesInputs = "#48484A",        // systemGray3 (dark)
            TableLines = "#38383A",
            Divider = "#38383A",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                // Prefer the platform UI font first (San Francisco on iOS/macOS, Segoe on
                // Windows) so the app reads as native; Inter is the cross-platform fallback.
                FontFamily = new[] { "-apple-system", "BlinkMacSystemFont", "SF Pro Text", "Inter", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif" },
            },
            Button = new ButtonTypography
            {
                // Apple never shouts: sentence-case buttons, no Material tracking.
                TextTransform = "none",
                LetterSpacing = "0",
                FontWeight = "600",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            // Generous curvature: inputs, menus, panels and chips all inherit this.
            DefaultBorderRadius = "14px",
        },
    };
}
