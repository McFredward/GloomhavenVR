#!/usr/bin/env bash
# Focused end-to-end check for repeatable Steam Frame setup on a synthetic account.
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
mkdir -p -- "$root/.planning/debug/frame672-installer"
scratch="$(python3 -c 'import sys,tempfile; print(tempfile.mkdtemp(prefix="run-", dir=sys.argv[1]))' "$root/.planning/debug/frame672-installer")"
echo "evidence: $scratch"
fake_steam_pid=""
finish() {
    if [[ -n "$fake_steam_pid" ]]; then
        kill "$fake_steam_pid" 2>/dev/null || true
        wait "$fake_steam_pid" 2>/dev/null || true
    fi
}
trap finish EXIT

export HOME="$scratch/home with spaces"
export XDG_DATA_HOME="$HOME/.local/share"
steam_root="$XDG_DATA_HOME/Steam"
game="$steam_root/steamapps/common/Gloomhaven"
account="$steam_root/userdata/123/config"
setup="$game/BepInEx/plugins/GloomhavenVR/FrameSetup"
mkdir -p -- "$game/BepInEx/patchers/GloomhavenVR" "$game/GH_Data" "$account" "$scratch/bin" "$setup"
: > "$game/GH.exe"
: > "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
printf 'wait-for-native-debugger=0\n' > "$game/GH_Data/boot.config"
cat > "$account/localconfig.vdf" <<'EOF'
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "Apps"
                {
                    "780290"
                    {
                        "LaunchOptions" "MANGOHUD=1 %command%"
                    }
                }
            }
        }
    }
}
EOF
cp -- "$root/scripts/install-steam-frame.sh" "$setup/install-steam-frame.sh"
cp -- "$root/scripts/steam-frame-config.py" "$setup/steam-frame-config.py"
cp -- "$root/scripts/frame-boot-config.py" "$setup/frame-boot-config.py"
cp -- "$root/GloomhavenVR-Setup.desktop" "$setup/GloomhavenVR-Setup.desktop"
mkdir -p -- "$setup/SteamArtwork"
for artwork in library_600x900.png library_header.png library_hero.png logo.png icon.png; do
    printf 'test artwork: %s\n' "$artwork" > "$setup/SteamArtwork/$artwork"
done
printf 'obsolete\n' > "$setup/GloomhavenVR-steam-logo.png"
printf 'obsolete\n' > "$setup/GloomhavenVR-steam-icon.png"
legacy=(install-steam-frame.sh steam-frame-config.py frame-boot-config.py
    GloomhavenVR-Setup.desktop
    GloomhavenVR-steam-logo.png GloomhavenVR-steam-icon.png)
for obsolete in "${legacy[@]}"; do
    printf 'obsolete\n' > "$game/$obsolete"
done

marker="$game/BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker"
launcher="$XDG_DATA_HOME/GloomhavenVR/launch-steam-frame.sh"
shortcuts="$account/shortcuts.vdf"
boot="$game/GH_Data/boot.config"
vr_preferences="$game/vrpreferences.json"

bash "$setup/install-steam-frame.sh" --dry-run > "$scratch/dry-run.log"
[[ ! -e "$marker" && ! -e "$launcher" && ! -e "$shortcuts" && ! -e "$vr_preferences" ]]
[[ "$(cat "$boot")" == 'wait-for-native-debugger=0' ]]

# Dolphin supplies %k to a desktop entry. Gio exercises the same command without
# a graphical shell and may run it asynchronously on some distributions.
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$setup/GloomhavenVR-Setup.desktop"
fi
if command -v gio >/dev/null 2>&1; then
    sed -i 's/^Terminal=true$/Terminal=false/' "$setup/GloomhavenVR-Setup.desktop"
    (cd -- "$game" && gio launch "$setup/GloomhavenVR-Setup.desktop") > "$scratch/desktop.log" 2>&1
    for ((attempt = 0; attempt < 50; attempt++)); do
        if [[ -f "$marker" && -x "$launcher" && -f "$shortcuts" && -f "$vr_preferences" &&
            ! -e "$game/GloomhavenVR-Setup.desktop" ]]; then
            break
        fi
        sleep 0.1
    done
    if [[ ! -f "$marker" || ! -x "$launcher" || ! -f "$shortcuts" ]]; then
        cat "$scratch/desktop.log" >&2
        echo 'error: desktop entry did not complete Frame setup' >&2
        exit 1
    fi
