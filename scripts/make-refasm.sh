#!/usr/bin/env bash
# GloomhavenVR — generate the committed REFERENCE ASSEMBLIES in libs/RefAsm/.
#
# WHY THIS EXISTS
# ---------------
# The mod compiles against the game's own managed assemblies ($(GameManaged) —
# GH.Runtime.dll and friends). Those are the publisher's binaries: they must never
# be committed and they are not available on a GitHub-hosted runner. Without them
# CI cannot compile the mod at all.
#
# A REFERENCE ASSEMBLY is the standard answer. It carries the type and member
# METADATA (names, signatures, layout) and NO method bodies at all — the compiler
# needs exactly that and nothing more. The stubs here are a few hundred KB each,
# contain no executable game logic, and are regenerated from the user's own install
# with this script.
#
# WHAT THE BUILD DOES WITH THEM
# -----------------------------
# Directory.Build.props resolves $(GameManaged) first; if that folder does not hold
# GH.Runtime.dll (i.e. no local game install — CI), it falls back to $(RepoRoot)libs/RefAsm.
# So a developer with the game installed always compiles against the REAL assemblies
# and never notices this directory exists.
#
# IMPORTANT — WHY `--all`
# -----------------------
# The mod publicizes the game assemblies (BepInEx.AssemblyPublicizer) and calls
# internal and private members directly. A "public API only" reference assembly
# (refasmer -p / -i) would drop exactly those members and the build would fail with
# hundreds of CS0117/CS1061. `--all` keeps every member's METADATA regardless of
# visibility — still no method bodies, still no game logic.
#
# USAGE
#   scripts/make-refasm.sh [<path to Gloomhaven_Data/Managed>]
#
# The Managed folder is taken from (first hit wins):
#   1. the argument
#   2. $GameManaged
#   3. <GameManaged> in Directory.Build.props.user
#   4. ./ressources/Managed (the dev symlink into the install)
#
# ONE-TIME TOOL INSTALL (the only command the user runs by hand):
#   dotnet tool install -g JetBrains.Refasmer.CliTool
# NOTE the package name: `JetBrains.Refasmer` is the LIBRARY and has no `refasmer`
# command; the CLI lives in `JetBrains.Refasmer.CliTool`.
#
# REGENERATE AFTER EVERY GAME UPDATE. A game patch changes the signatures the mod
# compiles against; stale stubs make CI green while the real build is broken (or,
# worse, the other way round).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/libs/RefAsm"

# --- 0. dotnet / refasmer on PATH -------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
fi
export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v refasmer >/dev/null 2>&1; then
    cat >&2 <<'EOF'
error: `refasmer` not found on PATH.

Install the JetBrains Refasmer CLI once:

    dotnet tool install -g JetBrains.Refasmer.CliTool

(the package `JetBrains.Refasmer` is the library and ships no command), then make
sure ~/.dotnet/tools is on PATH and re-run this script.

This script REFUSES to fall back to copying the game DLLs: a full copy of
GH.Runtime.dll is the publisher's code and must never enter this repository.
EOF
    exit 1
fi

# --- 1. locate the game's Managed folder ------------------------------------------
MANAGED="${1:-${GameManaged:-}}"
if [[ -z "$MANAGED" && -f "$ROOT/Directory.Build.props.user" ]]; then
    MANAGED="$(sed -n 's:.*<GameManaged>\(.*\)</GameManaged>.*:\1:p' "$ROOT/Directory.Build.props.user" | head -1)"
fi
if [[ -z "$MANAGED" && -d "$ROOT/ressources/Managed" ]]; then
    MANAGED="$ROOT/ressources/Managed"
fi
if [[ -z "$MANAGED" || ! -d "$MANAGED" ]]; then
    echo "error: no game Managed folder. Pass it as an argument, set \$GameManaged," >&2
    echo "       or put <GameManaged> in Directory.Build.props.user." >&2
    exit 1
fi
MANAGED="$(cd "$MANAGED" && pwd)"

# --- 2. derive the assembly list FROM THE PROJECTS --------------------------------
# Every `HintPath="$(GameManaged)\Foo.dll"` (or /Foo.dll) in any project file that
# takes part in a CI build. Deliberately NOT "copy the Managed folder": the set must
# shrink and grow with the projects, and 169 stubs would be 169 files to review.
#
# tests/GloomhavenVR.WireTests is INCLUDED in the scan for completeness, but see
# libs/RefAsm/README.md: it LOADS UnityEngine.CoreModule at runtime (real Mathf
# rounding), so a body-less stub cannot serve it and the wire tests do not run in CI.
mapfile -t PROJECT_FILES < <(
    cd "$ROOT" &&
    find src tools tests -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | sort
    echo "Directory.Build.props"
)

