using System.Net;
using System.Text;
using InvestAdvisor.Core.Entities;
using InvestAdvisor.Core.Enums;
using InvestAdvisor.Core.Models;

namespace InvestAdvisor.Core.Notifications;

/// <summary>
/// Builds the HTML + plain-text digest sent by <see cref="EmailNotificationChannel"/>.
/// Always appends a non-advice disclaimer footer so the user is reminded the LLM is
/// not their financial advisor.
/// </summary>
public static class DigestRenderer
{
    public const string DisclaimerFooter =
        "InvestAdvisor is a personal research tool, not a licensed financial advisor. " +
        "The LLM analyzes data; you make every decision.";

    public static string BuildSubject(AdviceLog row) =>
        $"InvestAdvisor [{row.Trigger}]: {Truncate(row.TriggerDetail, 80)}";

    public static (string Html, string Plain) BuildBody(AdviceLog row, AgentAnalysis analysis)
    {
        return (BuildHtml(row, analysis), BuildPlain(row, analysis));
    }

    private static string BuildHtml(AdviceLog row, AgentAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><body style=\"font-family:system-ui,-apple-system,Segoe UI,sans-serif;line-height:1.4;color:#222;\">");
        sb.AppendLine($"<h2 style=\"margin-bottom:0\">InvestAdvisor — {Encode(row.Trigger.ToString())}</h2>");
        sb.AppendLine($"<div style=\"color:#666;font-size:13px;\">{Encode(row.TriggerDetail)} · {row.TimestampUtc:yyyy-MM-dd HH:mm} UTC</div>");
        sb.AppendLine($"<p>{Encode(a.Summary)}</p>");

        if (a.Flags.Count > 0)
        {
            sb.AppendLine("<h3>Flags</h3><ul>");
            foreach (var f in a.Flags.OrderByDescending(f => (int)f.Severity))
            {
                var color = SeverityColor(f.Severity);
                sb.AppendLine(
                    $"<li><span style=\"display:inline-block;padding:1px 6px;border-radius:3px;background:{color};color:#fff;font-size:11px;text-transform:uppercase;\">{f.Severity}</span> " +
                    $"<strong>{Encode(f.Title)}</strong>" +
                    (f.Ticker is null ? "" : $" <code>{Encode(f.Ticker)}</code>") +
                    (f.KnownTicker ? "" : " <em style=\"color:#a00\">(unknown ticker)</em>") +
                    $"<br><span style=\"color:#444\">{Encode(f.Detail)}</span></li>");
            }
            sb.AppendLine("</ul>");
        }

        if (a.DriftAlerts.Count > 0)
        {
            sb.AppendLine("<h3>Drift alerts</h3><ul>");
            foreach (var d in a.DriftAlerts)
            {
                sb.AppendLine(
                    $"<li><code>{Encode(d.Ticker)}</code> at <strong>{d.CurrentPct:0.0}%</strong> vs target {d.TargetPct:0.0}% " +
                    $"(drift {d.DriftPct:+0.0;-0.0}%, {Encode(d.Severity.ToString())})<br>" +
                    $"<span style=\"color:#444\">{Encode(d.Note)}</span></li>");
            }
            sb.AppendLine("</ul>");
        }

        if (a.Considerations.Count > 0)
        {
            sb.AppendLine("<h3>Considerations</h3><ul>");
            foreach (var c in a.Considerations)
                sb.AppendLine($"<li><strong>{Encode(c.Topic)}.</strong> {Encode(c.Text)}</li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("<hr><div style=\"font-size:11px;color:#888\">");
        sb.AppendLine(Encode(DisclaimerFooter));
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static string BuildPlain(AdviceLog row, AgentAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"InvestAdvisor — {row.Trigger}");
        sb.AppendLine($"{row.TriggerDetail} · {row.TimestampUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine(a.Summary);
        sb.AppendLine();

        if (a.Flags.Count > 0)
        {
            sb.AppendLine("FLAGS");
            foreach (var f in a.Flags.OrderByDescending(f => (int)f.Severity))
            {
                var t = f.Ticker is null ? "" : $" [{f.Ticker}]";
                var unk = f.KnownTicker ? "" : " (unknown ticker)";
                sb.AppendLine($"- [{f.Severity}]{t}{unk} {f.Title}");
                sb.AppendLine($"    {f.Detail}");
            }
            sb.AppendLine();
        }

        if (a.DriftAlerts.Count > 0)
        {
            sb.AppendLine("DRIFT");
            foreach (var d in a.DriftAlerts)
            {
                sb.AppendLine($"- {d.Ticker}: {d.CurrentPct:0.0}% vs target {d.TargetPct:0.0}% (drift {d.DriftPct:+0.0;-0.0}%, {d.Severity})");
                sb.AppendLine($"    {d.Note}");
            }
            sb.AppendLine();
        }

        if (a.Considerations.Count > 0)
        {
            sb.AppendLine("CONSIDERATIONS");
            foreach (var c in a.Considerations)
                sb.AppendLine($"- {c.Topic}. {c.Text}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine(DisclaimerFooter);
        return sb.ToString();
    }

    private static string SeverityColor(FlagSeverity s) => s switch
    {
        FlagSeverity.Critical => "#c33",
        FlagSeverity.Warn => "#c80",
        _ => "#779",
    };

    private static string Encode(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
