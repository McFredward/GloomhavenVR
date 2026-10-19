#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
evidence_root="${GHVR_OPTIONS_CLOSE_OUTPUT_ROOT:-$repo_root/.planning/debug/options-close}"
mkdir -p "$evidence_root"
evidence="$(mktemp -d "$evidence_root/run-XXXXXXXX")"
cleanup() {
    local code=$?
    trap - EXIT
    set +e # Receipt/copy failure must not prevent this run's exact cache removal.
    # Preserve this bounded run's source and logs before removing its reaped
    # compiler caches. No shared references or another run are cleanup inputs.
    find "$test_dir" -mindepth 1 -maxdepth 1 -type f \
        \( -name '*.cs' -o -name '*.csproj' -o -name '*.fixture' -o -name '*.mutant' -o -name '*.log' \) \
        -exec cp -- {} "$evidence/" \;
    python3 - "$repo_root" "$evidence" "$code" <<'PY'
import importlib.util, json, pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:3]); code = int(sys.argv[3])
completion = out / 'completion.log'
completion.write_text('Options close focused run exit: ' + str(code) + '\n')
spec = importlib.util.spec_from_file_location('options_retention', root / 'scripts/town_run_retention.py')
retention = importlib.util.module_from_spec(spec); spec.loader.exec_module(retention)
result = retention.cleanup_completed_run('options-close', code, completion, root, process_reaped=True)
(out / 'cleanup.json').write_text(json.dumps(result, indent=2) + '\n')
PY
    rm -rf -- "$test_dir"
    echo "Options close evidence: $evidence"
    exit "$code"
}
trap cleanup EXIT
python3 - "$repo_root" "$test_dir" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
pane = (root / 'src/GloomhavenVR/WorldUI/Options/VROptionsTab.1.Inject.cs').read_text()
modal = (root / 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs').read_text()
toggle = (root / 'src/GloomhavenVR/WorldUI/Options/OptionsToggle.cs').read_text()
def method(source, signature):
    start = source.index('    ' + signature)
    end = source.index('\n    }', start) + len('\n    }')
    return source[start:end]
s = 'using System; using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class VROptionsTab {\n'
for signature in ['internal static bool IsOpen', 'private static void HookHidden(UISubmenuGOWindow window)', 'private static void NotifyHidden(UISubmenuGOWindow source)', 'internal static bool Open(Action? onHidden, RectTransform? pointAt = null)', 'internal static void Close()']:
    s += method(pane, signature) + '\n'
s += '}\ninternal static partial class ModalFallback {\n'
s += method(modal, 'internal static void PrepareModMenuReopen(UIWindow window)') + '\n}\n'
prefix = method(toggle, 'public void Tick()').split('        // PARENT open-state', 1)[0]
assert 'ESCMenu? menu = ResolveMenu();' in prefix and '_spentPressId = NonDominantHold.PressId;' in prefix
s += 'internal sealed partial class OptionsToggle {\n' + prefix + '\n        ReachedNormalMenu = true;\n    }\n}\n'
show = method(pane, 'private static bool ShowStandalone()')
assert show.index('ModalFallback.PrepareModMenuReopen(win);') < show.index('_window.Show();')
(out / 'Close.fixture').write_text(s)
print('VR options close: six production method bindings, actual menu Tick prefix and reopen ordering passed.')
PY
cp "$repo_root/tests/GloomhavenVR.OptionsCloseTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.OptionsCloseTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.OptionsCloseTests.csproj"
dotnet run --disable-build-servers --project "$project" --configuration Release --property:UseSharedCompilation=false --property:OptionsCloseSource="$test_dir/Close.fixture" 2>&1 | tee "$test_dir/production.log"
mutations=(native-order stale-clone stale-open callback-once reopen-gap ordinary-window)
if (( $# > 0 )); then mutations=("$@"); fi
for mutation in "${mutations[@]}"; do
    python3 - "$test_dir" "$mutation" <<'PY'
import pathlib, sys
out = pathlib.Path(sys.argv[1]); s = (out / 'Close.fixture').read_text()
pairs = {
'native-order': ('(IsOpen && source.gameObject.activeSelf)', 'IsOpen'),
'stale-clone': ('!ReferenceEquals(source, _window) || ', ''),
'stale-open': (' || (IsOpen && source.gameObject.activeSelf)', ''),
'callback-once': ('        _onHidden = null;\n        if (pending == null)', '        if (pending == null)'),
'reopen-gap': ('        wp.UserClosing = false;', '        wp.UserClosing = true;'),
'ordinary-window': ('if (!MenuWindowFamily.IsModOwned(window))', 'if (window == null)'),
}
a, b = pairs[sys.argv[2]]; assert a in s
mutated = s.replace(a, b, 1)
(out / 'Close.mutant').write_text(mutated)
(out / ('Close.' + sys.argv[2] + '.mutant')).write_text(mutated)
PY
    if dotnet run --disable-build-servers --project "$project" --configuration Release --property:UseSharedCompilation=false --property:OptionsCloseSource="$test_dir/Close.mutant" > "$test_dir/$mutation.log" 2>&1; then
        cat "$test_dir/$mutation.log"
        echo "FAIL: $mutation escaped VR options close regression." >&2
        exit 1
    fi
    case "$mutation" in
        native-order) expected='native close synchronizes row before Hide returns' ;;
        stale-clone|stale-open) expected='stale close does not consume current callback' ;;
        callback-once) expected='duplicate close invokes callback once' ;;
        reopen-gap) expected='explicit reopen supersedes pending gap-close' ;;
        ordinary-window) expected='ordinary window pending close stays untouched' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/$mutation.log"; then cat "$test_dir/$mutation.log"; exit 1; fi
    echo "VR options close negative control: $mutation rejected."
done
