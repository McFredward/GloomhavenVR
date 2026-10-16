"""Finite recovery choices for the current APK-update failure only.

These suggestions confer no repair, signing or deletion authority. The caller
has already selected this attempt's regular-file failure and child-log tails;
old progress events and unrelated workspace logs are deliberately not searched.
"""
from __future__ import annotations

from state import WizardError

UPDATE_STAGES = frozenset(("inspect-update", "update-mod", "update-profile"))
REBUILD_DECISION_PREFIX = "Compact update needs a rebuild decision:"


def rebuild_decision(cause, *, stage="update-mod"):
    """Pause before original-content work; a diagnostic is never rebuild consent."""
    cause = str(cause)[:16384]
    return update_failure({"workflowMode": "update-mod", "failureStage": stage,
                           "builderError": REBUILD_DECISION_PREFIX + " " + cause,
                           "cause": cause})


def update_failure(parameters):
    mode, stage = parameters.get("workflowMode"), parameters.get("failureStage")
    if mode is not None and mode not in ("update-mod", "update-profile"):
        return None
    # Support143919's actual nested producer reports stage="build". Only the
    # Supervisor's explicit selected workflow plus a current structured failure
    # can qualify that legacy shape; touching a stale log grants no authority.
    legacy_update = (mode in ("update-mod", "update-profile") and stage in ("build", "inspect")
                     and "builderError" in parameters)
    if stage not in UPDATE_STAGES and not legacy_update:
        return None
    error = "\n".join(str(parameters.get(name, "")) for name in ("builderError", "cause"))[:16384]
    kind = "unknown"
    en = "The APK update stopped at the cause shown below."
    de = "Die APK-Aktualisierung wurde bei der unten genannten Ursache angehalten."
    next_en = ("Retry this run to reuse compatible saved results. If the same cause returns, save diagnostics and keep the workspace for a targeted repair. "
               "Use Check update inputs to review the PC game, APK/ZIP and signing selection. A new full build is a separate choice on the start page.")
    next_de = ("Diesen Run erneut versuchen, um passende gespeicherte Ergebnisse zu verwenden. Tritt dieselbe Ursache wieder auf, Logs exportieren und den Arbeitsordner für eine gezielte Reparatur behalten. "
               "Unter Update-Eingaben prüfen lassen sich PC-Spiel, APK/ZIP und Signaturauswahl kontrollieren. Ein vollständiger neuer Build ist eine gesonderte Wahl auf der Startseite.")
    decision = REBUILD_DECISION_PREFIX in error
    if decision:
        kind = "rebuild-decision"
        en = "The compact update cannot continue automatically. A full rebuild requires your explicit choice."
        de = "Das kompakte Update kann nicht automatisch fortgesetzt werden. Ein vollständiger Neuaufbau benötigt deine ausdrückliche Entscheidung."
        next_en = ("Keep the existing APK and workspace. Save diagnostics for a targeted repair, then retry this saved update with the corrected Builder. "
                   "Check update inputs if another APK or game copy was selected. You can explicitly set up a new build below; that opens the selection form and does not start conversion or delete saved results.")
        next_de = ("Vorhandene APK und Arbeitsordner behalten. Logs für eine gezielte Reparatur exportieren und dieses gespeicherte Update danach mit dem korrigierten Builder erneut versuchen. "
                   "Update-Eingaben prüfen, falls eine andere APK oder Spielkopie ausgewählt wurde. Unten lässt sich ausdrücklich ein neuer Build einrichten; das öffnet die Auswahl und startet keine Konvertierung oder Löschung gespeicherter Ergebnisse.")
    elif "Compact update plugin import failed:" in error:
        kind = "compact-project"
        en = "Unity could not load the required update plugins. This does not establish that the game contents need rebuilding."
        de = "Unity konnte die benötigten Update-Plugins nicht laden. Das belegt keinen nötigen Neuaufbau der Spielinhalte."
        next_en = ("Keep the existing APK, converted game contents and workspace. Save diagnostics for a targeted plugin repair, then retry this saved update with the corrected Builder. "
                   "Close any Unity Editor using the update project before retrying. Repeating the same attempt without a fix will not resolve an incompatible plugin.")
        next_de = ("Vorhandene APK, konvertierte Spielinhalte und Arbeitsordner behalten. Logs für eine gezielte Plugin-Reparatur exportieren und dieses gespeicherte Update danach mit dem korrigierten Builder erneut versuchen. "
                   "Vor dem Wiederholen einen Unity Editor schließen, der das Update-Projekt verwendet. Derselbe Versuch ohne Korrektur behebt kein inkompatibles Plugin.")
    elif any(text in error for text in ("Compact update repair required:", "The retained private UGUI package changed;",
                                      "A rewritten original assembly needs one recovered DLL and its retained .meta:",
                                      "The code-update package API binding is incomplete", "The code update did not compile the actual Android package APIs.")):
        kind = "compact-project"
        en = "The small update compiler project did not produce a verified native update with its current inputs."
        de = "Das kleine Compilerprojekt für das Update hat mit seinen aktuellen Eingaben kein geprüftes natives Update erzeugt."
        next_en = ("Close any Unity Editor using this update project, then retry this saved run. Compatible saved game conversion remains available. "
                   "If the same cause returns, save diagnostics; a repeated retry alone will not resolve an unknown compiler or package mismatch. Keep the workspace and original APK.")
        next_de = ("Einen Unity Editor schließen, der dieses Update-Projekt geöffnet hat, und diesen gespeicherten Run erneut versuchen. Eine passende gespeicherte Spielkonvertierung bleibt verfügbar. "
                   "Bleibt dieselbe Ursache bestehen, Logs exportieren; bloßes Wiederholen behebt keine unbekannte Compiler- oder Paketabweichung. Arbeitsordner und ursprüngliche APK behalten.")
    elif any(text in error for text in ("Code-only project belongs to a different update;", "Code-only project owner state is invalid.",
                                        "The code-only project has no matching owned staging receipt;", "Compact update repair refused:",
                                        "Code-only project cannot reuse a linked", "Code-only project rejects linked")):
        kind = "project-ownership"
        en = "The retained update project does not have an unambiguous matching owner."
        de = "Das gespeicherte Update-Projekt lässt sich diesem Vorgang nicht eindeutig zuordnen."
        next_en = ("Close other Builder or Unity windows using this workspace. Check update inputs and keep the original PC game and APK/ZIP selected for this run, then retry. "
                   "If the owner still differs, save diagnostics for a targeted repair. Do not delete the whole workspace or copy another project's Library into it.")
        next_de = ("Andere Builder- oder Unity-Fenster schließen, die diesen Arbeitsordner verwenden. Update-Eingaben prüfen, für diesen Run dieselbe PC-Spielkopie und APK/ZIP auswählen und erneut versuchen. "
                   "Bleibt die Zuordnung abweichend, Logs für eine gezielte Reparatur exportieren. Nicht den gesamten Arbeitsordner löschen oder die Library eines anderen Projekts hineinkopieren.")
    elif any(text in error for text in ("The PC game does not match this APK's original assembly:", "Owned PC game validation rejects linked assembly inputs.",
                                        "Code update requires the owned original game managed plugin set.")):
        kind = "owned-input"
        en = "The selected PC game files do not match the original game used by this APK."
        de = "Die ausgewählten PC-Spieldateien passen nicht zum Originalspiel dieser APK."
        next_en = ("Choose Check update inputs and select the matching installed PC game and APK/ZIP. If the game was updated, restore the matching game version or explicitly create a new Quest version from the current game on the start page. "
                   "The ownership and assembly checks cannot be skipped. Keep compatible saved build results.")
        next_de = ("Update-Eingaben prüfen wählen und die passende installierte PC-Spielkopie und APK/ZIP auswählen. Wurde das Spiel aktualisiert, die passende Spielversion wiederherstellen oder auf der Startseite ausdrücklich eine neue Quest-Version aus dem aktuellen Spiel erstellen. "
                   "Besitz- und Assembly-Prüfungen können nicht übersprungen werden. Passende gespeicherte Build-Ergebnisse behalten.")
    elif any(text in error for text in ("requires its original signing folder", "requires the original signing key", "selected signing folder lacks ",
                                        "selected signing key does not belong to this APK", "workspace already has a different signing key",
                                        "selected source APK has no single verified signing certificate", "selected local signing key has no verified certificate",
                                        "Local signing identity changed;", "signing identity changed after update qualification")):
        kind = "signing"
        en = "The APK update needs a valid signing choice for this APK."
        de = "Die APK-Aktualisierung benötigt eine gültige Signaturauswahl für diese APK."
        next_en = ("Choose Check update inputs. For an in-place update, select the original signing folder containing quest.keystore and local-key.json, or keep its original Builder workspace. "
                   "If that key is unavailable, explicitly choose the new-owner signing option; installation may then require separate app-data migration or reset consent. An APK cannot supply its private signing key.")
        next_de = ("Update-Eingaben prüfen wählen. Für ein direktes Update den ursprünglichen Signaturordner mit quest.keystore und local-key.json auswählen oder dessen ursprünglichen Builder-Arbeitsordner nutzen. "
                   "Ist dieser Schlüssel nicht verfügbar, ausdrücklich die Signatur für einen neuen Besitzer wählen; bei der Installation kann danach eine gesonderte Entscheidung zur Datenübernahme oder zum Zurücksetzen nötig sein. Eine APK enthält ihren privaten Signaturschlüssel nicht.")
    elif any(text in error for text in ("The existing Quest APK could not be read.", "An ARM64 IL2CPP Quest APK is required", "selected source APK changed", "selected APK changed",
                                        "A complete Campaign APK with matching signed game identity is required.", "APK contains duplicate, encrypted or excessive members:",
                                        "APK signed JSON contract is unreadable:", "APK lacks one bounded signed contract:")):
        kind = "source-apk"
        en = "The selected APK could not be read consistently or does not contain the required Quest game contracts."
        de = "Die ausgewählte APK konnte nicht konsistent gelesen werden oder enthält nicht die benötigten Quest-Spielnachweise."
        next_en = ("Wait for any copy, download or synchronization of the APK/ZIP to finish. Choose Check update inputs and select the completed Quest APK or exported Quest ZIP; the existing .ghvrq result can also be used. "
                   "Then retry. If the same file is still rejected, save diagnostics rather than repeatedly rebuilding the game.")
        next_de = ("Kopieren, Herunterladen oder Synchronisieren der APK/ZIP zuerst abschließen lassen. Update-Eingaben prüfen wählen und die fertige Quest-APK oder exportierte Quest-ZIP auswählen; auch das vorhandene Ergebnis aus .ghvrq lässt sich nutzen. "
                   "Danach erneut versuchen. Wird dieselbe Datei weiter abgelehnt, Logs exportieren, statt das Spiel wiederholt neu zu bauen.")
    return WizardError("update_rebuild_decision_required" if decision else "build_tool_failed", en, de,
                       **dict(parameters, completedWorkRetained=True, updateGuidance={
                           "schema": 1, "kind": kind, "nextSteps": {"en": next_en, "de": next_de},
                           "retryRetained": True, "editInputs": True,
                           **({"newBuild": True} if decision else {})}))
