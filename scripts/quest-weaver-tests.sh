#!/usr/bin/env bash
# Execute source-derived static integration fixtures; no owned game input needed.
set -euo pipefail
quest_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
dotnet run --project "$quest_repo_root/tests/QuestWeaver.Tests/QuestWeaver.Tests.csproj" --configuration Release
