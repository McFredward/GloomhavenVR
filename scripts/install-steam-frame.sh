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
setup_log="$script_dir/steam-frame-setup.log"
if [[ -f "$setup_log" ]]; then
    mv -f -- "$setup_log" "$setup_log.previous"
fi
if ! : > "$setup_log"; then
    echo "error: cannot write Steam Frame setup log: $setup_log" >&2
    exit 1
fi
exec > >(tee -a "$setup_log") 2>&1
echo "==== GloomhavenVR Steam Frame setup: $(date -Is) ===="
echo "Setup helper: $script_dir/install-steam-frame.sh"
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

# The desktop launcher opens a terminal. Keep setup failures visible even when
# Steam later restarts the headset shell and closes the terminal.
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
    echo "Setup exit status: $status; log: $setup_log"
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
artwork_source="$script_dir/SteamArtwork"
if [[ ! -d "$artwork_source" && -d "$script_dir/../src/GloomhavenVR/Assets/SteamFrameArtwork" ]]; then
    # Developer checkout: release archives place these under FrameSetup.
    artwork_source="$script_dir/../src/GloomhavenVR/Assets/SteamFrameArtwork"
fi
destination="${XDG_DATA_HOME:-$HOME/.local/share}/GloomhavenVR"
launcher="$destination/launch-steam-frame.sh"
artwork_destination="$destination/SteamArtwork"
logo_destination="$destination/GloomhavenVR-steam-logo.png"
icon_destination="$destination/GloomhavenVR-steam-icon.png"
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
steam_helper_args=(--game-path "$game_dir" --launcher "$launcher"
    --icon "$icon_destination" --logo "$logo_destination"
    --portrait "$artwork_destination/library_600x900.png"
    --header "$artwork_destination/library_header.png"
    --hero "$artwork_destination/library_hero.png"
    "${steam_root_args[@]}")

[[ -f "$game_dir/GH.exe" ]] || { echo "error: GH.exe not found in $game_dir" >&2; exit 1; }
[[ -f "$patcher" ]] || { echo "error: GloomhavenVR preloader not found: $patcher" >&2; exit 1; }
for artwork in library_600x900.png library_header.png library_hero.png logo.png icon.png; do
    [[ -f "$artwork_source/$artwork" ]] || {
        echo "error: Steam artwork is missing: $artwork_source/$artwork; extract the complete GloomhavenVR release archive" >&2
        exit 1
    }
done
[[ -f "$steam_helper" && -f "$boot_helper" ]] || {
    echo 'error: Steam Frame setup helpers are missing; extract the complete GloomhavenVR release archive' >&2
    exit 1
}
command -v python3 >/dev/null 2>&1 || { echo 'error: python3 is required for Steam Frame setup' >&2; exit 1; }

# Validate all inputs before touching either the game or Steam. In particular,
# failure to locate the active Steam account must not leave a half-created entry.
python3 "$boot_helper" --game-path "$game_dir" --dry-run
python3 "$steam_helper" "${steam_helper_args[@]}" --dry-run

cat <<EOF
Game folder: $game_dir
VR opt-in marker: $marker
Steam shortcut target: $launcher
Steam logo: $logo_destination
Steam icon: $icon_destination
Steam Library artwork: $artwork_destination
EOF
if steam_client_alive; then
    echo 'Steam client before setup: running'
else
    echo 'Steam client before setup: stopped'
fi

if $dry_run; then
    echo 'Dry run: no files changed.'
    exit 0
fi

# Unity reads boot.config before the preloader can run. Prepare its graphics-job
# keys now, so the first Frame launch does not need the unsupported auto-restart.
python3 "$boot_helper" --game-path "$game_dir"

