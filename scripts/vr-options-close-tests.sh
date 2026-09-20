#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
python3 - "$repo_root" "$test_dir" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
pane = (root / 'src/GloomhavenVR/WorldUI/Options/VROptionsTab.1.Inject.cs').read_text()
modal = (root / 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.7.Close.cs').read_text()
def method(source, signature):
    start = source.index('    ' + signature)
    end = source.index('\n    }', start) + len('\n    }')
    return source[start:end]
s = 'using System;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class VROptionsTab {\n'
for signature in ['internal static bool IsOpen', 'private static void HookHidden(UISubmenuGOWindow window)', 'private static void NotifyHidden(UISubmenuGOWindow source)', 'internal static bool Open(Action? onHidden, RectTransform? pointAt = null)', 'internal static void Close()']:
    s += method(pane, signature) + '\n'
s += '}\ninternal static partial class ModalFallback {\n'
s += method(modal, 'internal static void PrepareModMenuReopen(UIWindow window)') + '\n}\n'
show = method(pane, 'private static bool ShowStandalone()')
assert show.index('ModalFallback.PrepareModMenuReopen(win);') < show.index('_window.Show();')
(out / 'Close.fixture').write_text(s)
print('VR options close: six production method bindings and reopen call ordering passed.')
PY
cp "$repo_root/tests/GloomhavenVR.OptionsCloseTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.OptionsCloseTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.OptionsCloseTests.csproj"
dotnet run --project "$project" --configuration Release --property:OptionsCloseSource="$test_dir/Close.fixture"
for mutation in native-order stale-clone stale-open callback-once reopen-gap ordinary-window; do
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
a, b = pairs[sys.argv[2]]; assert a in s; (out / 'Close.mutant').write_text(s.replace(a, b, 1))
PY
    if dotnet run --project "$project" --configuration Release --property:OptionsCloseSource="$test_dir/Close.mutant" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
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
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then cat "$test_dir/mutant.log"; exit 1; fi
    echo "VR options close negative control: $mutation rejected."
done
