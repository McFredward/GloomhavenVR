#!/usr/bin/env bash
# Linux bootstrap: a usable system Python is read-only; fallback runtimes and the
# venv are private to the extracted script folder. No package manager or sudo.
# Astral's official install-only distribution and pinned release metadata:
# https://github.com/astral-sh/python-build-standalone/releases/tag/20261003
# https://docs.astral.sh/python-build-standalone/

quest_linux_fail() { printf 'Quest Python: %s\n' "$*" >&2; return 1; }
quest_linux_owner='GloomhavenVR.QuestInstaller.Linux schema=1'
quest_linux_python_version='3.13.16'
quest_linux_python_release='20261003'
quest_linux_python_sha='4595c5589fff7bf0cb158d9a88a797e0d791fa33830770fcb7bf3f4b104feeae'
quest_linux_python_bytes=35076205
quest_linux_python_url='https://github.com/astral-sh/python-build-standalone/releases/download/20261003/cpython-3.13.16%2B20261003-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz'

quest_linux_owned_directory() {
    local directory=$1 kind=$2
    if [[ -e "$directory" || -L "$directory" ]]; then
        [[ -d "$directory" && ! -L "$directory" && -f "$directory/.quest-linux-owner" && ! -L "$directory/.quest-linux-owner" ]] || {
            quest_linux_fail "Refusing to change unowned file/link/folder: $directory"; return 1;
        }
        [[ "$(cat -- "$directory/.quest-linux-owner")" == "$quest_linux_owner kind=$kind" ]] || {
            quest_linux_fail "Refusing to change an unowned folder: $directory"; return 1;
        }
    else
        mkdir -- "$directory"
        printf '%s kind=%s\n' "$quest_linux_owner" "$kind" > "$directory/.quest-linux-owner"
    fi
}

quest_linux_usable_python() {
    # Conversion later creates a hash-pinned dependency venv with pip. Debian's
    # minimal Python can provide venv but omit ensurepip; select the standalone
    # fallback now rather than discovering that after the Wizard has started.
    "$1" -I -B -c 'import sys,venv,ensurepip,ssl; raise SystemExit(0 if sys.version_info >= (3,11) else 1)' >/dev/null 2>&1
}

quest_linux_package_valid() {
    local archive=$1
    [[ -f "$archive" && ! -L "$archive" && $(stat -c '%h' -- "$archive") == 1 && $(stat -c '%s' -- "$archive") == "$quest_linux_python_bytes" ]] &&
        [[ "$(sha256sum -- "$archive" | cut -d ' ' -f 1)" == "$quest_linux_python_sha" ]]
}

quest_linux_links_internal() {
    local root=$1 link target
    while IFS= read -r -d '' link; do
        target=$(realpath -e -- "$link") || return 1
        [[ "$target" == "$root/"* ]] || return 1
    done < <(find "$root" -type l -print0)
}

quest_linux_runtime_valid() {
    local runtime=$1
    [[ -f "$runtime/.quest-location" && ! -L "$runtime/.quest-location" && "$(cat -- "$runtime/.quest-location")" == "$runtime" &&
        -f "$runtime/.quest-files.sha256" && ! -L "$runtime/.quest-files.sha256" && -f "$runtime/python/bin/python3" ]] || return 1
    quest_linux_links_internal "$runtime" && sha256sum --status -c "$runtime/.quest-files.sha256" 2>/dev/null
}