# The marker is intentionally Frame-only. It keeps the original library entry
# flat even when its winhttp override loads BepInEx.
mkdir -p -- "$artwork_destination"
# Keep the two legacy destination paths stable so an existing shortcut only
# changes its artwork, not its identity or the path stored in shortcuts.vdf.
for artwork in library_600x900.png library_header.png library_hero.png logo.png icon.png; do
    source="$artwork_source/$artwork"
    target="$artwork_destination/$artwork"
    if [[ "$artwork" == logo.png ]]; then target="$logo_destination"; fi
    if [[ "$artwork" == icon.png ]]; then target="$icon_destination"; fi
    if [[ ! -f "$target" ]] || ! cmp -s -- "$source" "$target"; then
        install -m 0644 -- "$source" "$target"
    fi
done
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

# On Steam Frame, closing Steam also restarts the headset shell and terminates
# the installer, even when it runs in a detached process session. Complete and
# verify every file change before requesting that restart as the final step.
steam_changes="$(python3 "$steam_helper" "${steam_helper_args[@]}" --needs-update)"
if [[ "$steam_changes" != yes && "$steam_changes" != no ]]; then
    echo "error: unexpected Steam setup status: $steam_changes" >&2
    exit 1
fi
if [[ "$steam_changes" == yes ]]; then
    live_write_args=()
    if steam_client_alive; then
        live_write_args=(--allow-running)
        echo 'Steam is running; writing its backed-up configuration before the headset restarts.'
    fi
    python3 "$steam_helper" "${steam_helper_args[@]}" "${live_write_args[@]}"
else
    echo 'Steam configuration already correct; no Steam restart needed.'
fi

echo 'Steam configuration readback before restart:'
python3 "$steam_helper" "${steam_helper_args[@]}" --dry-run
readback_changes="$(python3 "$steam_helper" "${steam_helper_args[@]}" --needs-update)"
echo "Steam configuration before restart needs repair: $readback_changes"
if [[ "$readback_changes" != no ]]; then
    echo 'error: Steam-side files differ from the expected setup state before restart' >&2
    exit 1
fi
# Archives before the nested setup layout placed these owned helpers directly in
# the game root. Extraction never deletes old files, so retire them after the
# replacement has completed successfully.
if [[ "$script_dir" == "$game_dir/BepInEx/plugins/GloomhavenVR/FrameSetup" ]]; then
    for obsolete in GloomhavenVR-steam-logo.png GloomhavenVR-steam-icon.png; do
        if [[ -f "$script_dir/$obsolete" ]] && ! rm -f -- "$script_dir/$obsolete"; then
            echo "warning: could not remove obsolete packaged artwork: $script_dir/$obsolete" >&2
        fi
    done
    for obsolete in install-steam-frame.sh steam-frame-config.py frame-boot-config.py \
        GloomhavenVR-steam-logo.png GloomhavenVR-steam-icon.png \
        GloomhavenVR-Setup.desktop; do
        if [[ -f "$game_dir/$obsolete" ]] && ! rm -f -- "$game_dir/$obsolete"; then
            echo "warning: could not remove obsolete setup file: $game_dir/$obsolete" >&2
        fi
    done
fi
echo 'Steam configuration is present on disk; the original Gloomhaven entry remains flat.'
if [[ "$steam_changes" == yes ]] && command -v steam >/dev/null 2>&1 && steam_client_alive; then
    echo 'All setup changes and readback checks are complete. Restarting Steam as the final step.'
    echo 'The Steam Frame display may briefly reboot; then check the library for GloomhavenVR.'
    # Desktop-mode Steam may not relaunch itself. This best-effort waiter starts
    # it after shutdown there; Frame's own shell restart can terminate it.
    if command -v setsid >/dev/null 2>&1; then
        setsid bash -c '
            for ((attempt = 0; attempt < 45; attempt++)); do
                if ! pgrep -u "$(id -u)" -x steam >/dev/null 2>&1 &&
                   ! pgrep -u "$(id -u)" -x steamwebhelper >/dev/null 2>&1; then
                    exec steam -silent
                fi
                sleep 1
            done
        ' </dev/null >/dev/null 2>&1 &
    fi
    pause_on_exit=false
    steam -shutdown || true
else
    echo 'Check the Steam library for GloomhavenVR.'
fi
