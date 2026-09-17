#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.cs "$repo_root/tests/GloomhavenVR.BurnReplayTests/"*.csproj "$work_dir/"
cp "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEpisode.cs" "$repo_root/src/GloomhavenVR/Cards/Art/NativeBurnEnumerator.cs" "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.BurnReplayTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release
cp "$work_dir/NativeBurnEpisode.cs" "$work_dir/original.txt"
for mutation in replay completed recovery identity; do
 python3 - "$work_dir" "$mutation" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]);s=(p/'original.txt').read_text()
a,b={
'replay':('_running || _completed','false || _completed'),
'completed':('_completed && (!resetting || durable)','false && (!resetting || durable)'),
'recovery':('recovered && _leftRecoveryPile','false && _leftRecoveryPile'),
'identity':('!ReferenceEquals(_card, card) ||','false ||'),
}[sys.argv[2]]
assert s.count(a)==1
(p/'NativeBurnEpisode.cs').write_text(s.replace(a,b))
PY
 if dotnet run --project "$work_dir/GloomhavenVR.BurnReplayTests.csproj" -c Release > "$work_dir/negative.log" 2>&1; then cat "$work_dir/negative.log"; exit 1; fi
 case "$mutation" in
 replay) expected='Repeated native refresh must retain the first burn iterator and paint';;
 completed) expected='Completed lost burns must never replay or turn blue';;
 recovery) expected='Actual model recovery must restore normal artwork';;
 identity) expected="Recycled widget identity must not inherit another card's burn";;
 esac
 if ! grep -Fq "$expected" "$work_dir/negative.log"; then cat "$work_dir/negative.log"; exit 1; fi
 echo "Burn replay negative control: $mutation failed as expected."
done
