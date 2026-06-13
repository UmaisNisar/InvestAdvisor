using InvestAdvisor.Core.Swing;

namespace InvestAdvisor.Core.Entities;

public class RuntimeSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public int TickIntervalSeconds { get; set; } = 300;
    public bool MarketHoursOnly { get; set; } = true;
    public string TimeZoneId { get; set; } = "America/New_York";
    public int MaxRunsPerDay { get; set; } = 12;
    public int MinSecondsBetweenRuns { get; set; } = 1800;

    /// <summary>Master kill switch: when true, the worker skips all AI analysis runs (no LLM spend).</summary>
    public bool AgentPaused { get; set; }

    /// <summary>
    /// Hard daily spend ceiling in USD for AI calls. When today's estimated spend reaches this,
    /// worker-triggered runs and the daily recommendation are skipped until UTC midnight. Manual
    /// "Run now" still works. 0 = unlimited.
    /// </summary>
    public decimal DailyBudgetUsd { get; set; } = 2m;

    public int MaxSnapshotAgeForTriggerSeconds { get; set; } = 600;
    public int MinPriceFreshnessSeconds { get; set; } = 60;

    /// <summary>UI preference: dark (night) mode. Persisted so the choice survives reloads.</summary>
    public bool DarkMode { get; set; }

    // AI provider + models, switchable at runtime in Settings → AI Provider. Defaults to Google
    // Gemini's free tier so a fresh install costs nothing. API keys stay in config (user-secrets /
    // env vars), never in this table. Provider ids: "gemini" | "anthropic" | "custom" — see
    // Core.Agent.LlmProviders.
    public string LlmProvider { get; set; } = "gemini";

    /// <summary>Primary model: manual runs, per-stock analysis, the daily recommendation.</summary>
    public string LlmModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Cheaper/faster model for worker-triggered runs and batch sentiment scoring.</summary>
    public string LlmRoutineModel { get; set; } = "gemini-2.5-flash-lite";

    /// <summary>Base URL for the "custom" provider (OpenAI-compatible, e.g. Groq/OpenRouter/Ollama).</summary>
    public string? LlmCustomBaseUrl { get; set; }

    // Screener composite-score factor weights (relative; the scorer normalizes by their sum,
    // so they need not add to 100). Editable in Settings.
    public int WeightValuation { get; set; } = 20;
    public int WeightGrowth { get; set; } = 25;
    public int WeightQuality { get; set; } = 10;
    public int WeightAnalyst { get; set; } = 20;
    public int WeightInsider { get; set; } = 10;
    public int WeightMomentum { get; set; } = 15;
    /// <summary>News + social-media sentiment factor (LLM-graded over a recent window).</summary>
    public int WeightSentiment { get; set; } = 10;

    /// <summary>How aggressively the swing scanner surfaces setups (Low/Medium/High). User-set on /swing.</summary>
    public SwingRiskLevel SwingRiskLevel { get; set; } = SwingRiskLevel.Medium;

    // Optional sources for auto-importing holdings (e.g. a Wealthsimple export). When set, the
    // import worker pulls them on its daily cycle; manual import via the Settings buttons works
    // regardless. Path = a CSV file on the server; Url = a CSV URL (e.g. a published Google Sheet
    // that Wealthica syncs your holdings into).
    public string? HoldingsCsvPath { get; set; }
    public string? HoldingsCsvUrl { get; set; }

    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFrom { get; set; }
    public string? SmtpTo { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
