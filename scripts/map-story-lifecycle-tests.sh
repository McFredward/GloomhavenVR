#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MapStoryLifecycleTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.MapStoryLifecycleTests/"*.csproj "$test_dir/"
ledger="$repo_root/src/GloomhavenVR/Net/MapStoryOpeningLedger.cs"
codec="$repo_root/src/GloomhavenVR/Net/MapStoryLifecycleState.cs"
project="$test_dir/GloomhavenVR.MapStoryLifecycleTests.csproj"
dotnet run --project "$project" --configuration Release --property:LedgerSource="$ledger" --property:CodecSource="$codec"
for mutation in no-terminal-latch no-recipient-check no-predecessor-check no-one-to-one no-run-match no-epoch-retirement no-page-revision no-recipient-total no-final-predecessor no-pose-token; do
    python3 - "$ledger" "$test_dir/mutant.fixture" "$mutation" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text()
pairs = {
 'no-terminal-latch': (' || current.TerminalApplied', ''),
 'no-recipient-check': (' || !Has(remote.Participants, localId)', ''),
 'no-predecessor-check': ('if (remote.PreviousToken != 0 && !used.Contains(remote.PreviousToken)', 'if (false && remote.PreviousToken != 0 && !used.Contains(remote.PreviousToken)'),
 'no-one-to-one': ('used.Contains(remote.Token) || ', ''),
 'no-run-match': ('remote.SemanticKey != local.State.SemanticKey || ', ''),
 'no-epoch-retirement': (' && retired.Contains(epoch)', ' && false'),
 'no-page-revision': ('if (entry.State.Bidirectional) ++entry.State.PageRevision;', ''),
 'no-recipient-total': ('|| predecessor.Participants.Length < predecessor.TotalParticipants', ''),
 'no-final-predecessor': ('|| !predecessor.Finished', ''),
 'no-pose-token': ('knownToken == token', 'true'),
}
before, after = pairs[sys.argv[3]]
assert before in source
Path(sys.argv[2]).write_text(source.replace(before, after, 1))
PY
    if dotnet run --project "$project" --configuration Release --property:LedgerSource="$test_dir/mutant.fixture" --property:CodecSource="$codec" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped map story lifecycle regression." >&2
        exit 1
    fi
    case "$mutation" in
        no-terminal-latch) expected='terminal callback applies once before native finish returns' ;;
        no-recipient-check) expected='late joiner cannot claim completed history from before participation' ;;
        no-predecessor-check) expected='reordered history must not swap identical completed and live occurrences' ;;
        no-one-to-one) expected='old same-content completion cannot bind a reopened native occurrence' ;;
        no-run-match) expected='prior public run cannot advance a new run with identical text' ;;
        no-epoch-retirement) expected='retired epoch replay cannot complete a new opening' ;;
        no-page-revision) expected='native previous-page action synchronizes backwards' ;;
        no-recipient-total) expected='partial recipient history cannot close the live successor' ;;
        no-final-predecessor) expected='live predecessor cannot prove final absence before future recipient enrollment' ;;
        no-pose-token) expected='unbound live token cannot move same-content story' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Map story lifecycle negative control: $mutation rejected."
done

python3 - "$repo_root" <<'PYBIND'
from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/GloomhavenVR/Net/Remote'
for name, family in [('RemoteMapStory.cs', 'true'), ('RemoteStorySync.cs', 'false')]:
    source = (root / name).read_text()
    assert 'MapStoryLifecycle.PoseOpening(in p,' in source, name + ': pose must capture same-packet opening provenance'
    pose = source[source.index('private static void ResolvePose('):]
    gate = f'MapStoryLifecycle.MatchesPose({family}, kv.Key, s.OpeningEpoch, s.OpeningToken)'
    assert gate in pose and pose.index(gate) < pose.index('bestPeer = kv.Key'), name + ': opening gate must precede ownership election'
source = (root / 'RemoteMapStory.cs').read_text()
case = source[source.index('case NetProtocol.SharedWindowKindMapStory:'):source.index('case NetProtocol.SharedWindowKindQuestConfirm:')]
assert 'if (MapStoryLifecycle.MatchesPose(true, senderId, openingEpoch, openingToken))\n                            ObserveReflowPose' in case
reflow = (root / 'RemoteMapStory.Reflow.cs').read_text()
assert 'MapStoryLifecycle.PoseOpening(in presence, StoryLocal.Key,' in reflow
assert 'CurrentHeldMask(sender, in peer) & 1' in reflow
assert 'held |= CurrentHeldMask(pair.Key, in peer) != 0;' in reflow
assert 'peer.Held & ~NetProtocol.SharedWindowMotionMapStoryBit' in reflow
assert 'if ((held & 2) != 0) CancelReflow(QuestLocal);' in reflow
assert 'if ((held & 4) != 0) CancelReflow(EncounterLocal);' in reflow
print('Story pose binding source checks passed.')
PYBIND
