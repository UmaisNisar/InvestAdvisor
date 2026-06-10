using MudBlazor;

namespace InvestAdvisor.Ui.Theme;

/// <summary>
/// Central application theme. Replaces stock MudBlazor (Material Design 2) with a
/// neutral slate surface palette, a single indigo accent, Inter typography, and a
/// larger default border radius — a cleaner, more current dashboard look. Defined
/// once here so both hosts (Photino desktop + Blazor Server) share it.
/// </summary>
public static class AppTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#4F46E5",            // indigo-600
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#0EA5E9",          // sky-500
            Info = "#2563EB",
            Success = "#16A34A",
            Warning = "#D97706",
            Error = "#DC2626",
            Background = "#F8FAFC",         // slate-50
            BackgroundGray = "#F1F5F9",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",   // light bar with dark text, not a colored block
            AppbarText = "#0F172A",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#334155",
            DrawerIcon = "#475569",
            TextPrimary = "#0F172A",        // slate-900
            TextSecondary = "#64748B",      // slate-500
            ActionDefault = "#64748B",
            LinesDefault = "#E2E8F0",       // slate-200 hairlines
            LinesInputs = "#CBD5E1",
            TableLines = "#E2E8F0",
            Divider = "#E2E8F0",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#818CF8",            // indigo-400 (brighter on dark)
            PrimaryContrastText = "#0B0F19",
            Secondary = "#38BDF8",
            Info = "#60A5FA",
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Error = "#F87171",
            Background = "#0B0F19",         // near-black slate
            BackgroundGray = "#111827",
            Surface = "#111827",
            AppbarBackground = "#0F172A",
            AppbarText = "#F1F5F9",
            DrawerBackground = "#0F172A",
            DrawerText = "#CBD5E1",
            DrawerIcon = "#94A3B8",
            TextPrimary = "#F1F5F9",
            TextSecondary = "#94A3B8",
            ActionDefault = "#94A3B8",
            LinesDefault = "#1E293B",
            LinesInputs = "#334155",
            TableLines = "#1E293B",
            Divider = "#1E293B",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                // Prefer the platform UI font first (San Francisco on iOS/macOS, Segoe on
                // Windows) so the app reads as native; Inter is the cross-platform fallback.
                FontFamily = new[] { "-apple-system", "BlinkMacSystemFont", "SF Pro Text", "Inter", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif" },
            },
        },
        LayoutProperties = new LayoutProperties
        {
            // Generous curvature: inputs, menus, panels and chips all inherit this.
            DefaultBorderRadius = "14px",
        },
    };
}
