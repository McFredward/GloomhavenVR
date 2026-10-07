#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.MapRetirementTests/GloomhavenVR.MapRetirementTests.csproj"
source_file="${MAP_RETIREMENT_SOURCE:-$repo_root/src/GloomhavenVR/WorldUI/MapRoom/MapRetirementPrompt.cs}"
native_root="$repo_root/decompiled/GH.Runtime"
if [[ ! -d "$native_root" ]]; then
    common_dir="$(git -C "$repo_root" rev-parse --path-format=absolute --git-common-dir)"
    native_root="$(dirname "$common_dir")/decompiled/GH.Runtime"
fi
python3 - "$repo_root" "$native_root" <<'PY'
from pathlib import Path
import sys
root, native = map(Path, sys.argv[1:])
fixtures = root / 'tests/GloomhavenVR.MapRetirementTests'
source = (root / 'src/GloomhavenVR/WorldUI/MapRoom/MapRetirementPrompt.cs').read_text()
assert 'FindObjects' not in source and '.Resolve(' not in source, 'No scene polling or automatic optional promise resolution'
if native.is_dir():
    widgets = (fixtures / 'NativePromptWidgets.cs').read_text()
    for name in ('UIGuildmasterConfirmActionButton', 'UIGuildmasterConfirmActionPopup',
                 'UIGuildmasterConfirmActionButtonPresenter', 'UIGuildmasterConfirmActionPopupPresenter'):
        original = (native / (name + '.cs')).read_text()
        original = original[original.index('public class '):].strip()
        assert original in widgets, name + ' fixture differs from actual native widget/presenter'
    promises = (native / 'Assets.Script.Misc/CallbackPromise.cs').read_text()
    assert promises in (fixtures / 'NativePromises.cs').read_text(), 'Actual native promise fixture differs'
    def method(text, signature):
        start = text.index('\t' + signature)
        begin = text.index('{', start)
        depth, end = 1, begin + 1
        while depth:
            depth += (text[end] == '{') - (text[end] == '}')
            end += 1
        return text[start:end]
    flow = (fixtures / 'NativeRetirementFlow.cs').read_text()
    for name, signature in (('UIMapMultiplayerController', 'public ICallbackPromise ConfirmRetirement('),
                            ('UIRetirementManager', 'public ICallbackPromise MPConfirmRetire(')):
        assert method((native / (name + '.cs')).read_text(), signature) in flow, name + ' native flow fixture differs'
    print('map retirement: actual native widget, promise and continuation fixtures verified')
else:
    print('map retirement: read-only game reference unavailable; using tracked native fixtures')
PY
dotnet run --project "$project" --configuration Release --property:RetirementSource="$source_file"
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.MapRetirementTests/"*.cs "$mutation_dir/"
cp "$project" "$mutation_dir/"
dotnet restore "$mutation_dir/GloomhavenVR.MapRetirementTests.csproj" --verbosity quiet
for mutation in capture surface auto-confirm stale-callback blocker duplicate restore hit-layer fallback; do
    python3 - "$source_file" "$mutation_dir/Retirement.mutant" "$mutation" <<'PY'
from pathlib import Path
import sys
source, out, name = sys.argv[1:]
text = Path(source).read_text()
mutations = {
    'capture': ('_callback = onConfirmCallback;', '_callback = null;'),
    'surface': ('if (_panel == null)', 'if (_panel != null)'),
    'auto-confirm': ('_callback = onConfirmCallback;', '_callback = onConfirmCallback; onConfirmCallback.Invoke();'),
    'stale-callback': ('ReferenceEquals(_callback, (_source is UIGuildmasterConfirmActionButton\n            ? ButtonCallback : PopupCallback)?.GetValue(_source))', 'true'),
    'blocker': ('|| blocker.IsBlock', '|| false'),
    'duplicate': ('_lastClickFrame == Time.frameCount', 'false'),
    'restore': ('CanvasConversion.Release(_panel);', '{}'),
    'hit-layer': ('_click.layer = target.gameObject.layer;', '_click.layer = 0;'),
    'fallback': ('ScreenFallbackWanted => _presentationEnabled && _failed', 'ScreenFallbackWanted => _presentationEnabled && false'),
}
before, after = mutations[name]
assert text.count(before) == (2 if name == 'hit-layer' else 1), (name, before)
Path(out).write_text(text.replace(before, after))
PY
    case "$mutation" in
        capture|surface) expected='early native Show survives later map activation' ;;
        auto-confirm) expected='early native Show survives later map activation' ;;
        stale-callback) expected='stale native callback identity rejects queued click' ;;
        blocker) expected='console native navigation tags gate actual press PartyPanel' ;;
        duplicate) expected='console same-frame duplicate dispatch is bounded' ;;
        restore) expected='native hide restores original HUD hierarchy' ;;
        hit-layer) expected='console hit surface inherits rendered layer' ;;
        fallback) expected='active map camera failure exposes original pending HUD' ;;
    esac
    if dotnet run --project "$mutation_dir/GloomhavenVR.MapRetirementTests.csproj" --configuration Release \
        --no-restore --property:RetirementSource="$mutation_dir/Retirement.mutant" > "$mutation_dir/$mutation.log" 2>&1; then
        echo "ERROR: retirement negative control survived: $mutation" >&2
        exit 1
    fi
    if ! rg -qF "ASSERT: $expected" "$mutation_dir/$mutation.log"; then
        cat "$mutation_dir/$mutation.log" >&2
        echo "ERROR: retirement negative control failed for an unexpected reason: $mutation" >&2
        exit 1
    fi
done
echo 'map retirement: 9 causal negative controls rejected'
