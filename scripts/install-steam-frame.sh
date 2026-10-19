#!/usr/bin/env bash
# Configure the Steam Frame VR entry while preserving the original flat entry.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: bash install-steam-frame.sh [--game-path PATH] [--steam-root PATH] [--dry-run] [--pause]

Run this from the extracted GloomhavenVR release after installing BepInEx.
The setup prepares the GloomhavenVR library entry and the original game's
required launch options. A running Steam Frame client restarts automatically.
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
helper_temporary=""
on_exit() {
    local status=$?
    if [[ -n "$temporary_launcher" ]]; then
        rm -f -- "$temporary_launcher"
    fi
    if [[ -n "$helper_temporary" ]]; then
        rm -f -- "$helper_temporary"
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
legacy_pending_setup="$script_dir/steam-launch-options.pending.json"
# Valve reads this app-provided Steam Frame default from the installed game's
# root. SteamVR keeps any per-application value the player has already chosen.
vr_preferences="$game_dir/vrpreferences.json"
artwork_source="$script_dir/SteamArtwork"
if [[ ! -d "$artwork_source" && -d "$script_dir/../src/GloomhavenVR/Assets/SteamFrameArtwork" ]]; then
    # Developer checkout: release archives place these under FrameSetup.
    artwork_source="$script_dir/../src/GloomhavenVR/Assets/SteamFrameArtwork"
fi
destination="${XDG_DATA_HOME:-$HOME/.local/share}/GloomhavenVR"
config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
if [[ "$destination" != /* || "$config_home" != /* ]]; then
    echo 'error: Steam setup requires absolute XDG_DATA_HOME and XDG_CONFIG_HOME paths' >&2
    exit 2
fi
managed_directory="$destination/SteamSetup"
pending_setup="$managed_directory/managed-setup.json"
managed_dropin="$config_home/systemd/user/steam.service.d/90-gloomhavenvr-setup.conf"
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

# A failed process inspection must never select the offline writer. The strict
# census and the service ownership check share the configuration helper's rules.
inspect_steam() {
    python3 -B - "$steam_helper" "$@" <<'PY'
import importlib.util
import os
from pathlib import Path
import re
import sys

spec = importlib.util.spec_from_file_location("frame_config", sys.argv[1])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
try:
    processes = module.strict_steam_processes(Path("/proc"))
    if sys.argv[2] == "state":
        print("running" if processes else "stopped")
        sys.exit(0)
    properties = dict(line.split("=", 1) for line in sys.argv[3].splitlines() if "=" in line)
    if properties.get("LoadState") != "loaded" or properties.get("ActiveState") != "active":
        raise ValueError("the user Steam service is not loaded and active")
    if properties.get("KillMode") not in {"control-group", "mixed"}:
        raise ValueError("the Steam service does not stop its complete process group")
    duration = properties.get("TimeoutStartUSec", "")
    if duration != "infinity":
        parts = re.findall(r"([0-9]+(?:\.[0-9]+)?)\s*(us|ms|s|min|h|d)", duration)
        residue = re.sub(r"[0-9]+(?:\.[0-9]+)?\s*(?:us|ms|s|min|h|d)", "", duration).strip()
        units = {"us": 1e-6, "ms": .001, "s": 1, "min": 60, "h": 3600, "d": 86400}
        if not parts or residue or sum(float(n) * units[u] for n, u in parts) < 30:
            raise ValueError("the Steam service startup budget cannot accommodate the bounded setup hook")
    main_pid = properties.get("MainPID", "")
    group = properties.get("ControlGroup", "").rstrip("/") or "/"
    if (not main_pid.isdigit() or int(main_pid) <= 0 or group == "/"
            or not properties.get("ControlGroup", "").startswith("/")):
        raise ValueError("Steam service process ownership is unavailable")
    def lifetime(pid):
        process = Path("/proc") / pid
        if process.stat().st_uid != os.getuid():
            raise ValueError("a Steam service process belongs to another user")
        return (process / "stat").read_text().rsplit(")", 1)[1].split()[19]
    def in_group(pid):
        rows = (Path("/proc") / pid / "cgroup").read_text().splitlines()
        paths = [row.split(":", 2)[2] for row in rows
                 if len(row.split(":", 2)) == 3 and row.split(":", 2)[1] in {"", "name=systemd"}]
        return any(path == group or path.startswith(group.rstrip("/") + "/") for path in paths)
    main_lifetime = lifetime(main_pid)
    if not in_group(main_pid) or not processes:
        raise ValueError("the Steam service does not own the running client")
    # Inspect a second census as well: webhelpers may appear while checking.
    for snapshot in (processes, module.strict_steam_processes(Path("/proc"))):
        for pid, start in snapshot.items():
            try:
                if lifetime(pid) != start or not in_group(pid):
                    raise ValueError("a running Steam client is outside the managed Steam service")
            except FileNotFoundError:
                continue  # An observed webhelper exited during the census.
    if lifetime(main_pid) != main_lifetime:
        raise ValueError("the Steam service changed during setup inspection")
    print("Verified the running client belongs to the active user Steam service.")
except (OSError, ValueError, IndexError, module.ConfigError) as error:
    print(f"error: cannot safely restart Steam for automatic setup: {error}", file=sys.stderr)
    sys.exit(1)
PY
}

validate_managed_service() {
    command -v systemctl >/dev/null 2>&1 || { echo 'error: the Steam user service manager is unavailable' >&2; return 1; }
    [[ -x /usr/bin/python3 && -x /usr/bin/timeout ]] || {
        echo 'error: /usr/bin/python3 and /usr/bin/timeout are required for automatic Steam setup' >&2
        return 1
    }
    local properties
    local -a query=(systemctl --user show steam.service
        --property=LoadState,ActiveState,MainPID,ControlGroup,TimeoutStartUSec,KillMode --no-pager)
    if ! properties="$("${query[@]}" 2>&1)"; then
        # Desktop launchers may omit the user bus variables. Only derive them
        # from this user's actual runtime socket; do not guess another session.
        local runtime="/run/user/$(id -u)"
        if [[ -d "$runtime" && -O "$runtime" && -S "$runtime/bus" && -O "$runtime/bus" ]]; then
            export XDG_RUNTIME_DIR="$runtime"
            export DBUS_SESSION_BUS_ADDRESS="unix:path=$runtime/bus"
            if ! properties="$("${query[@]}" 2>&1)"; then
                printf 'error: cannot inspect the Steam user service: %s\n' "$properties" >&2
                return 1
            fi
        else
            printf 'error: cannot inspect the Steam user service: %s\n' "$properties" >&2
            return 1
        fi
    fi
    inspect_steam service "$properties"
}

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
live_setup=false
steam_state="$(inspect_steam state)"
if [[ "$steam_state" == running ]]; then
    live_setup=true
    echo 'Steam client before setup: running'
    if ! $dry_run; then validate_managed_service; fi
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
mkdir -p -- "$managed_directory"
# The service hook must survive closure of the old desktop terminal and does
# not depend on a game library/removable drive to find its own executable.
helper_temporary="$(mktemp -- "$managed_directory/.steam-frame-config.XXXXXX")"
install -m 0644 -- "$steam_helper" "$helper_temporary"
helper_digest="$(python3 - "$helper_temporary" <<'PY'
import hashlib
from pathlib import Path
import sys
print(hashlib.sha256(Path(sys.argv[1]).read_bytes()).hexdigest())
PY
)"
managed_helper="$managed_directory/steam-frame-config-$helper_digest.py"
if [[ -f "$managed_helper" ]] && cmp -s -- "$helper_temporary" "$managed_helper"; then
    rm -f -- "$helper_temporary"
else
    mv -f -- "$helper_temporary" "$managed_helper"
fi
helper_temporary=""
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
printf 'check=(python3 -B %q --game-path %q --pending-setup %q' "$managed_helper" "$game_dir" "$pending_setup" >> "$temporary_launcher"
for argument in "${steam_root_args[@]}"; do
    printf ' %q' "$argument" >> "$temporary_launcher"
done
printf ' --check-launch-options)\n' >> "$temporary_launcher"
cat >> "$temporary_launcher" <<'EOF'
if ! message="$("${check[@]}" 2>&1)"; then
    printf '%s\n' "$message" >&2
    if command -v kdialog >/dev/null 2>&1 && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
        # KDialog auto-detects rich text; custom launch options are literal data.
        dialog_message="$(python3 -c 'import html,sys; print("<pre>" + html.escape(sys.argv[1]) + "</pre>")' "$message")"
        kdialog --title 'GloomhavenVR setup required' --error "$dialog_message" || true
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

# Never write the running client's VDF cache. Its shutdown flush can undo even
# a successful disk readback. The managed branch writes only after that flush,
# from the actual Steam service's persisted pre-start hook.
if ! $live_setup; then
    steam_changes="$(python3 "$managed_helper" "${steam_helper_args[@]}" --needs-update)"
    if [[ "$steam_changes" != yes && "$steam_changes" != no ]]; then
        echo "error: unexpected Steam setup status: $steam_changes" >&2
        exit 1
    fi
    if [[ "$steam_changes" == yes ]]; then
        python3 "$managed_helper" "${steam_helper_args[@]}"
    else
        echo 'Steam files selected for this setup are already current.'
    fi
    echo 'Steam file readback (Steam is stopped):'
    python3 "$managed_helper" "${steam_helper_args[@]}" --dry-run
    readback_changes="$(python3 "$managed_helper" "${steam_helper_args[@]}" --needs-update)"
    echo "Selected Steam files need repair: $readback_changes"
    if [[ "$readback_changes" != no ]]; then
        echo 'error: selected Steam-side files differ from the expected setup state' >&2
        exit 1
    fi
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
    # Validate again before handing ownership to the manager. A service/client
    # replacement during asset preparation must not restart an unrelated client.
    validate_managed_service
    queue_result="$(python3 "$managed_helper" "${steam_helper_args[@]}" --queue-managed-setup "$pending_setup")"
    printf '%s\n' "$queue_result"
    generation="$(printf '%s\n' "$queue_result" | sed -n 's/^Managed setup generation: \([0-9a-f]\{32\}\)$/\1/p')"
    if [[ ! "$generation" =~ ^[0-9a-f]{32}$ ]]; then
        echo 'error: the queued Steam setup did not return its generation' >&2
        exit 1
    fi
    python3 - "$managed_dropin" "$managed_helper" "$pending_setup" <<'PY'
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time

path = Path(sys.argv[1])
def quote(value):
    if any(ord(character) < 32 or ord(character) == 127 for character in value):
        raise ValueError("Steam setup paths cannot contain control characters")
    # systemd command lines are not shell commands. Escape its own specifier and
    # environment substitutions as well as its quoted-string syntax.
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%').replace('$', '$$') + '"'

command = ('ExecStartPre=-/usr/bin/timeout --signal=TERM --kill-after=2s 15s '
           '/usr/bin/python3 -B ' + quote(sys.argv[2]) + ' --apply-managed-setup ' + quote(sys.argv[3]))
content = ('# Owned by GloomhavenVR Steam Frame setup. No pending request is a no-op.\n'
           '# Failure is nonfatal to Steam; the VR launcher checks the repair receipt.\n'
           '[Service]\n' + command + '\n').encode()
path.parent.mkdir(parents=True, exist_ok=True)
if path.is_symlink():
    raise ValueError("refusing to replace a symlinked Steam setup drop-in")
if not path.exists() or path.read_bytes() != content:
    if path.exists():
        shutil.copy2(path, path.with_name(path.name + time.strftime('.backup-%Y%m%d-%H%M%S-') + str(os.getpid())))
    fd, temporary = tempfile.mkstemp(prefix='.gloomhavenvr-setup.', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as target:
            target.write(content)
            target.flush()
            os.fsync(target.fileno())
        os.chmod(temporary, 0o644)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
print(f"Steam service setup hook: {path}")
PY
    systemctl --user daemon-reload
    rm -f -- "$legacy_pending_setup"
    echo "Durable Steam setup request: $pending_setup"
    echo 'Setup state: queued; Steam service will apply the launch options before loading the library.'
    echo 'Restarting the Steam service. The Frame display and this terminal may close briefly.'
    systemctl --user --no-block restart steam.service

    # The old UI may close this foreground process. The persisted manager job
    # still owns completion and leaves its exact-generation result on disk.
    verified=false
    verification_log="$managed_directory/verification.log"
    verification_deadline=$((SECONDS + 30))
    while ((SECONDS < verification_deadline)); do
        if python3 "$managed_helper" --verify-managed-setup "$pending_setup" --expected-generation "$generation" > "$verification_log" 2>&1; then
            verified=true
            break
        fi
        if python3 -B - "$managed_helper" "$pending_setup" "$generation" <<'PY'
import importlib.util
from pathlib import Path
import sys
spec = importlib.util.spec_from_file_location('frame_config', sys.argv[1])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
try:
    receipt = module.read_managed_setup(Path(sys.argv[2]))
    failed = receipt['phase'] == 'failed' or receipt['generation'] != sys.argv[3]
except (OSError, module.ConfigError):
    failed = True
sys.exit(0 if failed else 1)
PY
        then
            break
        fi
        sleep 0.25
    done
    cat -- "$verification_log"
    if ! $verified; then
        echo "error: automatic Steam setup is not verified; see $pending_setup and $verification_log; rerun setup to retry" >&2
        exit 1
    fi
    echo 'Setup state: complete (Steam service applied and verified the launch options).'
else
    rm -f -- "$pending_setup" "$legacy_pending_setup"
    echo 'Setup state: complete (Steam was stopped); launch GloomhavenVR after starting Steam.'
fi
