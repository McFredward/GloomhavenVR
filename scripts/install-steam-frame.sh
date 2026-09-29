#!/usr/bin/env bash
# Prepare a local Steam Frame VR shortcut without changing the original Steam app.
# Steam's own UI creates the shortcut; this script never edits Steam's VDF files.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: bash install-steam-frame.sh [--game-path PATH] [--dry-run]

Run this from the Gloomhaven folder after extracting BepInEx and GloomhavenVR.
The generated launcher must be added to Steam as a non-Steam game named GloomhavenVR.
EOF
}

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
game_dir=""
dry_run=false
while (($#)); do
    case "$1" in
        --game-path)
            (($# >= 2)) || { echo 'error: --game-path needs a directory' >&2; exit 2; }
            game_dir="$2"
            shift 2
            ;;
        --dry-run)
            dry_run=true
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

if [[ -z "$game_dir" ]]; then
    game_dir="$script_dir"
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

[[ -f "$game_dir/GH.exe" ]] || { echo "error: GH.exe not found in $game_dir" >&2; exit 1; }
[[ -f "$patcher" ]] || { echo "error: GloomhavenVR preloader not found: $patcher" >&2; exit 1; }
[[ -f "$logo" && -f "$icon" ]] || {
    echo 'error: Steam artwork is missing; extract the complete GloomhavenVR release archive' >&2
    exit 1
}

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

# The marker is intentionally Frame-only. It makes normal Gloomhaven launches
# stay flat even if the old winhttp launch override is still present in Steam.
mkdir -p -- "$destination"
install -m 0644 -- "$logo" "$destination/GloomhavenVR-steam-logo.png"
install -m 0644 -- "$icon" "$destination/GloomhavenVR-steam-icon.png"
: > "$marker"

temporary_launcher="$(mktemp -- "$destination/.launch-steam-frame.XXXXXX")"
trap 'rm -f -- "$temporary_launcher"' EXIT
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
mv -f -- "$temporary_launcher" "$launcher"
trap - EXIT

cat <<EOF

Next, in Steam Desktop Mode:
  1. Games > Add a Non-Steam Game > Browse and select:
     $launcher
  2. Name the shortcut exactly GloomhavenVR and enable Include in VR Library.
  3. In its Properties, set the icon to:
     $destination/GloomhavenVR-steam-icon.png
     Set its library logo to $destination/GloomhavenVR-steam-logo.png.
  4. Leave the original Gloomhaven entry without --gloomhavenvr.

The GloomhavenVR shortcut forwards to the original Steam AppID 780290. Steam's
original WINEDLLOVERRIDES="winhttp=n,b" %command% launch option is still required
on this Frame for BepInEx; the marker prevents VR activation without the flag.
EOF
