#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.RewardPoseTests/GloomhavenVR.RewardPoseTests.csproj"
handshake="$repo_root/src/GloomhavenVR/Net/RewardPoseHandshake.cs"
reward="$repo_root/src/GloomhavenVR/Net/Remote/RemoteMapStory.Reward.cs"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
python3 - "$repo_root/src/GloomhavenVR/WorldUI/Modal/PostQuestRewardSync.cs" "$mutation_dir/Identity.cs" <<'PYIDENTITY'
from pathlib import Path
import sys
source=Path(sys.argv[1]).read_text()
a=source.index('    internal static bool MatchesPose(')
b=source.index('    private static IReadOnlyList<int> Participants()',a)
Path(sys.argv[2]).write_text('using GloomhavenVR.Net;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PostQuestRewardSync {\n'+source[a:b]+'}\n')
PYIDENTITY
dotnet run --project "$project" --configuration Release --property:PostQuestIdentitySource="$mutation_dir/Identity.cs"
for mutation in late-opening stale-response missing-reply routing empty-reorder survivor old-map-pose map-pose-cache; do
    python3 - "$handshake" "$reward" "$mutation_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
handshake, reward, out = map(Path,sys.argv[1:4]); name=sys.argv[4]
a,b=handshake.read_text(),reward.read_text()
changes={
'old-map-pose': ('b','if (postQuest && (','if (false && ('),
'map-pose-cache': ('b','RewardPeers.Clear(); RewardCandidates.Clear(); RewardStamp.Clear(); RewardStampAt.Clear();','RewardCandidates.Clear(); RewardStamp.Clear(); RewardStampAt.Clear();'),
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
    if dotnet run --project "$project" --configuration Release --property:PostQuestIdentitySource="$mutation_dir/Identity.cs" --property:HandshakeSource="$mutation_dir/Handshake.cs" --property:RewardSource="$mutation_dir/Reward.cs" >"$mutation_dir/output" 2>&1; then
        echo "ERROR: reward pose mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        old-map-pose) expected='old peer pose cannot bind repeated map reward opening' ;;
        map-pose-cache) expected='new native map opening clears prior same-key pose' ;;
        late-opening|missing-reply) expected='nonparticipant declares inability for requested key' ;;
        stale-response) expected='old generation does not exclude source on reopened chest' ;;
        routing) expected='production explicit no-window response permits actual controller reveal' ;;
        empty-reorder) expected='late empty extras cannot revoke follower role while originating avatar remains live' ;;
        survivor) expected='last surviving native copy becomes a real pose publisher after source departure' ;;
    esac
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Reward pose negative control rejected: $mutation"
done
