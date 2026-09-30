#!/usr/bin/env bash
# Focused end-to-end check for repeatable Steam Frame setup on a synthetic account.
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT

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

bash "$setup/install-steam-frame.sh" --dry-run > "$scratch/dry-run.log"
[[ ! -e "$marker" && ! -e "$launcher" && ! -e "$shortcuts" ]]
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
        if [[ -f "$marker" && -x "$launcher" && -f "$shortcuts" &&
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

[[ -f "$marker" && -x "$launcher" && -f "$shortcuts" ]]
for ((attempt = 0; attempt < 50; attempt++)); do
    rg -q 'Steam configuration before restart needs repair: no' "$setup/steam-frame-setup.log" 2>/dev/null && break
    sleep 0.1
done
rg -q 'Steam configuration before restart needs repair: no' "$setup/steam-frame-setup.log"
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

# A changed Steam shortcut requires a client restart. The Frame reboot can
# terminate every setup process, so the VDF must already be correct at shutdown.
cat > "$scratch/bin/pgrep" <<'EOF'
#!/usr/bin/env bash
[[ -f "$HOME/fake-steam-running" && " $* " == *' -x steam '* ]]
EOF
cat > "$scratch/bin/steam" <<'EOF'
#!/usr/bin/env bash
case "$1" in
    -shutdown)
        [[ -s "$GHVR_TEST_SHORTCUTS" ]] || exit 41
        rg -q 'Steam configuration before restart needs repair: no' "$GHVR_TEST_SETUP_LOG" || exit 42
        printf 'shutdown\n' >> "$HOME/steam-setup-calls"
        rm -f -- "$HOME/fake-steam-running"
        if [[ -f "$HOME/fake-steam-kill-installer" ]]; then
            kill -TERM "$PPID"
        fi
        ;;
    -silent)
        printf 'restart\n' >> "$HOME/steam-setup-calls"
        : > "$HOME/fake-steam-running"
        ;;
    *)
        printf '%s\n' "$@" > "$HOME/steam-args"
        ;;
esac
EOF
chmod +x "$scratch/bin/pgrep" "$scratch/bin/steam"
export GHVR_TEST_SHORTCUTS="$shortcuts"
export GHVR_TEST_SETUP_LOG="$setup/steam-frame-setup.log"
rm -f -- "$shortcuts"
: > "$HOME/fake-steam-running"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/update.log"
[[ -s "$shortcuts" ]]
rg -q 'Steam configuration before restart needs repair: no' "$setup/steam-frame-setup.log"
rg -q 'All setup changes and readback checks are complete' "$setup/steam-frame-setup.log"
for ((attempt = 0; attempt < 30; attempt++)); do
    if [[ -f "$HOME/steam-setup-calls" ]] && rg -q '^restart$' "$HOME/steam-setup-calls"; then
        break
    fi
    sleep 0.1
done
printf 'shutdown\nrestart\n' > "$scratch/expected-setup-calls"
cmp -s "$HOME/steam-setup-calls" "$scratch/expected-setup-calls"

cp -- "$boot" "$scratch/boot-before"
cp -- "$shortcuts" "$scratch/shortcuts-before"
cp -- "$account/localconfig.vdf" "$scratch/localconfig-before"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/again.log"
cmp -s "$boot" "$scratch/boot-before"
cmp -s "$shortcuts" "$scratch/shortcuts-before"
cmp -s "$account/localconfig.vdf" "$scratch/localconfig-before"
cmp -s "$HOME/steam-setup-calls" "$scratch/expected-setup-calls"

# A Frame-like restart terminates the installer immediately. The shortcut and
# all expected fields must already be durable before that last command.
rm -f -- "$shortcuts"
: > "$HOME/fake-steam-kill-installer"
{ PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" || true; } > "$scratch/reboot.log" 2>&1
[[ -s "$shortcuts" ]]
rg -q 'Steam configuration before restart needs repair: no' "$setup/steam-frame-setup.log"
rg -q '^shutdown$' "$HOME/steam-setup-calls"
rm -f -- "$HOME/fake-steam-kill-installer"

PATH="$scratch/bin:$PATH" "$launcher"
printf '%s\n' -applaunch 780290 --gloomhavenvr > "$scratch/expected-args"
cmp -s "$HOME/steam-args" "$scratch/expected-args"

[[ -f "$setup/steam-frame-setup.log.previous" ]]

rm -f -- "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
if bash "$setup/install-steam-frame.sh" > /dev/null 2>&1; then
    echo 'error: installer accepted an incomplete mod install' >&2
    exit 1
fi

echo 'Steam Frame setup: desktop launch, Steam entry, boot config and rerun passed.'
