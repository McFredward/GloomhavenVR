#!/usr/bin/env bash
# Configure the Steam Frame VR entry while preserving the original flat entry.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: bash install-steam-frame.sh [--game-path PATH] [--steam-root PATH] [--dry-run] [--pause]

Run this from the extracted GloomhavenVR release after installing BepInEx.
The setup creates the GloomhavenVR library entry and configures the first VR launch.
EOF
}

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
game_dir=""
steam_root=""
dry_run=false
pause_on_exit=false
while (($#)); do
    case "$1" in
        --game-path)
            (($# >= 2)) || { echo 'error: --game-path needs a directory' >&2; exit 2; }
            game_dir="$2"
            shift 2
            ;;
        --steam-root)
            (($# >= 2)) || { echo 'error: --steam-root needs a directory' >&2; exit 2; }
            steam_root="$2"
            shift 2
            ;;
        --dry-run)
            dry_run=true
            shift
            ;;
        --pause)
            pause_on_exit=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "error: unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

# The desktop launcher opens a terminal. Keep failures visible, and restore Steam
# even when a later configuration write fails after its clean shutdown.
steam_stopped=false
temporary_launcher=""
steam_client_alive() {
    pgrep -u "$(id -u)" -x steam >/dev/null 2>&1 ||
        pgrep -u "$(id -u)" -x steamwebhelper >/dev/null 2>&1
}
on_exit() {
    local status=$?
    if [[ -n "$temporary_launcher" ]]; then
        rm -f -- "$temporary_launcher"
    fi
    if $steam_stopped; then
        echo 'Restarting Steam after setup...'
        nohup steam -silent >/dev/null 2>&1 </dev/null &
    fi
    if $pause_on_exit && [[ -t 0 ]]; then
        printf '\nPress Enter to close this window...'
        read -r _ || true
    fi
    return "$status"
}
trap on_exit EXIT

if [[ -z "$game_dir" ]]; then
    # The release keeps setup assets under the plugin so the extracted game
    # root contains only BepInEx and the installation texts.
    if [[ -f "$script_dir/../../../../GH.exe" ]]; then
        game_dir="$script_dir/../../../.."
    else
        game_dir="$script_dir"
    fi
fi
game_dir="$(cd -- "$game_dir" && pwd -P)"
patcher="$game_dir/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
marker="$game_dir/BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker"
logo="$script_dir/GloomhavenVR-steam-logo.png"
icon="$script_dir/GloomhavenVR-steam-icon.png"
if [[ ! -f "$logo" && -f "$script_dir/../src/GloomhavenVR/Assets/GloomhavenVR_logo.png" ]]; then
    # Developer checkout: the release archive puts these files beside the helper.
    logo="$script_dir/../src/GloomhavenVR/Assets/GloomhavenVR_logo.png"
    icon="$script_dir/../unity/GloomhavenVR.Assets/Assets/Bundle/UI/VRMenuIcon.png"
fi
destination="${XDG_DATA_HOME:-$HOME/.local/share}/GloomhavenVR"
launcher="$destination/launch-steam-frame.sh"
steam_helper="$script_dir/steam-frame-config.py"
boot_helper="$script_dir/frame-boot-config.py"
if [[ ! -f "$steam_helper" && -f "$script_dir/../scripts/steam-frame-config.py" ]]; then
    steam_helper="$script_dir/../scripts/steam-frame-config.py"
    boot_helper="$script_dir/../scripts/frame-boot-config.py"
fi
steam_root_args=()
if [[ -n "$steam_root" ]]; then
    steam_root_args=(--steam-root "$steam_root")
fi

[[ -f "$game_dir/GH.exe" ]] || { echo "error: GH.exe not found in $game_dir" >&2; exit 1; }
[[ -f "$patcher" ]] || { echo "error: GloomhavenVR preloader not found: $patcher" >&2; exit 1; }
[[ -f "$logo" && -f "$icon" ]] || {
    echo 'error: Steam artwork is missing; extract the complete GloomhavenVR release archive' >&2
    exit 1
}
[[ -f "$steam_helper" && -f "$boot_helper" ]] || {
    echo 'error: Steam Frame setup helpers are missing; extract the complete GloomhavenVR release archive' >&2
    exit 1
}
command -v python3 >/dev/null 2>&1 || { echo 'error: python3 is required for Steam Frame setup' >&2; exit 1; }

# Validate all inputs before touching either the game or Steam. In particular,
# failure to locate the active Steam account must not leave a half-created entry.
python3 "$boot_helper" --game-path "$game_dir" --dry-run
python3 "$steam_helper" --game-path "$game_dir" --launcher "$launcher" \
    --icon "$destination/GloomhavenVR-steam-icon.png" \
    --logo "$destination/GloomhavenVR-steam-logo.png" \
    "${steam_root_args[@]}" --dry-run

cat <<EOF
Game folder: $game_dir
VR opt-in marker: $marker
Steam shortcut target: $launcher
Steam logo: $destination/GloomhavenVR-steam-logo.png
Steam icon: $destination/GloomhavenVR-steam-icon.png
EOF

if $dry_run; then
    echo 'Dry run: no files changed.'
    exit 0
fi

# Unity reads boot.config before the preloader can run. Prepare its graphics-job
# keys now, so the first Frame launch does not need the unsupported auto-restart.
python3 "$boot_helper" --game-path "$game_dir"

# The marker is intentionally Frame-only. It keeps the original library entry
# flat even when its winhttp override loads BepInEx.
mkdir -p -- "$destination"
if [[ ! -f "$destination/GloomhavenVR-steam-logo.png" ]] ||
    ! cmp -s -- "$logo" "$destination/GloomhavenVR-steam-logo.png"; then
    install -m 0644 -- "$logo" "$destination/GloomhavenVR-steam-logo.png"
fi
if [[ ! -f "$destination/GloomhavenVR-steam-icon.png" ]] ||
    ! cmp -s -- "$icon" "$destination/GloomhavenVR-steam-icon.png"; then
    install -m 0644 -- "$icon" "$destination/GloomhavenVR-steam-icon.png"
fi
if [[ ! -f "$marker" ]]; then
    : > "$marker"
fi

temporary_launcher="$(mktemp -- "$destination/.launch-steam-frame.XXXXXX")"
cat > "$temporary_launcher" <<'EOF'
#!/usr/bin/env bash
# Keep Gloomhaven's original Steam AppID and Proton prefix (including its saves).
set -euo pipefail
if ! command -v steam >/dev/null 2>&1; then
    echo 'Steam is not available in PATH.' >&2
    exit 127
fi
exec steam -applaunch 780290 --gloomhavenvr
EOF
chmod 0755 "$temporary_launcher"
if [[ -f "$launcher" ]] && cmp -s -- "$temporary_launcher" "$launcher"; then
    rm -f -- "$temporary_launcher"
else
    mv -f -- "$temporary_launcher" "$launcher"
fi
temporary_launcher=""

# Steam caches localconfig.vdf and shortcuts.vdf until exit. A graceful shutdown
# ensures it writes its old state before our changes; never kill it or overwrite
# a live client's in-memory copy. The exit trap restores the client on failure.
steam_changes="$(python3 "$steam_helper" --game-path "$game_dir" --launcher "$launcher" \
    --icon "$destination/GloomhavenVR-steam-icon.png" \
    --logo "$destination/GloomhavenVR-steam-logo.png" \
    "${steam_root_args[@]}" --needs-update)"
if [[ "$steam_changes" != yes && "$steam_changes" != no ]]; then
    echo "error: unexpected Steam setup status: $steam_changes" >&2
    exit 1
fi
if [[ "$steam_changes" == yes ]] && command -v steam >/dev/null 2>&1 && steam_client_alive; then
    echo 'Closing Steam briefly to update its library configuration...'
    steam -shutdown
    for ((attempt = 0; attempt < 60; attempt++)); do
        if ! steam_client_alive; then
            steam_stopped=true
            break
        fi
        sleep 1
    done
    if ! $steam_stopped; then
        echo 'error: Steam did not exit cleanly; its library configuration was not changed' >&2
        exit 1
    fi
fi

if [[ "$steam_changes" == yes ]]; then
    python3 "$steam_helper" --game-path "$game_dir" --launcher "$launcher" \
        --icon "$destination/GloomhavenVR-steam-icon.png" \
        --logo "$destination/GloomhavenVR-steam-logo.png" \
        "${steam_root_args[@]}"
else
    echo 'Steam configuration already correct; no Steam restart needed.'
fi

if $steam_stopped; then
    echo 'Restarting Steam...'
    nohup steam -silent >/dev/null 2>&1 </dev/null &
    steam_stopped=false
fi
# Archives before the nested setup layout placed these owned helpers directly in
# the game root. Extraction never deletes old files, so retire them after the
# replacement has completed successfully.
if [[ "$script_dir" == "$game_dir/BepInEx/plugins/GloomhavenVR/FrameSetup" ]]; then
    for obsolete in install-steam-frame.sh steam-frame-config.py frame-boot-config.py \
        GloomhavenVR-steam-logo.png GloomhavenVR-steam-icon.png \
        GloomhavenVR-Setup.desktop; do
        if [[ -f "$game_dir/$obsolete" ]] && ! rm -f -- "$game_dir/$obsolete"; then
            echo "warning: could not remove obsolete setup file: $game_dir/$obsolete" >&2
        fi
    done
fi
echo 'GloomhavenVR is ready in the Steam VR library. The original Gloomhaven entry remains flat.'
