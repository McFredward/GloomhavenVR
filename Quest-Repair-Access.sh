#!/usr/bin/env bash
# Repair only the installed app's content permissions; retain all game payloads.
set -euo pipefail
quest_repair_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$quest_repair_root/tools/quest-installer/bootstrap.sh"
quest_repair_python="$(get_quest_installer_python "$quest_repair_root/scripts" "$quest_repair_root/tools/quest-installer/requirements.txt")"
quest_repair_state="${GHVR_QUEST_STATE_ROOT:-$HOME/.ghvrq}"
quest_repair_source=(--output-root "$quest_repair_state/build")
for quest_repair_arg in "$@"; do
  case "$quest_repair_arg" in --apk|--apk=*|--handoff|--handoff=*|--output-root|--output-root=*) quest_repair_source=();; esac
done
exec "$quest_repair_python" -I -B -X utf8 "$quest_repair_root/scripts/install-quest-wireless.py" \
  "${quest_repair_source[@]}" --config "$quest_repair_state/device/wireless-install.json" \
  --repair-app-access --wait-for-connection "$@"
