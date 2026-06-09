#requires -Version 5.1
# Publishes InvestAdvisor.App for all three target RIDs as self-contained single-file binaries.
# Usage: ./publish-all.ps1 [-Configuration Release]

[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$rids = @('win-x64', 'osx-arm64', 'osx-x64')
$project = Join-Path $PSScriptRoot 'InvestAdvisor.App/InvestAdvisor.App.csproj'

foreach ($rid in $rids) {
    Write-Host ""
    Write-Host "==> Publishing for $rid" -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed for $rid"
        exit $LASTEXITCODE
    }

    $output = Join-Path $PSScriptRoot "InvestAdvisor.App/bin/$Configuration/net10.0/$rid/publish"
    Write-Host "    Output: $output" -ForegroundColor Green
}

Write-Host ""
Write-Host "All three publishes complete." -ForegroundColor Green