else
    bash "$setup/install-steam-frame.sh" > "$scratch/install.log"
fi

[[ -f "$marker" && -x "$launcher" && -f "$shortcuts" && -f "$vr_preferences" ]]
python3 - "$vr_preferences" <<'PY'
import json
from pathlib import Path
import sys
assert json.loads(Path(sys.argv[1]).read_text()) == {"steam_frame": {"preferResolution": 3408}}
PY
for ((attempt = 0; attempt < 50; attempt++)); do
    rg -q 'Setup state: complete' "$setup/steam-frame-setup.log" 2>/dev/null && break
    sleep 0.1
done
rg -q 'Selected Steam files need repair: no' "$setup/steam-frame-setup.log"
rg -q 'GloomhavenVR shortcut before this check: current' "$setup/steam-frame-setup.log"
[[ -f "$setup/steam-frame-setup.log.previous" ]]
for obsolete in "${legacy[@]}"; do
    [[ ! -e "$game/$obsolete" ]]
done
[[ ! -e "$setup/GloomhavenVR-steam-logo.png" && ! -e "$setup/GloomhavenVR-steam-icon.png" ]]
# Some desktop environments leave %k empty. Even with a legacy helper in the
# game root, the launcher must resolve the nested setup from the Steam path.
if command -v gio >/dev/null 2>&1; then
    no_path_desktop="$setup/GloomhavenVR-Setup-NoPath.desktop"
    sed 's/ bash %k$/ bash/' "$setup/GloomhavenVR-Setup.desktop" > "$no_path_desktop"
    printf 'obsolete\n' > "$game/install-steam-frame.sh"
    (cd -- "$game" && gio launch "$no_path_desktop") > "$scratch/no-path-desktop.log" 2>&1
    for ((attempt = 0; attempt < 50; attempt++)); do
        [[ ! -e "$game/install-steam-frame.sh" ]] && break
        sleep 0.1
    done
    if [[ -e "$game/install-steam-frame.sh" ]]; then
        cat "$scratch/no-path-desktop.log" >&2
        echo 'error: desktop entry selected the obsolete game-root helper without %k' >&2
        exit 1
    fi
fi
[[ -f "$boot.gloomhavenvr-backup" ]]
[[ "$(cat "$boot.gloomhavenvr-backup")" == 'wait-for-native-debugger=0' ]]
rg -q '^gfx-enable-gfx-jobs=1$' "$boot"
rg -q '^gfx-enable-native-gfx-jobs=1$' "$boot"
rg -q 'MANGOHUD=1' "$account/localconfig.vdf"
rg -q 'WINEDLLOVERRIDES=' "$account/localconfig.vdf"
cmp -s "$setup/SteamArtwork/logo.png" "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-logo.png"
cmp -s "$setup/SteamArtwork/icon.png" "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-icon.png"
for artwork in library_600x900.png library_header.png library_hero.png; do
    cmp -s "$setup/SteamArtwork/$artwork" "$XDG_DATA_HOME/GloomhavenVR/SteamArtwork/$artwork"
done
shortcut_appid="$(python3 - "$shortcuts" "$launcher" "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-icon.png" "$root/scripts/steam-frame-config.py" <<'PY'
import importlib.util
from pathlib import Path
import sys
spec = importlib.util.spec_from_file_location("steam_frame_config", sys.argv[4])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
print(module.patch_shortcuts(Path(sys.argv[1]).read_bytes(), Path(sys.argv[2]), Path(sys.argv[3]))[1])
PY
)"
for pair in "library_600x900.png:${shortcut_appid}p.png" \
    "library_header.png:${shortcut_appid}.png" \
    "library_hero.png:${shortcut_appid}_hero.png" \
    "logo.png:${shortcut_appid}_logo.png" \
    "icon.png:${shortcut_appid}_icon.png"; do
    source_name="${pair%%:*}"
    grid_name="${pair#*:}"
    cmp -s "$setup/SteamArtwork/$source_name" "$account/grid/$grid_name"
