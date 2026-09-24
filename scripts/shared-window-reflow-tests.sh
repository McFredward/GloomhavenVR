#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.SharedReflowTests/"*.cs "$repo_root/tests/GloomhavenVR.SharedReflowTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.SharedReflowTests/extract.py" "$repo_root" "$work_dir"
dotnet run --project "$work_dir/GloomhavenVR.SharedReflowTests.csproj" --configuration Release
for mutation in peer-grip absent-metadata unsent-remainder finished-pose stale-grip cached-grip stale-pose; do
    cp "$work_dir/Reflow.cs" "$work_dir/reflow.original"
    cp "$work_dir/Extracted.cs" "$work_dir/extracted.original"
    cp "$work_dir/PoseGate.cs" "$work_dir/pose.original"
    python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]);mutation=sys.argv[2]
name,old,new={
    'peer-grip':('Reflow.cs','if ((CurrentHeldMask(sender, in peer) & 1) != 0) CancelReflow(StoryLocal);',''),
    'absent-metadata':('Reflow.cs','presence.HasSharedWindowMotion ? presence.SharedWindowHeldMask : (byte)7','presence.HasSharedWindowMotion ? presence.SharedWindowHeldMask : (byte)0'),
    'unsent-remainder':('Reflow.cs','if (local.Grab != null && SharedWindowFrame.TryRead(local.Grab,','if (bool.Parse("false") && local.Grab != null && SharedWindowFrame.TryRead(local.Grab,'),
    'finished-pose':('Extracted.cs','if (visiblePose) WritePose(SharedWindowKind.MapStory, StoryLocal, ref SendBuffer[n]);',''),
    'stale-grip':('Reflow.cs','CurrentHeldMask(sender, in peer) & 1','held & 1'),
    'cached-grip':('Reflow.cs','held |= CurrentHeldMask(pair.Key, in peer) != 0;','held |= pair.Value.Held != 0;'),
    'stale-pose':('PoseGate.cs','if (MapStoryLifecycle.MatchesPose(true, senderId, openingEpoch, openingToken))','if (true)'),
}[mutation]
p=root/name;s=p.read_text();assert s.count(old)==1;s=s.replace(old,new);p.write_text(s)
PY
    if dotnet run --project "$work_dir/GloomhavenVR.SharedReflowTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then
        echo "FAIL: shared window reflow regression $mutation escaped coverage" >&2; exit 1
    fi
    case "$mutation" in
        peer-grip) expected='Remote grip must cancel animation ownership';;
        absent-metadata) expected='Missing metadata must not mean released';;
        unsent-remainder) expected='Cancellation must baseline unsent tween remainder to prevent echo';;
        finished-pose) expected='Visible finished story must carry FINISHED plus pose, never OPEN';;
        stale-grip) expected='Prior opening grip must not cancel repeated story animation';;
        cached-grip) expected='Prior opening grip must not block current layout ownership';;
        stale-pose) expected='Prior opening manual pose must not cancel repeated story animation';;
    esac
    if ! rg -Fq "Unhandled exception. System.Exception: $expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    echo "Shared reflow negative control: $mutation rejected."
    mv "$work_dir/reflow.original" "$work_dir/Reflow.cs"
    mv "$work_dir/extracted.original" "$work_dir/Extracted.cs"
    mv "$work_dir/pose.original" "$work_dir/PoseGate.cs"
done
