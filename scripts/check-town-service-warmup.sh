#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
"$dotnet_bin" run --project "$root/scripts/town-service-warmup-runtime/Warmup.csproj" -c Release -- first
"$dotnet_bin" run --project "$root/scripts/town-service-warmup-runtime/Warmup.csproj" -c Release -- missing
