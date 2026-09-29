#!/usr/bin/env bash
# Focused smoke test for the Frame-only opt-in installer and forwarded app launch.
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT

game="$scratch/Steam library/Gloomhaven"
installer="$scratch/release/install-steam-frame.sh"
mkdir -p -- "$game/BepInEx/patchers/GloomhavenVR" "$scratch/release" "$scratch/bin"
: > "$game/GH.exe"
: > "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
cp -- "$root/scripts/install-steam-frame.sh" "$installer"
cp -- "$root/src/GloomhavenVR/Assets/GloomhavenVR_logo.png" "$scratch/release/GloomhavenVR-steam-logo.png"
cp -- "$root/unity/GloomhavenVR.Assets/Assets/Bundle/UI/VRMenuIcon.png" "$scratch/release/GloomhavenVR-steam-icon.png"

export HOME="$scratch/home"
export XDG_DATA_HOME="$HOME/.local/share"
mkdir -p -- "$HOME"
marker="$game/BepInEx/patchers/GloomhavenVR/frame-launch-opt-in.marker"
launcher="$XDG_DATA_HOME/GloomhavenVR/launch-steam-frame.sh"

bash "$installer" --game-path "$game" --dry-run > "$scratch/dry-run.log"
[[ ! -e "$marker" && ! -e "$launcher" ]]
bash "$root/scripts/install-steam-frame.sh" --game-path "$game" --dry-run > /dev/null

bash "$installer" --game-path "$game" > "$scratch/install.log"
[[ -f "$marker" && -x "$launcher" ]]
cmp -s "$scratch/release/GloomhavenVR-steam-logo.png" \
    "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-logo.png"
cmp -s "$scratch/release/GloomhavenVR-steam-icon.png" \
    "$XDG_DATA_HOME/GloomhavenVR/GloomhavenVR-steam-icon.png"

# Repeating setup may replace the wrapper, but it must preserve the marker and
# never create a nested or additional Steam entry on its own.
bash "$installer" --game-path "$game" > /dev/null
[[ -f "$marker" && -x "$launcher" ]]
[[ "$(find "$XDG_DATA_HOME/GloomhavenVR" -maxdepth 1 -type f | wc -l)" -eq 3 ]]

cat > "$scratch/bin/steam" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$HOME/steam-args"
EOF
chmod +x "$scratch/bin/steam"
PATH="$scratch/bin:$PATH" "$launcher"
printf '%s\n' -applaunch 780290 --gloomhavenvr > "$scratch/expected-args"
cmp -s "$HOME/steam-args" "$scratch/expected-args"

rm -f -- "$game/BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"
if bash "$installer" --game-path "$game" > /dev/null 2>&1; then
    echo 'error: installer accepted an incomplete game install' >&2
    exit 1
fi

echo 'Frame shortcut installer: smoke checks passed.'