done

# Model a real Linux Steam process lifetime so the durable launch guard can
# distinguish the setup client from a restarted client without a user bus.
start_steam() {
    rm -f -- "$HOME/fake-steam-ready"
    python3 - "$HOME/fake-steam-ready" <<'STEAMPY' &
import ctypes
from pathlib import Path
import signal
import sys
ctypes.CDLL(None).prctl(15, b"steam", 0, 0, 0)
Path(sys.argv[1]).touch()
signal.pause()
STEAMPY
    fake_steam_pid=$!
    for ((attempt = 0; attempt < 50; attempt++)); do
        [[ -f "$HOME/fake-steam-ready" ]] && return
        sleep 0.02
    done
    echo 'error: synthetic Steam client did not start' >&2
    exit 1
}
stop_steam() {
    kill "$fake_steam_pid"
    wait "$fake_steam_pid" 2>/dev/null || true
    fake_steam_pid=""
}
cat > "$scratch/bin/steam" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$HOME/steam-args"
EOF
cat > "$scratch/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
printf 'unexpected user-bus call\n' >> "$HOME/user-bus-calls"
exit 1
EOF
cat > "$scratch/bin/kdialog" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$@" >> "$HOME/setup-dialogs"
if [[ -f "$HOME/fake-frame-session-kill" ]]; then
    cp -- "$HOME/steam-cached-localconfig" "$GHVR_TEST_LOCALCONFIG"
    kill -TERM "$PPID"
fi
EOF
chmod +x "$scratch/bin/steam" "$scratch/bin/systemctl" "$scratch/bin/kdialog"
export GHVR_TEST_LOCALCONFIG="$account/localconfig.vdf"
cp -- "$account/localconfig.vdf" "$scratch/localconfig-before"
cp -- "$boot" "$scratch/boot-before"
cp -- "$vr_preferences" "$scratch/vr-preferences-before"
start_steam
rm -f -- "$shortcuts"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/live.log"
[[ -s "$shortcuts" && -s "$setup/steam-launch-options.pending.json" ]]
cmp -s "$account/localconfig.vdf" "$scratch/localconfig-before"
rg -q 'native Steam launch-options confirmation required' "$setup/steam-frame-setup.log"
! rg -q 'Setup state: complete' "$setup/steam-frame-setup.log"
[[ ! -e "$HOME/steam-args" && ! -e "$HOME/user-bus-calls" ]]
# Even a previously correct disk line does not prove the live Steam cache.
if PATH="$scratch/bin:$PATH" "$launcher" > "$scratch/live-launch.log" 2>&1; then
    echo 'error: VR launcher trusted live setup disk readback' >&2
    exit 1
fi
rg -q 'native Steam save' "$scratch/live-launch.log"
[[ ! -e "$HOME/steam-args" ]]

# Flush exactly the user's observed old empty-options cache after setup and
# kill the entire client lifetime. The guard survives, and a new client still
# refuses to start flat until native UI options persist across its restart.
python3 - "$account/localconfig.vdf" "$root/scripts/steam-frame-config.py" <<'STEAMPY'
import importlib.util
from pathlib import Path
import sys
spec = importlib.util.spec_from_file_location('frame_config_test', sys.argv[2])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
p = Path(sys.argv[1])
s = p.read_text()
tokens = module.tokenize_text(s)
value = next(t for i, t in enumerate(tokens) if i and tokens[i-1].value == 'LaunchOptions')
p.write_text(s[:value.start] + module.quoted('MANGOHUD=1 %command%') + s[value.end:])
STEAMPY
cp -- "$account/localconfig.vdf" "$HOME/steam-cached-localconfig"
stop_steam
start_steam
if PATH="$scratch/bin:$PATH" "$launcher" > "$scratch/flushed-launch.log" 2>&1; then
    echo 'error: VR launcher accepted old cache flushed after restart' >&2
    exit 1
fi
rg -q 'missing or stale' "$scratch/flushed-launch.log"
[[ ! -e "$HOME/steam-args" ]]

