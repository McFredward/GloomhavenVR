#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.ItemBurnTests/"*.cs "$repo_root/tests/GloomhavenVR.ItemBurnTests/"*.csproj "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Cards/ItemBurnPlayback.cs" "$work_dir/Playback.cs"
python3 "$repo_root/tests/GloomhavenVR.ItemBurnTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.ItemBurnTests.csproj" --configuration Release
for mutation in early-collapse dropped-registration missing-native-lifetime no-disposal; do
    cp "$work_dir/Production.cs" "$work_dir/production.txt"
    cp "$work_dir/Playback.cs" "$work_dir/playback.txt"
    python3 - "$work_dir" "$mutation" <<'PY'
import pathlib,sys
root=pathlib.Path(sys.argv[1]);name,old,new={
'early-collapse':('Production.cs','&& !ItemBurnPlayback.Playing(_cardUI != null ? _cardUI.cardEffects : null)',''),
'dropped-registration':('Production.cs','PendingUse = true;','PendingUse = false;'),
'missing-native-lifetime':('Playback.cs','_state.Active++;','_state.Active += 0;'),
'no-disposal':('Playback.cs','(_original as IDisposable)?.Dispose();','_ = _original;'),
}[sys.argv[2]]
p=root/name;s=p.read_text();assert old in s;p.write_text(s.replace(old,new))
PY
    if dotnet run --project "$work_dir/GloomhavenVR.ItemBurnTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
    case "$mutation" in
        early-collapse|missing-native-lifetime) expected='Paused native burn must retain card and slot beyond unscaled flourish';;
        dropped-registration) expected='Used chip must remain registered in its original recess';;
        no-disposal) expected='Overlapping native burns must retain remaining ownership';;
    esac
    if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    mv "$work_dir/production.txt" "$work_dir/Production.cs"
    mv "$work_dir/playback.txt" "$work_dir/Playback.cs"
    echo "Item burn negative control: $mutation failed as expected."
done
