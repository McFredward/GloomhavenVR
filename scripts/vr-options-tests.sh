#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
python3 - "$repo_root" "$test_dir" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
base = root / 'src/GloomhavenVR/WorldUI'
catch = (base / 'Modal/ModalFallback.10.CatchAll.cs').read_text()
row = (base / 'Options/VRMenuEntry.cs').read_text()
family = (base / 'Modal/MenuWindowFamily.cs').read_text()
pane = (base / 'Options/VROptionsTab.1.Inject.cs').read_text()
def method(source, signature):
    start = source.index('    ' + signature)
    # All methods extracted here end at the class indentation; nested blocks remain intact.
    end = source.index('\n    }', start) + len('\n    }')
    return source[start:end]
early = catch[catch.index('            if (!hoverCard && !MenuWindowFamily.IsModOwned(window)'):]
early = early[:early.index('\n            if (!CatchAllEligible')]
early = early.replace('continue;', 'return false;')
assert catch.index('if (!CatchAllEligible(window))') < catch.index('if (!AllowCatchAllRepeat(window, hoverCard))') < catch.index('OpenWindows.Add(window);', catch.index('if (!AllowCatchAllRepeat(window, hoverCard))'))
assert 'if (!MenuWindowFamily.IsModOwned(window) && CatchAllWarned.Add(window.name))' in catch
assert 'if (!InjectionRetryReady(host))' in pane and 'if (_degraded)\n            return;' not in method(pane, 'internal static void Tick()')
assert 'Shutdown();' in method(pane, 'private static void Degrade(UIOptionsWindow host, string reason)')
assert 'BindRow(clone, mainMenu: false);' in method(row, 'private static void Inject(ESCMenu host)')
assert 'BindRow(clone, mainMenu: true);' in method(row, 'private static void InjectMain(UIMainOptionsMenu menu)')
assert 'SetMainFocused(' not in row and 'SetFocused(host,' not in row
print('VR options: seven production source bindings passed.')
s = 'using System;\nusing UnityEngine;\nusing UnityEngine.SceneManagement;\nusing GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\n'
s += 'internal static partial class ModalFallback {\n'
s += method(catch, 'private static bool AllowCatchAllRepeat(UIWindow window, bool hoverCard)')
s += '\nprivate static bool PassEarlySuppression(UIWindow window, bool hoverCard) {\n' + early + '\nreturn true;\n}\n}\n'
s += 'internal static partial class MenuWindowFamily {\n' + method(family, 'internal static bool IsModOwned(UIWindow? window)') + '\n}\n'
s += 'internal static partial class VRMenuEntry {\n'
for signature in ['internal static void Tick()', 'internal static void LateTick()', 'private static void TickPauseMenu()', 'private static void TickMainMenu()', 'private static void MaintainRowAvailability(UIMainMenuOption? row)', 'private static void BindRow(UIMainMenuOption row, bool mainMenu)', 'private static void ClearRow(UIMainMenuOption? row)', 'private static void TickRowLatch(bool open)', 'private static void ClearRivals()']:
    s += method(row, signature) + '\n'
s += '}\ninternal static partial class VROptionsTab {\n'
for signature in ['private static bool InjectionRetryReady(UIOptionsWindow host)', 'private static void Degrade(UIOptionsWindow host, string reason)', 'private static void NotifyHidden(UISubmenuGOWindow source)']:
    s += method(pane, signature) + '\n'
s += '}\n'
(out / 'Options.fixture').write_text(s)
PY
cp "$repo_root/tests/GloomhavenVR.VROptionsTests/"*.cs "$test_dir/"
cp "$repo_root/tests/GloomhavenVR.VROptionsTests/"*.csproj "$test_dir/"
project="$test_dir/GloomhavenVR.VROptionsTests.csproj"
dotnet run --project "$project" --configuration Release --property:OptionsSource="$test_dir/Options.fixture"
for mutation in mod-count mod-early registered-identity row-visibility row-enabled row-focus entry-recovery seat-recovery injection-recovery injection-cleanup stale-clone stale-close discovery-budget known-host pause-retry native-focus recursive-close latch-immediate rival-close; do
    python3 - "$test_dir" "$mutation" <<'PY'
