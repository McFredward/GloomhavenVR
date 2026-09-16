#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.PickTrayTests/"*.cs "$repo_root/tests/GloomhavenVR.PickTrayTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.PickTrayTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.PickTrayTests.csproj" --configuration Release
cp "$work_dir/Production.cs" "$work_dir/original.txt"
for mutation in forget-page confirm-rearm stale-selection recycle-prefix recycle-live recycle-claim; do
    python3 - "$work_dir" "$mutation" <<'PY'
import pathlib,sys
folder=pathlib.Path(sys.argv[1]);s=(folder/'original.txt').read_text()
old,new={
'forget-page':('&& !(i < _pickLockedCount && _pickExitFlown.Contains(occupant))',''),
'confirm-rearm':('pickLive && !CardsGameApi.IsPickConfirmDialogOpen(hand)','pickLive'),
'stale-selection':('(!_pickReopenBusy && !occupant.GameCard.IsSelected)','(!_pickReopenBusy && !occupant.GameCard.IsSelected && !IsParked(occupant))'),
'recycle-prefix':('if (index < _pickLockedCount) _pickLockedCount--;',''),
'recycle-live':('if (index < _pickLockedCount) _pickLockedCount--;','if (index <= _pickLockedCount) _pickLockedCount--;'),
'recycle-claim':('_pickExitFlown.Remove(card);\n        RelayoutField();','RelayoutField();'),
}[sys.argv[2]]
assert s.count(old)==1
(folder/'Production.cs').write_text(s.replace(old,new))
PY
    if dotnet run --project "$work_dir/GloomhavenVR.PickTrayTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
    case "$mutation" in
        forget-page) expected='Completed page flight must retain selected locked bookkeeping';;
        confirm-rearm) expected='Native confirmation must stay dark before queued grab-to-reopen completes';;
        stale-selection) expected='Removing a deselected locked entry must shift the prefix and keep other pages intact';;
        recycle-prefix) expected='Recycling a locked page entry must decrease the locked prefix';;
        recycle-live) expected='Recycling a live final-page card must preserve all earlier locked pages';;
        recycle-claim) expected='Recycling a page entry must retire its exit claim';;
    esac
    if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
    echo "Pick tray negative control: $mutation failed as expected."
done
