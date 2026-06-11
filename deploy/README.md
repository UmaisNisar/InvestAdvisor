# Deploying InvestAdvisor to a Hetzner CPX11 (or any small Linux VPS)

This walks you from a fresh Ubuntu 24.04 VPS to a running InvestAdvisor reachable at
`https://your-subdomain.your-domain.com` with Google/GitHub SSO via Cloudflare Access,
end-to-end TLS via Cloudflare Tunnel, **no open inbound ports on the VPS at all**.

Total spend: ~€4.35/mo for the box. Cloudflare Tunnel + Access (up to 50 users) is free.

**Want it free?** Use an **Oracle Cloud Always Free** ARM VM instead of the Hetzner box — same kit,
**$0/mo forever**. See [Free box: Oracle Cloud Always Free](#free-box-oracle-cloud-always-free-arm--0mo)
just below, then follow the same steps. Ideal for letting a few friends test before you commit to a paid box.

## Free box: Oracle Cloud Always Free (ARM) — $0/mo

Oracle's **Always Free** tier includes Ampere **A1 (ARM)** compute that runs forever at no cost —
plenty for this app (even 1 OCPU / 6 GB is comfortable). Use it in place of the Hetzner box; every
other step below is identical.

1. **Sign up** at cloud.oracle.com (a card is required for identity verification — Always-Free
   resources are **not charged**). Pick a home region that has A1 capacity.
2. **Create the instance:** Compute → Instances → Create.
   - Image: **Ubuntu 24.04 (aarch64/ARM)**.
   - Shape: **VM.Standard.A1.Flex** (Ampere) — e.g. **1 OCPU / 6 GB** (well within Always Free).
   - Add your **SSH public key**.
   - **No ingress rules needed** — Cloudflare Tunnel is outbound-only, so leave the default Security
     List closed. Do **not** open 80/443.
3. **Set the ARM build target:** in `deploy/.env`, set `RID=linux-arm64`. The installer and the
   `aspnetcore-runtime-10.0` / `cloudflared` apt packages are already ARM-aware — nothing else changes.
4. Follow **§2 onward** exactly as written (base setup → secrets → Cloudflare Tunnel + Access → ship).
   The only difference from the Hetzner path is the ARM `RID`.

### Letting friends test it — read this first

- **Cloudflare Access is your gate, and it's mandatory.** In §5, set the Access policy to an **email
  allowlist** with your address + your 2–3 friends' emails. Without it, anyone with the URL can open
  the app, **burn your AI quota** (free-tier daily limits on Gemini; real money if you've switched to
  Claude) and your Finnhub credits, and see your data.
- **The app is single-user.** There is **one** shared portfolio / holdings / settings — everyone who
  logs in sees and edits the *same* data. Fine for testing the UX and the "where to invest" flow, but
  your friends can't each track their own portfolio yet (that's a multi-user feature for the real launch).

## What you need

- A Hetzner CPX11 VPS running **Ubuntu 24.04 LTS** (smaller plans work but 2 GB RAM is the practical floor for .NET + EF + Blazor Server + the background worker).
- A domain on Cloudflare (any TLD; even a subdomain of an existing site works).
- Local machine with the .NET 10 SDK, `ssh`, and `rsync`.

## 1. Provision the VPS

In the Hetzner console:

1. New project → Add server.
2. Image: **Ubuntu 24.04**, Type: **CPX11**, Location: pick one near you.
3. SSH key: paste your public key.
4. Networking: **no need to open any ports** — Cloudflare Tunnel is outbound-only.
5. Note the IPv4 address.

## 2. Base setup (one-time, on the VPS)

SSH in as root and run the bundled installer. You can either copy
`deploy/install-vps.sh` over with `scp` or, once you've pushed this repo to GitHub,
curl it directly:

```bash
ssh root@<vps-ip>

# option A: scp the script
# (on your laptop) scp deploy/install-vps.sh deploy/invest-advisor.service root@<vps-ip>:/root/
# (on the VPS)    bash /root/install-vps.sh

# option B: from GitHub
# curl -fsSL https://raw.githubusercontent.com/<you>/InvestAdvisor/main/deploy/install-vps.sh | bash
```

This installs ASP.NET Core 10 runtime + cloudflared, creates the `invest` user,
provisions `/opt/invest-advisor` and `/var/lib/invest-advisor`, drops the systemd
unit, and stubs `/etc/invest-advisor.env`.

## 3. Set the secrets on the VPS

```bash
sudo nano /etc/invest-advisor.env
```

Fill in:

```
GEMINI_API_KEY=...      # free key from https://aistudio.google.com — powers the default AI provider
FINNHUB_API_KEY=...
ANTHROPIC_API_KEY=      # optional — only if you switch to Anthropic Claude (paid) in Settings
SMTP_PASSWORD=          # optional — only if you turn on email alerts
```

