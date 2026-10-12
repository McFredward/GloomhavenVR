#!/usr/bin/env bash
# Configure the Steam Frame VR entry while preserving the original flat entry.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: bash install-steam-frame.sh [--game-path PATH] [--steam-root PATH] [--dry-run] [--pause]

Run this from the extracted GloomhavenVR release after installing BepInEx.
The setup prepares the GloomhavenVR library entry. While Steam is running,
finish the original game's launch options in Steam's Properties UI as instructed.
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
pending_setup="$script_dir/steam-launch-options.pending.json"
# Valve reads this app-provided Steam Frame default from the installed game's
# root. SteamVR keeps any per-application value the player has already chosen.
vr_preferences="$game_dir/vrpreferences.json"
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
Steam Frame per-eye resolution default: $vr_preferences (3408 px; existing value preserved)
EOF
if steam_client_alive; then
    echo 'Steam client before setup: running'
else
    echo 'Steam client before setup: stopped'
fi

ensure_frame_preferences() {
    python3 - "$vr_preferences" "$1" "$script_dir/vrpreferences.json.gloomhavenvr-backup" <<'PY'
import json
import os
from pathlib import Path
import stat
import sys
import tempfile

path = Path(sys.argv[1])
dry_run = sys.argv[2] == "dry-run"
if path.is_symlink():
    print("PRESERVED: Steam Frame preferences are a symlink; leaving their target untouched.")
    sys.exit(0)
try:
    previous = path.read_bytes()
except FileNotFoundError:
    previous = None
except OSError as error:
    print(f"error: cannot read Steam Frame preferences: {error}", file=sys.stderr)
    sys.exit(1)

if previous is None:
    preferences = {"steam_frame": {"preferResolution": 3408}}
else:
    try:
        preferences = json.loads(previous.decode("utf-8-sig"))
    except (UnicodeError, json.JSONDecodeError) as error:
        print(f"PRESERVED: existing Steam Frame preferences are invalid JSON ({error}); no changes made.")
        sys.exit(0)
    if not isinstance(preferences, dict):
        print("PRESERVED: existing Steam Frame preferences have no JSON object at the root; no changes made.")
        sys.exit(0)
    frame = preferences.setdefault("steam_frame", {})
    if not isinstance(frame, dict):
        print("PRESERVED: existing steam_frame preferences are not a JSON object; no changes made.")
        sys.exit(0)
    if "preferResolution" in frame:
        print("PRESERVED: existing Steam Frame per-eye resolution preference remains unchanged.")
        sys.exit(0)
    frame["preferResolution"] = 3408

verb = "create" if previous is None else "add 3408 to"
if dry_run:
    print(f"WOULD_CHANGE: would {verb} Steam Frame per-eye resolution preferences.")
    sys.exit(0)

encoded = (json.dumps(preferences, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
if previous is None:
    try:
        with path.open("xb") as target:
            target.write(encoded)
            target.flush()
            os.fsync(target.fileno())
    except FileExistsError:
        print("PRESERVED: Steam Frame preferences appeared during setup; no changes made.")
        sys.exit(0)
else:
    # An existing publisher file may carry other settings. Keep an exact one-time backup,
    # preserve every JSON field, and avoid replacing a concurrent edit.
    if path.read_bytes() != previous:
        print("PRESERVED: Steam Frame preferences changed during setup; no changes made.")
        sys.exit(0)
    backup = Path(sys.argv[3])
    try:
        with backup.open("xb") as target:
            target.write(previous)
            target.flush()
            os.fsync(target.fileno())
    except FileExistsError:
        pass
    fd, temporary_name = tempfile.mkstemp(prefix=".vrpreferences.json.", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(fd, "wb") as target:
            target.write(encoded)
            target.flush()
            os.fsync(target.fileno())
        os.chmod(temporary, stat.S_IMODE(path.stat().st_mode))
        if path.read_bytes() != previous:
            print("PRESERVED: Steam Frame preferences changed during setup; no changes made.")
            sys.exit(0)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
print(f"CHANGED: Steam Frame per-eye resolution default is 3408 px in {path}.")
PY
}

if $dry_run; then
    ensure_frame_preferences dry-run
    echo 'Dry run: no files changed.'
    exit 0
fi

# Unity reads boot.config before the preloader can run. Prepare its graphics-job
# keys now, so the first Frame launch does not need the unsupported auto-restart.
python3 "$boot_helper" --game-path "$game_dir"

# preferResolution is an absolute per-eye width, not SteamVR's percentage
# multiplier. This file is a suggested DEFAULT; the user's SteamVR override
# remains authoritative. The helper adds only a missing key in valid JSON.
preferences_result="$(ensure_frame_preferences apply)"
echo "$preferences_result"

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
EOF
# Quote each argument as shell syntax, including Steam libraries with spaces.
# The read-only helper also resolves the current account afresh on each launch.
printf 'check=(python3 %q --game-path %q --pending-setup %q' "$steam_helper" "$game_dir" "$pending_setup" >> "$temporary_launcher"
for argument in "${steam_root_args[@]}"; do
    printf ' %q' "$argument" >> "$temporary_launcher"
done
printf ' --check-launch-options)\n' >> "$temporary_launcher"
cat >> "$temporary_launcher" <<'EOF'
if ! message="$("${check[@]}" 2>&1)"; then
    printf '%s\n' "$message" >&2
    if command -v kdialog >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
        kdialog --title 'GloomhavenVR setup required' --error "$message" || true
    elif command -v zenity >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
        zenity --error --no-markup --title='GloomhavenVR setup required' --text="$message" || true
    fi
    exit 3
fi
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

# A native Frame report after 1.1.0 had the VR shortcut but no original-app
# override. The old live write passed disk readback, then Steam could overwrite
# localconfig with its cached value. Closing Frame's Steam kills the whole UI
# session, including detached writers; neither ordering is a safe handoff.
# Live setup now prepares only the separate shortcut and keeps Steam open for
# saving the exact options through its supported Properties UI. Offline setup
# remains automatic. A disk readback is never proof of the live client's state.
live_setup=false
write_args=()
if steam_client_alive; then
    live_setup=true
    write_args=(--shortcut-only --allow-running)
    python3 "$steam_helper" "${steam_helper_args[@]}" --record-running-setup "$pending_setup"
    echo 'Steam is running; original launch options must be saved through Steam Properties.'
fi
required_options="$(python3 "$steam_helper" "${steam_helper_args[@]}" --required-launch-options)"
printf '%s\n' "$required_options" > "$script_dir/steam-launch-options.txt"
steam_changes="$(python3 "$steam_helper" "${steam_helper_args[@]}" "${write_args[@]}" --needs-update)"
if [[ "$steam_changes" != yes && "$steam_changes" != no ]]; then
    echo "error: unexpected Steam setup status: $steam_changes" >&2
    exit 1
fi
if [[ "$steam_changes" == yes ]]; then
    python3 "$steam_helper" "${steam_helper_args[@]}" "${write_args[@]}"
else
    echo 'Steam files selected for this setup are already current.'
fi

echo 'Steam file readback (does not verify a running Steam client):'
python3 "$steam_helper" "${steam_helper_args[@]}" "${write_args[@]}" --dry-run
readback_changes="$(python3 "$steam_helper" "${steam_helper_args[@]}" "${write_args[@]}" --needs-update)"
echo "Selected Steam files need repair: $readback_changes"
if [[ "$readback_changes" != no ]]; then
    echo 'error: selected Steam-side files differ from the expected setup state' >&2
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
if $live_setup; then
    instruction="Library entry prepared. One required step remains in Steam:
Open Gloomhaven (the ORIGINAL game) > Properties > General > Launch options.
Paste/save the complete line below, including %command%:
$required_options
The line is also saved in $script_dir/steam-launch-options.txt.
Then restart Frame from its normal power menu and launch GloomhavenVR.
The original Gloomhaven entry remains flat; do not add --gloomhavenvr there.

Bibliothekseintrag vorbereitet. In Steam beim ORIGINALEN Gloomhaven:
Eigenschaften > Allgemein > Startoptionen; die komplette Zeile oben speichern.
Danach Frame über das normale Power-Menü neu starten und GloomhavenVR starten."
    printf '%s\n' "$instruction"
    if command -v kdialog >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
        kdialog --title 'GloomhavenVR: finish Steam setup / Steam-Einrichtung abschließen' --msgbox "$instruction" || true
    elif command -v zenity >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
        zenity --info --no-markup --title='GloomhavenVR: finish Steam setup' --text="$instruction" || true
    fi
    echo 'Setup state: native Steam launch-options confirmation required; no automatic shutdown requested.'
else
    rm -f -- "$pending_setup"
    echo 'Setup state: complete (Steam was stopped); launch GloomhavenVR after starting Steam.'
fi
