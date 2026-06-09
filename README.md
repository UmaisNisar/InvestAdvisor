# InvestAdvisor

Personal, single-user investment **monitoring, screening, and advisory** tool. It pulls your
holdings and live market data, an LLM analyzes them, and it surfaces plain-language observations,
per-holding calls (add / hold / trim / sell), and a daily "where to invest" shortlist. **You execute
any trades yourself elsewhere** — nothing here places orders or talks to a brokerage.

> InvestAdvisor is a personal research tool, not a licensed financial advisor.
> The LLM analyzes the data you give it; you make every decision, and it can be wrong.

## What it does

- **Portfolio analysis with per-holding calls.** On a trigger (or on demand), one Anthropic call
  reviews the whole portfolio and emits, for *every* holding, a stance — **Add / Hold / Trim / Sell** —
  with a **conviction** (high / med / low) and a one-line, data-grounded reason, plus attention flags,
  drift-from-target alerts, and neutral considerations. The dashboard surfaces the strongest calls first.
- **Daily "where to invest."** One consolidated AI call ranks across **stocks, ETFs, and crypto** and
  returns a focused shortlist tailored to your profile — capped to one call/day to control cost.
- **Market screener.** A curated universe of equities, ETFs, and crypto scored with a percentile-rank
  factor model (growth, valuation, analyst, momentum, quality, insider — asset-class-aware).
- **Multi-currency.** Holdings can be **USD / CAD / AUD**; everything is converted to USD on the backend
  at live FX rates for totals, P/L, and allocation. Prices display in their native currency.
- **Multi-source pricing** (all free tiers, no premium needed): **Finnhub** for US equities/ETFs,
  **Yahoo Finance** for non-US listings (`.TO`, `.AX`, …), **CoinGecko** for crypto, **Frankfurter** for FX.
- **Wealthsimple CSV import.** Drops a Wealthsimple holdings export straight in — reconstructs quotable
  tickers (Symbol + exchange → `IDIV.B.TO`), reads per-row currency, and converts cost basis.
- **Watchlist** with optional low/high price-target triggers.
- **Email alerts** on warn/critical flags or action-suggested drift.
- **Responsive dashboard** (MudBlazor) that works on phones, with light/dark mode persisted to the DB.

## Stack

- **.NET 10** (SDK pinned via `global.json`)
- **Blazor Server** (`InvestAdvisor.Server`) + **MudBlazor** — the recommended host; reachable from any browser
- **EF Core + SQLite** — single file at `LocalApplicationData/InvestAdvisor/app.db`, migrated on startup
- **Background workers** (`IHostedService`) — price/news refresh, screener sync, daily recommendation, CSV auto-import
- **LLM:** Anthropic Messages API (`claude-sonnet-4-6`), structured output via forced tool use
- **Data:** Finnhub · Yahoo Finance · CoinGecko · Frankfurter (FX)

> There is also a **Photino desktop host** (`InvestAdvisor.App`) that shares the same UI, but it is
> **currently broken on .NET 10** — run the **Blazor Server** host instead (instructions below).

Both hosts share the same Razor pages (`InvestAdvisor.Ui` RCL) and identical DI wiring
(`Data.Composition.ServiceCollectionExtensions.AddInvestAdvisor`).

## Repository layout

```
InvestAdvisor/
├── InvestAdvisor.Server/  # Blazor Server host (run this; also the Linux/Oracle deployable)
├── InvestAdvisor.App/     # Photino desktop host (shares the UI; broken on .NET 10)
├── InvestAdvisor.Ui/      # Razor Class Library — Pages, Components, Shared, app.css
├── InvestAdvisor.Core/    # Domain, agent contracts, abstractions, options (no infra deps)
├── InvestAdvisor.Data/    # EF Core, providers (Anthropic/Finnhub/Yahoo/CoinGecko/Fx), services, queries, workers, DI
├── InvestAdvisor.Test/    # xUnit tests
├── deploy/                # systemd unit, install-vps.sh, ship.sh + ship.ps1, deploy README (Oracle/Hetzner)
├── global.json
├── Directory.Build.props
└── Directory.Packages.props
```

## Run it (Windows / macOS / Linux)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone + restore:
   ```bash
   git clone https://github.com/UmaisNisar/InvestAdvisor.git
   cd InvestAdvisor
   dotnet restore
   ```
3. Set your API keys (kept out of the repo via user-secrets):
   ```bash
   dotnet user-secrets init --project InvestAdvisor.Server
   dotnet user-secrets set "Anthropic:ApiKey" "<your-anthropic-key>" --project InvestAdvisor.Server
   dotnet user-secrets set "Finnhub:ApiKey"   "<your-finnhub-key>"   --project InvestAdvisor.Server
   # optional, only for email alerts:
   dotnet user-secrets set "Smtp:Password"    "<your-smtp-password>"  --project InvestAdvisor.Server
   ```
   The env vars `ANTHROPIC_API_KEY`, `FINNHUB_API_KEY`, `SMTP_PASSWORD` work as overrides.
   (Yahoo, CoinGecko, and Frankfurter need **no keys**.)
