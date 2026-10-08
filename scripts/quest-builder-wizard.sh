#!/usr/bin/env bash
# Start the same local browser wizard as the Windows launcher, without sudo.
set -euo pipefail
quest_script_directory=$(CDPATH= builtin cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
quest_source_directory=$(dirname -- "$quest_script_directory")
quest_wizard_script="$quest_source_directory/tools/quest-wizard/wizard.py"
quest_ui_directory="$quest_source_directory/tools/quest-wizard-ui"
quest_state_root="${HOME:?The Linux home directory is unavailable.}/.ghvrq"
quest_open_browser=true
quest_port=0

quest_fail() { printf 'Quest Wizard: %s\n' "$*" >&2; exit 1; }
while (( $# )); do
    case "$1" in
        --no-browser) quest_open_browser=false; shift ;;
        --state-root|--port)
            (( $# >= 2 )) || quest_fail "A value is required after $1."
            if [[ "$1" == --state-root ]]; then quest_state_root="$2"; else quest_port="$2"; fi
            shift 2 ;;
        -h|--help)
            printf 'Usage: bash %s [--no-browser] [--state-root PATH] [--port NUMBER]\n' "${BASH_SOURCE[0]}"
            exit 0 ;;
        *) quest_fail "Unknown option: $1" ;;
    esac
done
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || quest_fail 'The Quest builder requires Linux x86_64 for Unity and its native tools.'
[[ -f "$quest_wizard_script" && -f "$quest_ui_directory/index.html" ]] || quest_fail 'Wizard files are missing. Extract the complete matching Builder archive.'
source "$quest_source_directory/tools/quest-installer/bootstrap.sh"
quest_python=$(get_quest_installer_python "$quest_script_directory" "$quest_source_directory/tools/quest-installer/requirements.txt")
quest_arguments=("$quest_wizard_script" serve --state-root "$quest_state_root" --ui-root "$quest_ui_directory" --port "$quest_port")
if $quest_open_browser; then quest_arguments+=(--open-browser); fi
printf 'GloomhavenVR Quest Wizard - local browser interface\nKeep this launch window open while using the wizard.\n'
exec "$quest_python" -I -B -X utf8 "${quest_arguments[@]}"
