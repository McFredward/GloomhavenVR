#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${STORY_COMPLETION_SOURCE_ROOT:-$repo_root}"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.StoryCompletionTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.StoryCompletionTests/"*.csproj "$test_dir/"
python3 "$repo_root/tests/GloomhavenVR.StoryCompletionTests/BindProduction.py" "$source_root" "$test_dir"
project="$test_dir/GloomhavenVR.StoryCompletionTests.csproj"
run_fixture() {
    dotnet run --project "$project" --configuration Release \
        --property:StorySource="$test_dir/NativeStoryWindow.fixture" \
        --property:LifecycleSource="$test_dir/Lifecycle.fixture" \
        --property:TermSource="$test_dir/MandatoryDecisionTerm.fixture" \
        --property:AnchorSource="$test_dir/StorySpawnAnchor.fixture"
}
run_fixture
cp "$test_dir/NativeStoryWindow.fixture" "$test_dir/NativeStoryWindow.original"
cp "$test_dir/Lifecycle.fixture" "$test_dir/Lifecycle.original"
cp "$test_dir/StorySpawnAnchor.fixture" "$test_dir/StorySpawnAnchor.original"
for mutation in stale-membership resurrect-alpha visual-completion wrong-identity scenario-omitted reopen-lost poll-resurrection reconversion stale-anchor live-anchor unrelated-anchor; do
    cp "$test_dir/NativeStoryWindow.original" "$test_dir/NativeStoryWindow.fixture"
    cp "$test_dir/Lifecycle.original" "$test_dir/Lifecycle.fixture"
    cp "$test_dir/StorySpawnAnchor.original" "$test_dir/StorySpawnAnchor.fixture"
    python3 - "$test_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
pairs = {
    'stale-membership': ('Lifecycle', '&& !NativeStoryWindow.IsCompleted(wp.Window)', ''),
    'resurrect-alpha': ('Lifecycle', 'if (NativeStoryWindow.IsCompleted(wp.Window)) return false;', ''),
    'visual-completion': ('NativeStoryWindow', '!window.IsOpen', '!window.IsVisible'),
    'wrong-identity': ('NativeStoryWindow', '&& IsStory(window)', ''),
    'scenario-omitted': ('NativeStoryWindow', '|| (Singleton<StoryController>.IsInitialized', '|| (false && Singleton<StoryController>.IsInitialized'),
    'reopen-lost': ('NativeStoryWindow', '&& !window.IsOpen', ''),
    'poll-resurrection': ('Lifecycle', 'if (NativeStoryWindow.IsCompleted(window))\n            return;', ''),
    'reconversion': ('Lifecycle', '|| NativeStoryWindow.IsCompleted(window)', '|| false'),
    'stale-anchor': ('StorySpawnAnchor', 'SharedAnchorSpent.Remove(kind);', ''),
    'live-anchor': ('StorySpawnAnchor', 'existing.Panel.IsAlive', '!existing.Panel.IsAlive'),
    'unrelated-anchor': ('StorySpawnAnchor', 'SharedAnchorSpent.Remove(kind);', 'SharedAnchorSpent.Clear();'),
}
name, before, after = pairs[sys.argv[2]]
path = root / (name + '.fixture')
s = path.read_text()
assert before in s, 'negative control did not match production code'
path.write_text(s.replace(before, after, 1))
PY
    if run_fixture > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped story completion regression." >&2
        exit 1
    fi
    case "$mutation" in
        stale-membership) expected='closed final story releases even with stale membership and positive alpha' ;;
        resurrect-alpha) expected='completed story cannot be made visible again' ;;
        visual-completion) expected='live pages are never completed' ;;
        wrong-identity) expected='ordinary destination and permanent panel keep their identity' ;;
        scenario-omitted) expected='both native controllers identify their exact window' ;;
        reopen-lost) expected='live pages are never completed' ;;
        poll-resurrection) expected='closed story alpha cannot reenter polling' ;;
        reconversion) expected='stale sampled story cannot reconvert after release' ;;
        stale-anchor) expected='fresh story reclaims its shared spawn seat after prior movement' ;;
        live-anchor) expected='live story frame retains its spent anchor and manual pose' ;;
        unrelated-anchor) expected='new story never rearms other shared windows' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "Story completion negative control: $mutation rejected."
done
