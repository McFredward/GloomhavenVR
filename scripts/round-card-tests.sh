#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
cp "$repo_root/tests/GloomhavenVR.RoundCardTests/"*.cs "$repo_root/tests/GloomhavenVR.RoundCardTests/"*.csproj "$work_dir/"
python3 "$repo_root/tests/GloomhavenVR.RoundCardTests/extract.py" "$repo_root" "$work_dir/Production.cs"
dotnet run --project "$work_dir/GloomhavenVR.RoundCardTests.csproj" --configuration Release
cp "$work_dir/Production.cs" "$work_dir/Original.txt"
for mutation in recovered-hand stale-supplement extra-turn actor-scope long-rest owner-membership; do
    python3 - "$work_dir/Original.txt" "$work_dir/Production.cs" "$mutation" <<'PY'
import pathlib,sys
s=pathlib.Path(sys.argv[1]).read_text()
old,new={
    'recovered-hand': ('klass.HandAbilityCards.Contains(ac)', 'false'),
    'stale-supplement': ('staticPair && !HasLeftTheRound(hand, widget)', 'staticPair'),
    'extra-turn': (' || klass.ExtraTurnCards.Contains(ac)', ''),
    'actor-scope': ('ReferenceEquals(Board.CharacterFocus.TurnActor, hand.PlayerActor)', 'bool.Parse("true")'),
    'long-rest': ('CardsGameApi.IsLongResting(hand) || CardsGameApi.HasLongRested(hand)', 'bool.Parse("false")'),
    'owner-membership': ('widget.PlayerActor != null ? widget.PlayerActor : hand.PlayerActor', 'hand.PlayerActor'),
}[sys.argv[3]]
assert old in s
# Removing extra-turn admission must cause its membership to count as departed for this control.
if sys.argv[3]=='extra-turn':
    s=s.replace('return klass.HandAbilityCards.Contains(ac)', 'return klass.ExtraTurnCards.Contains(ac) || klass.HandAbilityCards.Contains(ac)')
pathlib.Path(sys.argv[2]).write_text(s.replace(old,new))
PY
    if dotnet run --project "$work_dir/GloomhavenVR.RoundCardTests.csproj" --configuration Release > "$work_dir/negative.log" 2>&1; then
        cat "$work_dir/negative.log"
        echo "FAIL: round-card negative control escaped: $mutation" >&2
        exit 1
    fi
    if ! grep -Eq 'Recovered first card must stay in hand|Round dock must exactly match current native card membership' "$work_dir/negative.log"; then
        cat "$work_dir/negative.log"
        exit 1
    fi
    echo "Round-card negative control: $mutation failed as expected."
done
