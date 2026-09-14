#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.FlightTimingTests/"*.cs "$work_dir/"
cp "$repo_root/tests/GloomhavenVR.FlightTimingTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.FlightTimingTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.FlightTimingTests.csproj" --configuration Release
if [[ "${FLIGHT_NEGATIVE_CONTROLS:-1}" != 1 ]]; then exit 0; fi
for mutation in missing-transfer stale-repaint missing-arrival-gate old-generation-reclaim missing-retirement; do
    mkdir -p "$work_dir/source/src/GloomhavenVR/Net/Remote"
    cp "$repo_root/src/GloomhavenVR/Net/Remote/RemoteBoardCard.cs" "$repo_root/src/GloomhavenVR/Net/Remote/RemoteCardFx.cs" "$repo_root/src/GloomhavenVR/Net/Remote/RemoteControlBoard.cs" "$work_dir/source/src/GloomhavenVR/Net/Remote/"
    python3 - "$work_dir/source/src/GloomhavenVR/Net/Remote" "$mutation" <<'PY'
import pathlib,sys
root=pathlib.Path(sys.argv[1]); mutation=sys.argv[2]
file,old,new={
'missing-transfer': ('RemoteCardFx.cs','        TransferVisibleFlight(f);','        // TransferVisibleFlight(f);'),
'stale-repaint': ('RemoteBoardCard.cs','if (!empty && id == _flightDepartedId && ownerId == _flightDepartedOwner)','if (!empty && bool.Parse("false") && id == _flightDepartedId && ownerId == _flightDepartedOwner)'),
'missing-arrival-gate': ('RemoteControlBoard.cs',' || _owner.FlightOwnsRecess(i)',''),
'old-generation-reclaim': ('RemoteControlBoard.cs','if (generation < epochs[slot]) return false;','if (bool.Parse("false") && generation < epochs[slot]) return false;'),
'missing-retirement': ('RemoteBoardCard.cs','if (_retiredFlightGenerations.TryGetValue(ownerId, out long retired) && generation <= retired)','if (bool.Parse("false") && _retiredFlightGenerations.TryGetValue(ownerId, out long retired) && generation <= retired)'),
}[mutation]
p=root/file; text=p.read_text(); assert old in text; p.write_text(text.replace(old,new))
PY
    if python3 "$repo_root/tests/GloomhavenVR.FlightTimingTests/extract.py" "$work_dir/source" "$work_dir/Production.cs" > "$work_dir/mutant.log" 2>&1; then
        if dotnet run --project "$work_dir/GloomhavenVR.FlightTimingTests.csproj" --configuration Release >> "$work_dir/mutant.log" 2>&1; then
            cat "$work_dir/mutant.log"
            echo "FAIL: flight timing negative control escaped: $mutation" >&2
            exit 1
        fi
    fi
    case "$mutation" in
        missing-transfer) expected='Flight draw precedes source transfer';;
        stale-repaint) expected='Stale seating must not repaint a departed source';;
        missing-arrival-gate) expected='Arrival seat lacks flight ownership';;
        old-generation-reclaim) expected='Older outgoing generation must not reclaim a landed newer card';;
        missing-retirement) expected='Old flight must not recreate its fence after native empty acknowledgement';;
    esac
    if ! rg -q "$expected" "$work_dir/mutant.log"; then cat "$work_dir/mutant.log"; exit 1; fi
    echo "Flight timing negative control: $mutation failed as expected."
done
