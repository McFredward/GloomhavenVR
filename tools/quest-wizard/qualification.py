"""Early host checks before large downloads; licensing is never inferred as valid."""
from __future__ import annotations
import os
from pathlib import Path
import platform
import shutil
from state import WizardError, atomic_json, ordinary

GIB = 1024 ** 3
MIN_SETUP_FREE_BYTES = 4 * GIB
# Current Unity project/shader path suffixes consume most of MAX_PATH. A short
# private root also avoids requiring users to enable global Windows path policy.
MAX_WINDOWS_ROOT_CHARS = 70


def qualify(store_root, *, system=None, machine=None, free_bytes=None):
    root = ordinary(store_root)
    system = system or platform.system(); machine = machine or platform.machine()
    if system != 'Windows': return {'schema': 1, 'supportedAutomation': False, 'licenseVerified': False}
    if machine.lower() not in ('amd64', 'x86_64'):
        raise WizardError('windows_x64_required', 'Use Windows x64 for the Unity and native builder tools.',
                          'Für Unity und die nativen Build-Werkzeuge Windows x64 verwenden.')
    if len(str(root)) > MAX_WINDOWS_ROOT_CHARS:
        raise WizardError('workspace_path_long', 'Choose a short build workspace such as D:\\GHQ, then relaunch with -StateRoot D:\\GHQ.',
                          'Einen kurzen Build-Ordner wie D:\\GHQ wählen und den Launcher mit -StateRoot D:\\GHQ neu starten.',
                          maxCharacters=MAX_WINDOWS_ROOT_CHARS)
    free = shutil.disk_usage(root).free if free_bytes is None else free_bytes
    if free < MIN_SETUP_FREE_BYTES:
        raise WizardError('workspace_space_low', 'Free at least 4 GiB before tool setup; full game conversion needs substantially more space.',
                          'Vor der Werkzeug-Einrichtung mindestens 4 GiB freigeben; der vollständige Spielumbau benötigt wesentlich mehr Platz.',
                          freeBytes=free, minimumSetupBytes=MIN_SETUP_FREE_BYTES)
    result = {'schema': 1, 'supportedAutomation': True, 'system': system, 'machine': machine,
              'freeBytes': free, 'minimumSetupBytes': MIN_SETUP_FREE_BYTES, 'rootCharacters': len(str(root)),
              'licenseVerified': False, 'unityAction': 'Sign in and activate an eligible license in Unity Hub; a version check does not verify licensing.',
              'fullBuildSpaceVerified': False}
    atomic_json(root / 'qualification.json', result)
    return result
