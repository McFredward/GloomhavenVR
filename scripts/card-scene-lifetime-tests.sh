#!/usr/bin/env bash
# Run the production load iterator and verify the native ownership binding.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.CardSceneLifetimeTests/GloomhavenVR.CardSceneLifetimeTests.csproj"
python3 "$root/tests/GloomhavenVR.CardSceneLifetimeTests/source_contract.py" "$root"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$root/tests/GloomhavenVR.CardSceneLifetimeTests/Program.cs" "$root/tests/GloomhavenVR.CardSceneLifetimeTests/FactoryAdapters.cs" "$project" "$fixture/"
python3 "$root/tests/GloomhavenVR.CardSceneLifetimeTests/extract_factory.py" "$root" "$fixture/Factory.production.fixture"
dotnet run --project "$project" --configuration Release --property:FactorySource="$fixture/Factory.production.fixture"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/Cards/Art/NativeCardSceneLifetime.cs').read_text()
for name, old, new in [
    ('late', 'if (_shouldRelease()) _release();', 'if (_shouldRelease()) { _native.MoveNext(); _release(); }'),
    ('restore', 'if (_shouldRelease()) _release();', '_release();'),
    ('repeat', '_started = true;', '_started = false;')]:
    assert s.count(old) == 1
    (dest/(name+'.cs.fixture')).write_text(s.replace(old, new))
PY
for mutation in late restore repeat; do
    case "$mutation" in
        late) expected='Borrowed face must return before native destruction begins' ;;
        restore) expected='DataRestoring must be sampled on first execution' ;;
        repeat) expected='A skipped no-op load must not enter on a later step' ;;
    esac
    if dotnet run --project "$fixture/GloomhavenVR.CardSceneLifetimeTests.csproj" --configuration Release \
        --property:FactorySource="$fixture/Factory.production.fixture" --property:LifetimeSource="$fixture/$mutation.cs.fixture" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: scene lifetime $mutation escaped" >&2; exit 1
    fi
    if ! rg -Fq "$expected" "$fixture/$mutation.log"; then cat "$fixture/$mutation.log"; exit 1; fi
    echo "Card scene lifetime negative control: $mutation rejected."
done
python3 - "$fixture" <<'PY'
from pathlib import Path
import sys
folder = Path(sys.argv[1])
s = (folder/'Factory.production.fixture').read_text()
for name, old, new in [
    ('retain-face', 'DetachGameCard();', ''),
    ('drop-identity', 'GameCard = widget;', ''),
    ('miss-selected', 'complete &= card.AttachGameCard(card.GameCard);', 'complete &= false;')]:
    assert s.count(old) == 1, name
    (folder/(name+'.factory.fixture')).write_text(s.replace(old, new))
PY
for mutation in retain-face drop-identity miss-selected; do
    case "$mutation" in
        retain-face) expected='Every borrowed native hierarchy must return to its owner' ;;
        drop-identity) expected='Scene release must retain selected wrapper game identity' ;;
        miss-selected) expected='Ready retained face must complete pending recovery' ;;
    esac
    if dotnet run --project "$fixture/GloomhavenVR.CardSceneLifetimeTests.csproj" --configuration Release \
        --property:FactorySource="$fixture/$mutation.factory.fixture" \
        --property:LifetimeSource="$root/src/GloomhavenVR/Cards/Art/NativeCardSceneLifetime.cs" > "$fixture/$mutation.log" 2>&1; then
        echo "FAIL: factory lifetime $mutation escaped" >&2; exit 1
    fi
    if ! rg -Fq "$expected" "$fixture/$mutation.log"; then cat "$fixture/$mutation.log"; exit 1; fi
    echo "Card scene lifetime negative control: $mutation rejected."
done
