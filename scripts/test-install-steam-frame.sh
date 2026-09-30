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
cp -- "$root/src/GloomhavenVR/Assets/GloomhavenVR_logo.png" "$setup/GloomhavenVR-steam-logo.png"
cp -- "$root/unity/GloomhavenVR.Assets/Assets/Bundle/UI/VRMenuIcon.png" "$setup/GloomhavenVR-steam-icon.png"
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
for obsolete in "${legacy[@]}"; do
    [[ ! -e "$game/$obsolete" ]]
done
[[ -f "$boot.gloomhavenvr-backup" ]]
[[ "$(cat "$boot.gloomhavenvr-backup")" == 'wait-for-native-debugger=0' ]]
rg -q '^gfx-enable-gfx-jobs=1$' "$boot"
rg -q '^gfx-enable-native-gfx-jobs=1$' "$boot"
rg -q 'MANGOHUD=1' "$account/localconfig.vdf"
rg -q 'WINEDLLOVERRIDES=' "$account/localconfig.vdf"
cmp -s "$setup/GloomhavenVR-steam-logo.png" "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-logo.png"
cmp -s "$setup/GloomhavenVR-steam-icon.png" "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-icon.png"

# A changed Steam artwork file requires a client restart. Stub only the shell's
# pgrep/steam commands; the Python helper still checks the real /proc tree.
cat > "$scratch/bin/pgrep" <<'EOF'
#!/usr/bin/env bash
[[ -f "$HOME/fake-steam-running" && " $* " == *' -x steam '* ]]
EOF
cat > "$scratch/bin/steam" <<'EOF'
#!/usr/bin/env bash
case "$1" in
    -shutdown)
        printf 'shutdown\n' >> "$HOME/steam-setup-calls"
        rm -f -- "$HOME/fake-steam-running"
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
find "$account/grid" -maxdepth 1 -type f -name '*_logo.png' -delete
: > "$HOME/fake-steam-running"
PATH="$scratch/bin:$PATH" bash "$setup/install-steam-frame.sh" > "$scratch/update.log"
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

PATH="$scratch/bin:$PATH" "$launcher"
printf '%s\n' -applaunch 780290 --gloomhavenvr > "$scratch/expected-args"
cmp -s "$HOME/steam-args" "$scratch/expected-args"

rm -f -- "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
if bash "$setup/install-steam-frame.sh" > /dev/null 2>&1; then
    echo 'error: installer accepted an incomplete mod install' >&2
    exit 1
fi

echo 'Steam Frame setup: desktop launch, Steam entry, boot config and rerun passed.'
