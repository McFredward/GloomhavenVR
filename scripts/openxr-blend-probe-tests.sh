#!/usr/bin/env bash
# Execute the production read-only probe and feature callbacks against fake native delegates.
set -euo pipefail
task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
task_dotnet="${DOTNET:-}"
if [[ -z "$task_dotnet" ]]; then
    task_dotnet="$(command -v dotnet || true)"
fi
if [[ -z "$task_dotnet" && -x "$HOME/.dotnet/dotnet" ]]; then
    task_dotnet="$HOME/.dotnet/dotnet"
fi
if [[ -z "$task_dotnet" ]]; then
    echo "error: .NET SDK not found" >&2
    exit 1
fi
"$task_dotnet" run --project "$task_root/tests/GloomhavenVR.OpenXrBlendProbeTests/GloomhavenVR.OpenXrBlendProbeTests.csproj" -c Release