quest_linux_download_python() {
    local scripts=$1 cache runtime archive partial stage file
    # Separate runtime cache avoids interpreting a Windows NuGet installation as
    # Linux CPython when the same Builder folder is copied between computers.
    cache="$scripts/.quest-python-linux"
    quest_linux_owned_directory "$cache" cache || return 1
    runtime="$cache/python-$quest_linux_python_version-$quest_linux_python_release"
    quest_linux_owned_directory "$runtime" runtime || return 1
    if quest_linux_runtime_valid "$runtime"; then printf '%s\n' "$runtime/python/bin/python3"; return; fi
    archive="$cache/python-$quest_linux_python_version-$quest_linux_python_release.tar.gz"
    partial="$archive.partial"
    if ! quest_linux_package_valid "$archive"; then
        [[ ! -L "$archive" && ! -L "$partial" ]] || { quest_linux_fail 'Refusing a linked Python download.'; return 1; }
        [[ ! -e "$archive" || -f "$archive" ]] || { quest_linux_fail 'Refusing an unmanaged Python archive path.'; return 1; }
        [[ ! -e "$partial" || ( -f "$partial" && $(stat -c '%h' -- "$partial") == 1 ) ]] || { quest_linux_fail 'Refusing an unmanaged Python partial download.'; return 1; }
        printf 'Downloading pinned local CPython %s for Linux...\n' "$quest_linux_python_version" >&2
        if command -v curl >/dev/null 2>&1; then
            # Continue an interrupted private download; servers without Range
            # support receive one clean retry. No bytes run before verification.
            if ! curl --fail --location --retry 2 --connect-timeout 30 --max-time 600 --continue-at - --output "$partial" "$quest_linux_python_url"; then
                curl --fail --location --retry 2 --connect-timeout 30 --max-time 600 --output "$partial" "$quest_linux_python_url" || return 1
            fi
        elif command -v wget >/dev/null 2>&1; then
            wget --https-only --timeout=120 --tries=3 --continue --output-document="$partial" "$quest_linux_python_url" || return 1
        else
            quest_linux_fail 'Python 3.11+ is unavailable. Download setup requires curl or wget.'; return 1
        fi
        quest_linux_package_valid "$partial" || { quest_linux_fail 'CPython download checksum/size mismatch; no downloaded program was executed.'; return 1; }
        mv -f -- "$partial" "$archive"
    fi
    stage=$(mktemp -d "$cache/stage-XXXXXXXX")
    printf '%s kind=runtime\n' "$quest_linux_owner" > "$stage/.quest-linux-owner"
    # The trusted SHA-256 is checked above, before extraction or execution. The
    # archive is an install-only prefix, with relative internal Python symlinks.
    if ! tar -tzf "$archive" | awk 'BEGIN {ok=1} /(^\/|(^|\/)\.\.($|\/)|(^|\/)\.quest-)/ {ok=0} !/^python(\/|$)/ {ok=0} END {exit !ok}'; then
        rm -rf -- "$stage"; quest_linux_fail 'Pinned Python archive contains an unsafe path.'; return 1
    fi
    if ! tar --extract --gzip --file "$archive" --directory "$stage" --no-same-owner --no-same-permissions || ! quest_linux_links_internal "$stage"; then
        rm -rf -- "$stage"; quest_linux_fail 'Pinned Python extraction failed.'; return 1
    fi
    # rmtree/rm never follows an internal symlink. An escaping link indicates an
    # externally changed cache and is refused instead of silently replaced.
    quest_linux_links_internal "$runtime" || { rm -rf -- "$stage"; quest_linux_fail "Refusing an externally linked runtime: $runtime"; return 1; }
    rm -rf -- "$runtime"
    mv -- "$stage" "$runtime"
    # Absolute paths intentionally invalidate this receipt after a folder move;
    # the already verified archive reconstructs the runtime without downloading.
    printf '%s\n' "$runtime" > "$runtime/.quest-location"
    while IFS= read -r -d '' file; do sha256sum -- "$file"; done < <(find "$runtime/python" -type f -print0) > "$runtime/.quest-files.sha256"
    quest_linux_runtime_valid "$runtime" || { quest_linux_fail 'Pinned CPython runtime integrity check failed.'; return 1; }
    printf '%s\n' "$runtime/python/bin/python3"
}

get_quest_installer_python() (
    set -euo pipefail
    local scripts=$1 requirements=$2 base='' candidate lock="$1/.quest-bootstrap-linux.lock" helper
    [[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { quest_linux_fail 'Linux x86_64 is required.'; exit 1; }
    for candidate in flock sha256sum stat find realpath tar awk cut mktemp; do
        command -v "$candidate" >/dev/null 2>&1 || { quest_linux_fail "Required Linux utility is missing: $candidate"; exit 1; }
    done
    [[ ! -L "$lock" && ( ! -e "$lock" || ( -f "$lock" && $(stat -c '%h' -- "$lock") == 1 ) ) ]] || { quest_linux_fail 'Refusing a linked/unmanaged bootstrap lock.'; exit 1; }
    exec 9>>"$lock"
    flock -w 10 9 || { quest_linux_fail 'Another Quest Python bootstrap is using this folder; retry after it finishes.'; exit 1; }
    for candidate in python3 python; do
        if command -v "$candidate" >/dev/null 2>&1 && quest_linux_usable_python "$(command -v "$candidate")"; then base=$(command -v "$candidate"); break; fi
    done
    if [[ -z "$base" ]]; then base=$(quest_linux_download_python "$scripts"); fi
    helper="$(dirname -- "${BASH_SOURCE[0]}")/linux_bootstrap.py"
    "$base" -I -B -X utf8 "$helper" --scripts "$scripts" --requirements "$requirements"
)
