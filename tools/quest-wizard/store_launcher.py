"""Open an installed store client; opening it never establishes game ownership."""
import importlib.util
from pathlib import Path
import re

from state import WizardError

LABELS = {"steam": "Steam", "epic": "Epic Games Launcher", "gog": "GOG Galaxy"}


def required(provider, *, cause="", **parameters):
    if provider not in LABELS:
        raise WizardError("launcher_provider", "Select Steam, Epic or GOG.", "Bitte Steam, Epic oder GOG wählen.")
    label = LABELS[provider]
    next_en = (f"Open {label} and sign in with the account used for this PC copy. "
               "Finish any account confirmation there, then continue this saved run. "
               "If the selected account differs, review the inputs. Completed results are retained.")
    next_de = (f"{label} öffnen und mit dem Konto dieser PC-Kopie anmelden. "
               "Dort gegebenenfalls die Kontobestätigung abschließen und diesen Run fortsetzen. "
               "Bei einem anderen ausgewählten Konto die Eingaben prüfen. Fertige Ergebnisse bleiben erhalten.")
    if provider != "steam":
        next_en += " Enter the profile name and provider ID in More settings if they are missing. Local installation/DLC markers remain usable without signing in."
        next_de += " Fehlende Profilnamen und Provider-ID unter Weitere Einstellungen eingeben. Lokale Installations-/DLC-Nachweise bleiben ohne Anmeldung nutzbar."
    return WizardError("store_launcher_required",
        f"Game/account verification needs attention in {label}.",
        f"Die Spiel-/Kontoprüfung benötigt eine Aktion in {label}.",
        **dict(parameters, launcherProvider=provider, completedWorkRetained=True,
               cause=str(cause)[:2048], launcherGuidance={"schema": 1, "provider": provider,
                   "nextSteps": {"en": next_en, "de": next_de}}))


def module(repo):
    # No global Builder search path or dependency aliases. The helper is stdlib
    # only and also runs inside the CLI's isolated Steam-probe boundary.
    path = Path(repo) / "tools/quest-builder/store_launchers.py"
    spec = importlib.util.spec_from_file_location("_ghvr_wizard_store_launchers", path)
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    return helper


def launch(repo, provider):
    if not isinstance(provider, str) or provider not in LABELS:
        raise WizardError("launcher_provider", "Select Steam, Epic or GOG.", "Bitte Steam, Epic oder GOG wählen.")
    helper = module(repo)
    try:
        result = helper.launch(provider)
    except helper.LauncherError as error:
        raise WizardError("store_launcher_unavailable", error.messages["en"], error.messages["de"],
                          launcherProvider=provider, launcherReason=error.reason,
                          completedWorkRetained=True) from error
    # The browser cannot provide commands, paths or arguments. Return only the
    # interactive launch outcome; no launcher result can admit a build/update.
    if result.get("provider") != provider or result.get("launched") is not True:
        raise WizardError("store_launcher_unavailable", "The installed launcher did not open.",
                          "Der installierte Launcher konnte nicht geöffnet werden.", launcherProvider=provider)
    return {"schema": 1, "event": "launcher-opened", "provider": provider,
            "launcher": LABELS[provider], "message": {
                "en": f"{LABELS[provider]} opened. Finish any sign-in there, then continue in the Builder.",
                "de": f"{LABELS[provider]} geöffnet. Dort gegebenenfalls anmelden und danach im Builder fortsetzen."}}


def failure(parameters):
    # Only known current tool causes select this action. Old unrelated Unity,
    # signing or transport failures must not request a store client.
    if parameters.get("failureStage") not in ("inspect", "inspect-update", "build", "update-mod", "update-profile"):
        return None
    text = "\n".join(str(parameters.get(key, "")) for key in ("builderError", "cause"))[:16384]
    marker = re.search(r"\[launcher-action:(steam|epic|gog)\]", text)
    if marker:
        return required(marker[1], **parameters)
    if any(value in text for value in ("Start Steam with the account owning Gloomhaven before building its Quest copy.",
                                     "DLC ownership comes from a different active Steam account; switch Steam before building.",
                                     "The active Steam client has no matching Gloomhaven license/context.")):
        return required("steam", **parameters)
    return None