Save. The systemd unit reads this file as its environment.

## 4. Set up the Cloudflare Tunnel

In the Cloudflare dashboard:

1. **Zero Trust → Networks → Tunnels → Create a Tunnel** (Cloudflared connector).
2. Name it (e.g. `invest-advisor`), click **Save tunnel**.
3. Cloudflare shows you an install command — copy the **token** at the end.
4. On the VPS, run:
   ```bash
   sudo cloudflared service install <YOUR_TUNNEL_TOKEN>
   ```
   This creates a `cloudflared` systemd unit and starts it. The connector dials out
   to Cloudflare; **no inbound ports**.
5. Back in the Tunnel UI, **Public Hostnames → Add a public hostname**:
   - Subdomain: e.g. `invest`
   - Domain: pick the domain you control
   - Service: **HTTP**, URL: **`localhost:5174`**
   - Save.
6. Wait 30 seconds. `https://invest.<your-domain>` is now provisioned with a
   Cloudflare-managed cert.

## 5. Add Cloudflare Access (the auth layer)

Still in Zero Trust:

1. **Access → Applications → Add an application → Self-hosted**.
2. Application name: `InvestAdvisor`. Subdomain + domain: same as above.
3. Identity providers: pick at least one (Google/GitHub one-tap is easiest).
4. **Add policy** → **Allow** → selector **Emails** → list yourself and your brother.
5. Save.

Now visiting `https://invest.<your-domain>` redirects through Google login first.
Anyone whose email isn't on the allowlist gets a 403.

## 6. Push your first build

On your laptop:

```bash
cp deploy/.env.example deploy/.env
nano deploy/.env       # set SSH_HOST=<vps-ip>
```

Then:

```bash
# Bash / Linux / macOS / Git Bash on Windows:
./deploy/ship.sh

# PowerShell:
./deploy/ship.ps1
```

This builds `InvestAdvisor.Server` for linux-x64 (framework-dependent), rsyncs the
publish output to `/opt/invest-advisor/`, fixes ownership, and runs
`systemctl restart invest-advisor`.

Expect 30–60 seconds for the first build (subsequent rebuilds are 5–10 s, rsync ships
only deltas). Confirm:

```bash
ssh root@<vps-ip> 'systemctl status invest-advisor --no-pager -l | head -20'
ssh root@<vps-ip> 'journalctl -u invest-advisor -n 50 --no-pager'
```

Open `https://invest.<your-domain>` in a browser — log in via Cloudflare Access —
the dashboard loads.

## Operations

| Task | Command |
|---|---|
| Tail app logs | `ssh root@<vps-ip> journalctl -u invest-advisor -f` |
| Restart app | `./deploy/ship.sh --restart-only` |
| Rotate Gemini / Anthropic / Finnhub key | `sudo nano /etc/invest-advisor.env` then `sudo systemctl restart invest-advisor` |
| Backup the SQLite DB | `scp root@<vps-ip>:/var/lib/invest-advisor/app.db ./backups/app-$(date +%F).db` |
| Reset auth (Cloudflare Access) | Zero Trust → Access → Applications → edit policy |
| Take it offline temporarily | `ssh root@<vps-ip> 'sudo systemctl stop invest-advisor'` |

## What this setup is NOT

- **Not multi-user with separate data**. The Blazor Server instance has one SQLite DB
  shared across whoever logs in via Access. If you and your brother want fully isolated
  portfolios you'd run two separate VPSes (or add a `UserId` column everywhere — not v1).
- **Not HA**. One VPS, one process. If Hetzner has an outage your app is down.
- **Not autoscaling**. Single user; fine.
- **Not signed.** Email alerts go out from your SMTP server with whatever From: you set.

## Troubleshooting

**Service won't start.** `journalctl -u invest-advisor -n 100 --no-pager`. Common
causes: missing API key in `/etc/invest-advisor.env`, SQLite path permissions wrong
(`ls -la /var/lib/invest-advisor`).

**Cloudflare shows 502.** The tunnel is connected but the app isn't on `localhost:5174`.
Check `systemctl status invest-advisor` and that `ASPNETCORE_URLS` in the service file
matches the tunnel target.

**Cloudflare Access lets the wrong people in.** Zero Trust → Access → Applications →
your app → check the policy. The `Allow` rule should be selector "Emails" not
"Emails ending in", unless you really want everyone on `@gmail.com`.

**Updates fail at the rsync step.** Make sure `rsync` is installed on the VPS
(`apt install rsync`; install-vps.sh already does this). On Windows clients use
Git Bash's bundled rsync, or `scoop install rsync`.
