#requires -Version 5.1
<#
.SYNOPSIS
Build InvestAdvisor.Server for linux-x64, rsync to the VPS, and restart the service.

.DESCRIPTION
Reads SSH/host config from deploy/.env (copy deploy/.env.example first).
Requires `ssh`, `rsync`, and the .NET SDK on PATH. On Windows 10/11, ssh and rsync
come with Git for Windows (use Git Bash's bundled binaries, or install OpenSSH + rsync
via scoop/winget).

.PARAMETER RestartOnly
Skip the publish + rsync; just bounce the service.
#>
[CmdletBinding()]
param([switch]$RestartOnly)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')

$envFile = Join-Path $scriptDir '.env'
if (-not (Test-Path $envFile)) {
    Write-Error "Missing $envFile. Copy deploy/.env.example to deploy/.env first."
}

$envVars = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*#') { return }
    if ($_ -match '^\s*$') { return }
    if ($_ -match '^\s*([^=]+?)\s*=\s*(.*)\s*$') {
        $envVars[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
    }
}

$sshHost   = $envVars['SSH_HOST']; if (-not $sshHost) { Write-Error 'SSH_HOST must be set in deploy/.env' }
$sshUser   = if ($envVars['SSH_USER']) { $envVars['SSH_USER'] } else { 'root' }
$sshPort   = if ($envVars['SSH_PORT']) { $envVars['SSH_PORT'] } else { '22' }
$remote    = if ($envVars['REMOTE_PATH']) { $envVars['REMOTE_PATH'] } else { '/opt/invest-advisor' }
$service   = if ($envVars['SERVICE_NAME']) { $envVars['SERVICE_NAME'] } else { 'invest-advisor' }
$rid       = if ($envVars['RID']) { $envVars['RID'] } else { 'linux-x64' }

$target = "$sshUser@$sshHost"

if (-not $RestartOnly) {
    Write-Host "==> Publishing InvestAdvisor.Server for $rid" -ForegroundColor Cyan
    dotnet publish (Join-Path $repoRoot 'InvestAdvisor.Server/InvestAdvisor.Server.csproj') `
        -c Release -r $rid --no-self-contained -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { Write-Error 'Publish failed.' }

    $publishDir = Join-Path $repoRoot "InvestAdvisor.Server/bin/Release/net10.0/$rid/publish"
    if (-not (Test-Path $publishDir)) { Write-Error "Publish output not found at $publishDir" }

    Write-Host "==> Stopping service on remote (best-effort)" -ForegroundColor Cyan
    ssh -p $sshPort $target "sudo systemctl stop $service || true"

    Write-Host "==> Rsync publish output to ${target}:$remote" -ForegroundColor Cyan
    # rsync wants forward slashes and a trailing slash on the source.
    $src = $publishDir.Replace('\','/') + '/'
    rsync -az --delete -e "ssh -p $sshPort" $src "${target}:${remote}/"
    if ($LASTEXITCODE -ne 0) { Write-Error 'rsync failed.' }

    Write-Host "==> Fixing ownership" -ForegroundColor Cyan
    ssh -p $sshPort $target "sudo chown -R invest:invest $remote"
}

Write-Host "==> Starting service" -ForegroundColor Cyan
ssh -p $sshPort $target "sudo systemctl start $service && sudo systemctl status $service --no-pager -l | head -15"

Write-Host "==> Done." -ForegroundColor Green