mapfile -t NAMES < <(
    for f in "${PROJECT_FILES[@]}"; do
        [[ -f "$ROOT/$f" ]] || continue
        grep -o 'HintPath="\$(GameManaged)[\\/][A-Za-z0-9_.]*\.dll"' "$ROOT/$f" || true
    done | sed 's:.*[\\/]::; s:\.dll"$::' | sort -u
)

if [[ ${#NAMES[@]} -eq 0 ]]; then
    echo "error: no \$(GameManaged) HintPath found in any project file — refusing to guess." >&2
    exit 1
fi

echo "Reference set derived from ${#PROJECT_FILES[@]} project files: ${#NAMES[@]} assemblies"
echo "Source: $MANAGED"
echo

MISSING=()
SOURCES=()
for n in "${NAMES[@]}"; do
    if [[ -f "$MANAGED/$n.dll" ]]; then
        SOURCES+=("$MANAGED/$n.dll")
    else
        MISSING+=("$n.dll")
    fi
done
if [[ ${#MISSING[@]} -gt 0 ]]; then
    echo "error: not found in $MANAGED:" >&2
    printf '  %s\n' "${MISSING[@]}" >&2
    echo "       Wrong folder, or the game update renamed/removed an assembly the mod references." >&2
    exit 1
fi

# --- 3. generate ------------------------------------------------------------------
# Re-runnable: the destination is emptied first so a reference dropped from a csproj
# does not leave an orphan stub behind that nothing regenerates and nobody notices.
mkdir -p "$DEST"
find "$DEST" -maxdepth 1 -name '*.dll' -delete
refasmer --all --quiet --outputdir "$DEST" "${SOURCES[@]}"

# --- 4. verify: metadata only, no method bodies -----------------------------------
# A silent full copy is the failure this whole file exists to prevent, so the check
# is not "is it smaller" but "does the MethodDef table contain a single non-zero RVA".
python3 "$ROOT/scripts/check-refasm.py" "$DEST"

# --- 5. provenance ----------------------------------------------------------------
# Records what each stub was cut from, so "was this regenerated after the game
# update?" is answerable without the game install.
{
    echo '{'
    echo '  "kind": "reference assemblies (metadata only, no method bodies) — see README.md",'
    # No timestamp on purpose: the whole file must be a pure function of its inputs, so
    # that regenerating against an unchanged game install leaves `git status` clean and
    # a non-empty diff always MEANS something changed.
    printf '  "refasmer": "%s",\n' \
        "$(dotnet tool list -g 2>/dev/null | awk '$1 == "jetbrains.refasmer.clitool" { print $1 " " $2 }' | head -1)"
    echo '  "assemblies": {'
    first=1
    for n in "${NAMES[@]}"; do
        [[ $first -eq 0 ]] && printf ',\n'
        first=0
        printf '    "%s.dll": { "sourceSha256": "%s", "sourceBytes": %s, "refasmBytes": %s }' \
            "$n" \
            "$(sha256sum "$MANAGED/$n.dll" | cut -d' ' -f1)" \
            "$(stat -c%s "$MANAGED/$n.dll")" \
            "$(stat -c%s "$DEST/$n.dll")"
    done
    printf '\n'
    echo '  }'
    echo '}'
} > "$DEST/sources.json"

# --- 6. report --------------------------------------------------------------------
echo
echo "Produced in libs/RefAsm/:"
total_src=0
total_ref=0
for n in "${NAMES[@]}"; do
    s=$(stat -c%s "$MANAGED/$n.dll")
    r=$(stat -c%s "$DEST/$n.dll")
    total_src=$((total_src + s))
    total_ref=$((total_ref + r))
    printf '  %-34s %9d -> %8d bytes\n' "$n.dll" "$s" "$r"
done
echo
printf '  %-34s %9d -> %8d bytes (%d%% of source)\n' "TOTAL" "$total_src" "$total_ref" \
    $((total_ref * 100 / total_src))
echo
echo "Provenance: libs/RefAsm/sources.json"
echo "Commit these files. Regenerate after every game update."