# On Frame no service bus or detached continuation is assumed. Simulate loss
# of the installer terminal at the native guidance dialog and a stale flush.
rm -f -- "$shortcuts"
: > "$HOME/fake-frame-session-kill"
{ DISPLAY=:frame-test PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" || true; } > "$scratch/session-kill.log" 2>&1
[[ -s "$shortcuts" && -s "$setup/steam-launch-options.pending.json" ]]
cmp -s "$account/localconfig.vdf" "$HOME/steam-cached-localconfig"
[[ ! -e "$HOME/steam-args" && ! -e "$HOME/user-bus-calls" ]]
rm -f -- "$HOME/fake-frame-session-kill"
stop_steam
start_steam
if PATH="$scratch/bin:$PATH" "$launcher" > "$scratch/session-kill-launch.log" 2>&1; then
    echo 'error: terminal/session loss bypassed pending setup' >&2
    exit 1
fi
rg -q 'missing or stale' "$scratch/session-kill-launch.log"

# A native UI save is modeled as the client writing its complete changed
# option value. It is the existing canonical line, preserving MANGOHUD.
cp -- "$scratch/localconfig-before" "$account/localconfig.vdf"
PATH="$scratch/bin:$PATH" "$launcher"
printf '%s\n' -applaunch 780290 --gloomhavenvr > "$scratch/expected-args"
cmp -s "$HOME/steam-args" "$scratch/expected-args"
! rg -q -- '--gloomhavenvr' "$account/localconfig.vdf"

# A live rerun preserves all original options/config but again requires a
# native UI check before restarting; offline rerun completes automatically.
cp -- "$shortcuts" "$scratch/shortcuts-before"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/again-live.log"
cmp -s "$boot" "$scratch/boot-before"
cmp -s "$shortcuts" "$scratch/shortcuts-before"
cmp -s "$account/localconfig.vdf" "$scratch/localconfig-before"
cmp -s "$vr_preferences" "$scratch/vr-preferences-before"
stop_steam
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/again-offline.log"
[[ ! -e "$setup/steam-launch-options.pending.json" ]]
rg -q 'Setup state: complete' "$setup/steam-frame-setup.log"
PATH="$scratch/bin:$PATH" "$launcher"
cmp -s "$HOME/steam-args" "$scratch/expected-args"

# Publisher JSON retains all unrelated fields and an exact first backup.
printf '{"publisher":{"mode":"custom"},"steam_frame":{"preferMinRefreshRate":72}}\n' > "$vr_preferences"
cp -- "$vr_preferences" "$scratch/vr-preferences-publisher"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/merge-preferences.log"
python3 - "$vr_preferences" <<'STEAMPY'
import json
from pathlib import Path
import sys
assert json.loads(Path(sys.argv[1]).read_text()) == {
    "publisher": {"mode": "custom"},
    "steam_frame": {"preferMinRefreshRate": 72, "preferResolution": 3408},
}
STEAMPY
cmp -s "$setup/vrpreferences.json.gloomhavenvr-backup" "$scratch/vr-preferences-publisher"
printf '{"steam_frame":{"preferResolution":2500,"preferMinRefreshRate":72}}\n' > "$vr_preferences"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/existing-preferences.log"
rg -q '"preferResolution":2500' "$vr_preferences"
printf '{invalid json\n' > "$vr_preferences"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/invalid-preferences.log"
[[ "$(cat "$vr_preferences")" == '{invalid json' ]]
rg -q 'invalid JSON.*no changes made' "$setup/steam-frame-setup.log"
rm -f -- "$vr_preferences"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/preferences-only.log"
rg -q '"preferResolution": 3408' "$vr_preferences"

[[ -f "$setup/steam-frame-setup.log.previous" ]]

rm -f -- "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
if bash "$setup/install-steam-frame.sh" > /dev/null 2>&1; then
    echo 'error: installer accepted an incomplete mod install' >&2
    exit 1
fi

echo 'Steam Frame setup: desktop/offline setup, live native-UI handoff, stale flush, session kill, guarded AppID launch, publisher defaults and rerun passed.'
