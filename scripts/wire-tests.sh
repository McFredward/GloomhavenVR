#!/usr/bin/env bash
# Golden-vector wire tests — byte-exact, deliberately NOT round-trip.
#
#   scripts/wire-tests.sh          build and run the ten vectors
#
# WHY THEY EXIST. A sender never parses its own packet, and both serializers are index-walkers
# with no tags. The most likely damaging refactor is therefore one that moves a block in
# `Write` AND in `TryRead` together — a "group the ifs per feature" tidy-up. Writer and reader
# stay mutually consistent, so `Write(x); TryRead(...) == x` still passes while every peer on
# the old build is corrupted. Only expected BYTES catch that, and they do: patching the finger
# curls to be batched in both directions leaves 187 assertions passing and fails exactly one,
# at byte 53.
#
# The project is NOT in GloomhavenVR.sln (see its .csproj for why) so it is invoked by path.
# It ships nothing: package-release.sh copies named artifacts out of src/*/bin and never looks
# at tests/.
set -euo pipefail

ROOT_FOR_HINT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# A FRESH WORKTREE IS NOT A FAILING CHANGE, and until this guard existed it looked exactly
# like one. Both of the per-machine, gitignored inputs below are linked in by
# scripts/worktree-setup.sh, and neither absence announces itself usefully on its own:
# a missing ressources/Managed surfaces as a bare "The directory ... does not exist", and a
# missing Directory.Build.props.user surfaces as a BadImageFormatException reading
# "Reference assemblies cannot be loaded for execution" — which names neither the file nor
# the remedy. Two parallel workers read those as real gate failures and went looking for a
# defect in their own change; one of them reported it, which is why this is here.
_worktree_hint() {
    echo "error: $1" >&2
    echo "       This looks like a worktree that was never set up, not a failing change." >&2
    echo "       Run:  bash scripts/worktree-setup.sh" >&2
    echo "       (it links libs/RuntimeDeps, libs/Natives, ressources/ and" >&2
    echo "        Directory.Build.props.user in from the main checkout)" >&2
    exit 1
}
[[ -d "$ROOT_FOR_HINT/ressources/Managed" ]] \
    || _worktree_hint "ressources/Managed is missing (the game's reference assemblies)"
[[ -e "$ROOT_FOR_HINT/Directory.Build.props.user" ]] \
    || _worktree_hint "Directory.Build.props.user is missing (the per-machine GameManaged path)"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"

PROJ="$ROOT/tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj"
OUT="$ROOT/tests/GloomhavenVR.WireTests/bin/Release/net8.0/GloomhavenVR.WireTests"

# Build and run as two steps rather than `dotnet run`: `dotnet run` swallows the trailing
# repo-root argument the shim pin needs (it re-derives it from the assembly location instead,
# which is right here but wrong the moment the binary is copied anywhere).
# A COMPILE FAILURE HERE USED TO BE COMPLETELY SILENT, which is the one thing a gate must never
# be. `dotnet build` writes its errors to STDOUT, so `>/dev/null` swallowed them; combined with
# `set -e` the script exited 1 having printed nothing at all, and "the wire tests failed" was
# indistinguishable from "the wire tests printed nothing". Captured and re-emitted on failure —
# the output is still hidden on success, which is what the quiet flag was for.
if ! BUILD_LOG="$(dotnet build "$PROJ" -c Release -v quiet --nologo 2>&1)"; then
    echo "wire tests: THE TEST PROJECT DID NOT COMPILE — not one vector ran." >&2
    echo "$BUILD_LOG" >&2
    exit 1
fi
# Exercise production native hierarchy discovery as well as pure wire DTO fixtures. The
# controlled tree harness also proves that reinstating the old eight-group cap fails at runtime.
bash "$ROOT/scripts/card-bindings-tests.sh"
bash "$ROOT/scripts/native-playback-tests.sh"
bash "$ROOT/scripts/board-refresh-tests.sh"
bash "$ROOT/scripts/card-loss-modal-tests.sh"
bash "$ROOT/scripts/presentation-send-tests.sh"
exec "$OUT" "$ROOT"
