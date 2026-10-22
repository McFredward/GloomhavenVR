"""Run production dismissal methods/prefix against a simulated game with real Harmony."""
from pathlib import Path
import importlib.util
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
(ROOT / ".planning/debug/active-dismiss-api").mkdir(parents=True, exist_ok=True)
RUN = Path(tempfile.mkdtemp(prefix="run-", dir=ROOT / ".planning/debug/active-dismiss-api"))
PROJECT = RUN / "project"
PROJECT.mkdir()
LOG = RUN / "run.log"
for source in Path(__file__).parent.glob("*.cs*"):
    shutil.copy2(source, PROJECT / source.name)
source = (ROOT / "src/GloomhavenVR/Cards/CardsGameApi.cs").read_text()
start = source.index("    // ------------------------------------------------------- active-card dismissal --")
end = source.index("    // ---------------------------------------------------------------------- misc --", start)
(PROJECT / "Production.cs").write_text("using System.Collections.Generic;\nusing ScenarioRuleLibrary;\nusing GloomhavenVR.Core;\nnamespace GloomhavenVR.Cards;\ninternal static partial class CardsGameApi {\n" + source[start:end] + "}\n")
shutil.copy2(ROOT / "src/GloomhavenVR/Cards/Patches/ActiveDismissConfirmation.cs", PROJECT / "Capture.cs")
environment = dict(os.environ)
dotnet = os.environ.get("DOTNET") or shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
command = [dotnet, "run", "--project", str(PROJECT / "ActiveDismiss.csproj"), "-c", "Release"]
spec = importlib.util.spec_from_file_location("retention", ROOT / "scripts/town_run_retention.py")
retention = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retention)


def execute(label):
    log_path = RUN / f"{label}.log"
    with log_path.open("w") as log:
        log.write(f"evidence: {RUN}\n")
        log.flush()
        process = subprocess.Popen(command, env=environment, text=True, stdout=log, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        try:
            status = process.wait()
        except BaseException:
            import signal
            os.killpg(process.pid, signal.SIGKILL)
            status = process.wait()
            raise
        finally:
            log.flush()
            print(retention.cleanup_completed_run("active-dismiss-api", process.returncode, log_path, ROOT,
                                                  process_reaped=True))
    return status, log_path.read_text()


control_filter = sys.argv[1] if len(sys.argv) > 1 else None
if control_filter is None:
    status, output = execute("positive")
    print(output, end="")
    if status:
        sys.exit(status)
controls = [
    ("stale-click", "Production.cs", "if (!CanConfirm)", "if (!IsPending)",
     "A stale explicit click must cancel without invoking the original affirmative callback"),
    ("cannot-cancel", "Production.cs", "|| bonus.Ability.ActiveBonusData.CannotCancel", "",
     "Every stale native permission or exact-object lifetime must revoke confirmation"),
    ("foreign-control", "Production.cs", "|| !ControlsActor(actor)", "",
     "Every stale native permission or exact-object lifetime must revoke confirmation"),
    ("capture-scope", "Capture.cs", "_capture = null;", "_ = pending;",
     "Placing must arm the captured original confirmation"),
    ("repeated-confirm", "Production.cs",
     "_finished = true;\n            _confirm = null;\n            _cancel = null;\n            action();",
     "action();", "Completed confirmations must not call cancel afterwards"),
    ("wrong-online-turn", "Production.cs", "!ReferenceEquals(choreographer.CurrentPlayerActor, actor)",
     "!ControlsActor(actor)", "Another controlled hero's turn must revoke the previous hero's staged dismissal online"),
]
if control_filter is not None and control_filter not in {entry[0] for entry in controls}:
    raise ValueError(f"Unknown control: {control_filter}")
for label, name, old, new, expected in controls:
    if control_filter is not None and label != control_filter:
        continue
    path = PROJECT / name
    original = path.read_text()
    assert original.count(old) == 1, (label, "mutation anchor is no longer unique")
    path.write_text(original.replace(old, new))
    try:
        status, output = execute(label)
        if status == 0 or expected not in output or "error CS" in output:
            print(output, end="")
            raise RuntimeError(f"Negative control {label} did not fail for its intended behavior")
        print(f"Active dismissal negative control: {label} failed as expected.")
    finally:
        path.write_text(original)
print(f"evidence: {RUN}")
