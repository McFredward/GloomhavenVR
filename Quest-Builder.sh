#!/usr/bin/env bash
# Open the local Quest Builder from a release archive or source checkout.
set -euo pipefail
quest_builder_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
exec bash "$quest_builder_root/scripts/quest-builder-wizard.sh" "$@"
