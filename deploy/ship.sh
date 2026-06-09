#!/usr/bin/env bash
# Build InvestAdvisor.Server for linux-x64, rsync to the VPS, and restart the service.
# Configuration: copy deploy/.env.example to deploy/.env and fill in SSH details.
#
# Usage:
#   ./deploy/ship.sh                 # build Release + ship
#   ./deploy/ship.sh --restart-only  # don't build; just bounce the service on the VPS
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

env_file="${script_dir}/.env"
if [[ ! -f "${env_file}" ]]; then
  echo "Missing ${env_file}. Copy deploy/.env.example to deploy/.env first."
  exit 1
fi
# shellcheck disable=SC1090
set -a; source "${env_file}"; set +a

: "${SSH_HOST:?SSH_HOST must be set in deploy/.env}"
SSH_USER="${SSH_USER:-root}"
SSH_PORT="${SSH_PORT:-22}"
REMOTE_PATH="${REMOTE_PATH:-/opt/invest-advisor}"
SERVICE_NAME="${SERVICE_NAME:-invest-advisor}"
RID="${RID:-linux-x64}"

ssh_target="${SSH_USER}@${SSH_HOST}"
ssh_opts=(-p "${SSH_PORT}")

restart_only=0
for arg in "$@"; do
  case "$arg" in
    --restart-only) restart_only=1 ;;
  esac
done

if [[ $restart_only -eq 0 ]]; then
  echo "==> Publishing InvestAdvisor.Server for ${RID}"
  dotnet publish "${repo_root}/InvestAdvisor.Server/InvestAdvisor.Server.csproj" \
    -c Release \
    -r "${RID}" \
    --no-self-contained \
    -p:PublishSingleFile=false

  publish_dir="${repo_root}/InvestAdvisor.Server/bin/Release/net10.0/${RID}/publish"
  if [[ ! -d "${publish_dir}" ]]; then
    echo "Publish output not found at ${publish_dir}"
    exit 1
  fi

  echo "==> Stopping service on remote (best-effort)"
  ssh "${ssh_opts[@]}" "${ssh_target}" "sudo systemctl stop ${SERVICE_NAME} || true"

  echo "==> Rsync publish output to ${ssh_target}:${REMOTE_PATH}"
  rsync -az --delete \
    -e "ssh -p ${SSH_PORT}" \
    "${publish_dir}/" \
    "${ssh_target}:${REMOTE_PATH}/"

  echo "==> Fixing ownership"
  ssh "${ssh_opts[@]}" "${ssh_target}" "sudo chown -R invest:invest ${REMOTE_PATH}"
fi

echo "==> Starting service"
ssh "${ssh_opts[@]}" "${ssh_target}" "sudo systemctl start ${SERVICE_NAME} && sudo systemctl status ${SERVICE_NAME} --no-pager -l | head -15"

echo "==> Done."
