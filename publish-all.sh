#!/usr/bin/env bash
# Publishes InvestAdvisor.App for all three target RIDs as self-contained single-file binaries.
# Usage: ./publish-all.sh [Release|Debug]

set -euo pipefail

config="${1:-Release}"
rids=(win-x64 osx-arm64 osx-x64)
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="${script_dir}/InvestAdvisor.App/InvestAdvisor.App.csproj"

for rid in "${rids[@]}"; do
  echo
  echo "==> Publishing for ${rid}"
  dotnet publish "${project}" \
    -c "${config}" \
    -r "${rid}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

  output="${script_dir}/InvestAdvisor.App/bin/${config}/net10.0/${rid}/publish"
  echo "    Output: ${output}"
done

echo
echo "All three publishes complete."
