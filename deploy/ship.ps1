#requires -Version 5.1
<#
.SYNOPSIS
Windows entry point for deploy/ship.sh — publish InvestAdvisor.Server, rsync to the VPS, restart.

.DESCRIPTION
The deploy logic lives once, in ship.sh. This wrapper just runs it under Git Bash (which ships
with Git for Windows and carries ssh + rsync). Reads deploy/.env like the shell script does.

.PARAMETER RestartOnly
Skip the publish + rsync; just bounce the service.
#>
[CmdletBinding()]
param([switch]$RestartOnly)

$ErrorActionPreference = 'Stop'
$bash = Get-Command bash.exe -ErrorAction SilentlyContinue
if (-not $bash) { $bash = Get-Command "$env:ProgramFiles\Git\bin\bash.exe" -ErrorAction SilentlyContinue }
if (-not $bash) { Write-Error 'bash.exe not found — install Git for Windows (ships ssh, rsync and bash).' }

$script = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'ship.sh') -replace '\', '/'
$args = @($script); if ($RestartOnly) { $args += '--restart-only' }
& $bash.Source @args
exit $LASTEXITCODE
