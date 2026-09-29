#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-}"
if [[ -z "$dotnet_bin" ]]; then
    dotnet_bin="$HOME/.dotnet/dotnet"
    [[ -x "$dotnet_bin" ]] || dotnet_bin=dotnet
fi
"$dotnet_bin" run --project "$root/scripts/town-service-warmup-runtime/Warmup.csproj" -c Release -- first
"$dotnet_bin" run --project "$root/scripts/town-service-warmup-runtime/Warmup.csproj" -c Release -- missing
