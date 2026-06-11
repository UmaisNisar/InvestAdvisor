#!/usr/bin/env bash
# Run this script once on a fresh Ubuntu 24.04 LTS VPS as root.
# It installs the .NET 10 ASP.NET Core runtime, creates the service user and dirs,
# installs cloudflared, and registers the systemd unit. It does NOT start the service
# yet — that happens after you ship the first build.
#
# Usage on the VPS:
#   curl -fsSL https://raw.githubusercontent.com/<your>/InvestAdvisor/main/deploy/install-vps.sh | sudo bash
# Or copy this file there and run: sudo bash install-vps.sh
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run as root (or via sudo)."
  exit 1
fi

UBUNTU_CODENAME="$(lsb_release -cs)"
UBUNTU_VERSION="$(lsb_release -rs)"

echo "==> Updating apt and installing base packages"
apt-get update
apt-get install -y curl wget gnupg lsb-release ca-certificates rsync

echo "==> Installing Microsoft package signing repo"
wget -q "https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
rm -f /tmp/packages-microsoft-prod.deb
apt-get update

echo "==> Installing ASP.NET Core 10 runtime"
apt-get install -y aspnetcore-runtime-10.0

echo "==> Installing cloudflared apt repo + binary"
mkdir -p --mode=0755 /usr/share/keyrings
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg | tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared ${UBUNTU_CODENAME} main" | tee /etc/apt/sources.list.d/cloudflared.list
apt-get update
apt-get install -y cloudflared

echo "==> Creating service user and directories"
if ! id -u invest >/dev/null 2>&1; then
  useradd --system --no-create-home --shell /usr/sbin/nologin invest
fi
install -d -o invest -g invest -m 0755 /opt/invest-advisor
install -d -o invest -g invest -m 0750 /var/lib/invest-advisor

echo "==> Installing /etc/invest-advisor.env stub"
if [[ ! -f /etc/invest-advisor.env ]]; then
  cat > /etc/invest-advisor.env <<'EOF'
# Fill these in. systemd reads this file as the env for the service.
# GEMINI_API_KEY powers the free default AI provider (free key at https://aistudio.google.com).
GEMINI_API_KEY=
# Optional: only needed if you switch the AI provider to Anthropic Claude (paid) in Settings.
ANTHROPIC_API_KEY=
FINNHUB_API_KEY=
SMTP_PASSWORD=
EOF
  chown root:root /etc/invest-advisor.env
  chmod 600 /etc/invest-advisor.env
  echo "    Created /etc/invest-advisor.env (chmod 600). Fill in your keys."
else
  echo "    /etc/invest-advisor.env already exists — leaving it untouched."
fi

echo "==> Installing systemd unit"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "${script_dir}/invest-advisor.service" ]]; then
  cp "${script_dir}/invest-advisor.service" /etc/systemd/system/invest-advisor.service
else
  echo "    invest-advisor.service not next to this script. Copy it manually to /etc/systemd/system/."
fi
systemctl daemon-reload
systemctl enable invest-advisor.service

cat <<EOF

============================================================
VPS base setup complete. Next steps:

  1. Fill in /etc/invest-advisor.env with your real API keys.
  2. Set up the Cloudflare Tunnel:
       https://dash.cloudflare.com → Zero Trust → Networks → Tunnels
       Create a tunnel, copy its token, then on this VPS run:

         sudo cloudflared service install <YOUR_TUNNEL_TOKEN>

       In the Cloudflare dashboard, add a Public Hostname for the
       tunnel pointing to:   http://localhost:5174
  3. (Recommended) Cloudflare Zero Trust → Access → Applications →
       Add Application → Self-hosted → bind your hostname → policy:
       email-based allowlist (yourself, your brother).
  4. From your dev machine, run deploy/ship.sh (or ship.ps1) to push
     the first build. That will rsync to /opt/invest-advisor and run
     'systemctl restart invest-advisor'.

The service is enabled but not started; first start happens after ship.
============================================================
EOF
