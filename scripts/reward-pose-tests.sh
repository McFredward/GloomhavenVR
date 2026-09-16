#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.RewardPoseTests/GloomhavenVR.RewardPoseTests.csproj"
handshake="$repo_root/src/GloomhavenVR/Net/RewardPoseHandshake.cs"
reward="$repo_root/src/GloomhavenVR/Net/Remote/RemoteMapStory.Reward.cs"
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in late-opening stale-response missing-reply routing empty-reorder survivor; do
    python3 - "$handshake" "$reward" "$mutation_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
handshake, reward, out = map(Path,sys.argv[1:4]); name=sys.argv[4]
a,b=handshake.read_text(),reward.read_text()
changes={
'late-opening': ('a','internal bool LocalDeclined(uint key) => key != 0 && _declinedKeys.Contains(key);','internal bool LocalDeclined(uint key) => false;'),
'stale-response': ('a','_generation == decline.Generation','true'),
'missing-reply': ('a','if (request.Key == 0 || (request.Key == _key && !_declinedKeys.Contains(request.Key))) continue;','if (request.Key != 0) continue;'),
'routing': ('b','RewardHandshake.PeerDeclined(peer, key)','false'),
'empty-reorder': ('a','key != _key && !Requested(key)','!Requested(key)'),
'survivor': ('b','key == 0 || RewardKey != key || RewardShowcasePlacement.LocalPlacementFailed','key == 0 || RewardKey != key || RewardShowcasePlacement.LocalPlacementFailed || RewardHandshake.LocalDeclined(key)')}
target,old,new=changes[name]; source=a if target=='a' else b
assert old in source
source=source.replace(old,new)
(out/'Handshake.cs').write_text(source if target=='a' else a)
(out/'Reward.cs').write_text(source if target=='b' else b)
PY
    if dotnet run --project "$project" --configuration Release --property:HandshakeSource="$mutation_dir/Handshake.cs" --property:RewardSource="$mutation_dir/Reward.cs" >"$mutation_dir/output" 2>&1; then
        echo "ERROR: reward pose mutation survived: $mutation" >&2; exit 1
    fi
    if ! rg -q 'Unhandled exception.*System.Exception' "$mutation_dir/output"; then cat "$mutation_dir/output"; exit 1; fi
    echo "Reward pose negative control rejected: $mutation"
done