4. Run the Blazor Server host:
   ```bash
   dotnet run --project InvestAdvisor.Server
   ```
   The DB migrates automatically and lands at `%LOCALAPPDATA%\InvestAdvisor\app.db`
   (`~/.local/share/InvestAdvisor/app.db` on Linux/macOS). Open the printed `http://localhost:<port>`.

   In Visual Studio: set **InvestAdvisor.Server** as the startup project and press **F5**.

## First-run usage

1. **Settings → Profile:** goals, risk tolerance, time horizon, drift / single-day-move thresholds,
   rebalance cadence, and (optionally) an override for the agent's system prompt.
2. **Add holdings** right on the Dashboard (searchable ticker picker, currency, account) — or
   **Settings → Holdings → Import from CSV** to drop in a Wealthsimple export.
3. **Watchlist:** tickers to monitor, with optional low/high price targets.
4. **Run now** (top-right of the Dashboard) runs both modules: the portfolio analysis *and* a fresh
   "where to invest" shortlist. Otherwise the worker tick refreshes data and only calls the LLM when a
   trigger you defined is crossed.
5. **Where to invest** and **Screener** pages have their own views; **Advice feed** keeps every run,
   and each can be re-run against its captured input to iterate on the prompt.

## Deploy (free, on Oracle Cloud Always Free)

The `InvestAdvisor.Server` host runs on a small Linux box behind a **Cloudflare Tunnel + Access**
(Google/GitHub login gate, end-to-end TLS, **no inbound ports**). The full walkthrough — including the
**Oracle Cloud Always Free (ARM)** $0/mo path — is in **[deploy/README.md](deploy/README.md)**. Short version:

```bash
# Once, on the box:
sudo bash deploy/install-vps.sh
sudo cloudflared service install <YOUR_TUNNEL_TOKEN>
sudo nano /etc/invest-advisor.env   # Anthropic + Finnhub keys

# From your machine, on each change:
cp deploy/.env.example deploy/.env   # set SSH_HOST; RID=linux-arm64 for Oracle ARM
./deploy/ship.ps1                    # or ./deploy/ship.sh — builds, ships, restarts
```

The login gate is **mandatory** here: the app has no built-in auth and every analysis is a paid Claude
call, so Cloudflare Access (email allowlist) is what stops strangers from spending your API credits.

## Architecture

It's a layered design with dependency inversion — a shared-kernel `Core` (interfaces + entities) that
both the UI and the infrastructure depend on, but which depends on nothing itself:

```
        Ui  ──────►  Core  ◄──────  Data
   (frontend)   (contracts)   (backend / infra)
```

- **`Core`** — entities, enums, DTOs, and the interfaces (`IHoldingsService`, `IPortfolioQueries`,
  `IAnthropicClient`, `IMarketDataProvider`, `IFxRateProvider`, …) plus the LLM tool schemas.
- **`Data`** — implementations: EF Core, the external providers, the read-model **queries** (separate from
  the write **services** — a light CQRS split), the background workers, and the DI composition root.
- **`Ui`** — Blazor pages/components that inject `Core` interfaces and never touch `Data` directly.

Notes worth knowing:

- **The LLM never does math.** `Data/Agent/ContextAssembler.cs` computes P/L, allocation, drift, FX
  conversion, and today's % in C# before serialization. The model only synthesizes from numbers it's given.
- **Pricing is routed, not single-source.** `CompositeMarketDataProvider` sends crypto → CoinGecko,
  non-US (`.`-suffixed) tickers → Yahoo, everything else → Finnhub (Yahoo fallback) — because no single
  free tier covers US + international + crypto.
- **Structured output is forced** via Anthropic tool use: each request includes the `emit_analysis` tool
  ([`EmitAnalysisToolSchema.cs`](InvestAdvisor.Core/Agent/EmitAnalysisToolSchema.cs)) — which now carries
  the per-holding `positions[]` — with `tool_choice` set to it. A JSON fallback flags any non-tool reply.
- **Trigger priority** is deliberate and tested: `Manual > PriceTarget > BigMove > Drift > Scheduled`
  ([`TriggerEvaluator.cs`](InvestAdvisor.Core/Agent/TriggerEvaluator.cs)).
- **The system prompt is editable** (Profile → override), and every `AdviceLog` can be re-run against its
  captured input so you can tune the prompt without waiting for a trigger.

## v1 limitations

- **Single user, single shared dataset.** One portfolio / watchlist / settings per install; whoever logs
  in (via Cloudflare Access on a deploy) sees and edits the *same* data. Per-user accounts are future work.
- **Cash isn't modeled.** Allocation is computed over invested holdings only.
- **International quotes via Yahoo are unofficial** — reliable but can rate-limit; affected prices simply
  show "—" rather than breaking anything. Finnhub's free tier is US-only and its crypto candles are premium,
  which is why Yahoo/CoinGecko handle those.
- **No market-holiday calendar** — market-hours use Mon–Fri 09:30–16:00 in the configured timezone.
- **Not HA, not signed, not autoscaling** — it's a personal app: one box, one process.

## Disclaimer

InvestAdvisor is a personal research tool, not a licensed financial advisor. The LLM analyzes the data you
provide and may be wrong; past performance is not predictive; verify everything before acting. You make
every decision and execute every trade yourself.