import pathlib, sys
out = pathlib.Path(sys.argv[1]); s = (out / 'Options.fixture').read_text()
mutations = {
    'mod-count': ('if (hoverCard || MenuWindowFamily.IsModOwned(window))', 'if (hoverCard)'),
    'mod-early': ('!hoverCard && !MenuWindowFamily.IsModOwned(window)', '!hoverCard'),
    'registered-identity': ('if (ReferenceEquals(ModOwned[i], window))', 'if (ReferenceEquals(ModOwned[i], null))'),
    'row-visibility': ('row.gameObject.SetActive(true);', 'row.gameObject.SetActive(false);'),
    'row-enabled': ('row.IsInteractable = true;', 'row.IsInteractable = false;'),
    'row-focus': ('row.SetFocused(true);', 'row.SetFocused(false);'),
    'entry-recovery': ('_retryAfter = Time.unscaledTime + 2f;', '_retryAfter = float.MaxValue;'),
    'seat-recovery': ('_retryAfter = Time.unscaledTime + 2f;', '_retryAfter = float.MaxValue;'),
    'injection-recovery': ('ReferenceEquals(host, _failedHost) && Time.unscaledTime < _nextInjectionRetry', 'ReferenceEquals(host, _failedHost)'),
    'injection-cleanup': ('        Shutdown();', '        // skipped cleanup'),
    'stale-clone': ('!ReferenceEquals(source, _window) || (IsOpen && source.gameObject.activeSelf)', '(IsOpen && source.gameObject.activeSelf)'),
    'stale-close': ('!ReferenceEquals(source, _window) || (IsOpen && source.gameObject.activeSelf)', '!ReferenceEquals(source, _window)'),
    'discovery-budget': ('if (!VROptionsTab.CanOpen || Time.unscaledTime < _nextMainScan)', 'if (!VROptionsTab.CanOpen) _mainScansLeft = 0;\n        if (!VROptionsTab.CanOpen || Time.unscaledTime < _nextMainScan)'),
    'known-host': ('if (_mainHost != null)', 'if (_mainHost != null && _mainScansLeft < 0)'),
    'native-focus': ('        row.Init(', '        _host?.SetFocused(false);\n        row.Init('),
    'recursive-close': ('            row.SetSelected(false);', '            row.Deselect();'),
    'latch-immediate': ('        if (open)\n            return;\n        UIMainMenuOption? stale', '        if (!open) return;\n        UIMainMenuOption? stale'),
    'rival-close': ('                row.Deselect();', '                row.SetSelected(false);'),
    'pause-retry': ('ReferenceEquals(host, _pauseInjectFailed) && Time.unscaledTime < _pauseRetryAfter', 'ReferenceEquals(host, _pauseInjectFailed)'),
}
a,b=mutations[sys.argv[2]]
if sys.argv[2] == 'seat-recovery':
    at = s.index('internal static void LateTick()'); s = s[:at] + s[at:].replace(a,b,1)
else:
    assert a in s
    s=s.replace(a,b,1)
(out / 'Options.mutant').write_text(s)
PY
    if dotnet run --project "$project" --configuration Release --property:OptionsSource="$test_dir/Options.mutant" > "$test_dir/mutant.log" 2>&1; then
        cat "$test_dir/mutant.log"
        echo "FAIL: $mutation escaped VR options regression tests." >&2
        exit 1
    fi
    case "$mutation" in
        mod-count|mod-early) expected='settings survive preexisting suppression' ;;
        registered-identity) expected='registered identity survives' ;;
        row-visibility) expected='both settings doors regain visibility' ;;
        row-enabled) expected='both settings doors remain enabled' ;;
        row-focus) expected='settings door is visibly focused' ;;
        entry-recovery) expected='entry recovers after transient failure' ;;
        seat-recovery) expected='seat recovers after transient failure' ;;
        injection-recovery) expected='same host retries after cooldown' ;;
        injection-cleanup) expected='partial injection clone is cleaned before retry' ;;
        stale-clone) expected='old clone cannot consume current close callback' ;;
        stale-close) expected='delayed close cannot deselect reopened pane' ;;
        discovery-budget) expected='late pane recovery creates both menu entries' ;;
        known-host) expected='lost rows are recreated on known hosts' ;;
        native-focus) expected='VR toggle preserves native focus and disabled state' ;;
        recursive-close) expected='X resets toggle without recursive close' ;;
        latch-immediate) expected='closed window clears both rows immediately' ;;
        rival-close) expected='main toggle closes previous native window' ;;
        pause-retry) expected='failed row construction retries same native hosts' ;;
    esac
    if ! rg -qF "$expected" "$test_dir/mutant.log"; then
        cat "$test_dir/mutant.log"
        exit 1
    fi
    echo "VR options negative control: $mutation rejected."
done
