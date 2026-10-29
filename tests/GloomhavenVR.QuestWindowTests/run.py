#!/usr/bin/env python3
"""Bind native-window routing to production code and reject authored-ID/liveness regressions."""
from pathlib import Path
from contextlib import contextmanager
import hashlib
import json
import sys
import os
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path(__file__).resolve().parent


def method(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    index = opening + 1
    while depth:
        depth += (source[index] == "{") - (source[index] == "}")
        index += 1
    return source[start:index]


def run(project, shared, quest, output=None):
    env = os.environ.copy()
    env["PATH"] = str(Path.home() / ".dotnet") + os.pathsep + env["PATH"]
    env.update(DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1", UseSharedCompilation="false")
    result = subprocess.run([
        "dotnet", "run", "--project", str(project), "--configuration", "Release",
        "--property:SharedSource=" + str(shared), "--property:QuestWindowSource=" + str(quest)
    ], env=env, text=True, stdout=output if output is not None else subprocess.PIPE, stderr=subprocess.STDOUT)
    if output is None:
        (Path(project).parent / "production.log").write_text(result.stdout)
        print(result.stdout, end="", flush=True)
    return result


@contextmanager
def evidence_run():
    output = ROOT / ".planning/debug/quest-window-runtime"; output.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    completion = work / "completion.log"; completion.write_text("Evidence: " + str(work) + "\n")
    print(completion.read_text(), end="", flush=True)
    status = 1
    try:
        yield work
        status = 0
    finally:
        sys.path.insert(0, str(ROOT / "scripts"))
        from town_run_retention import cleanup_completed_run
        receipt = cleanup_completed_run("quest-window", status, completion, ROOT, process_reaped=True)
        (work / "cache-cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")


def main():
    modal = ROOT / "src/GloomhavenVR/WorldUI/Modal"
    shared = modal / "SharedWindows.cs"
    quest = modal / "ModalFallback.QuestConfirmation.cs"
    live = method((modal / "ModalFallback.4.Tick.cs").read_text(), "internal static bool FloatIsLive(")
    subject = method((modal / "ModalFallback.4.Tick.cs").read_text(), "private static bool StickinessSpentByClearedQuestSelection(")
    # FloatIsLive runs verbatim. Fixture objects represent conversion lifetime, not an alternative
    # liveness policy. Network/driver/initial spawn consumers are also bound to exact native lookup.
    consumers = {
        ROOT / "src/GloomhavenVR/Net/Remote/RemoteMapStory.cs": "ModalFallback.FloatedQuestConfirmationWindow()",
        ROOT / "src/GloomhavenVR/WorldUI/MapRoom/MapRoomDriver.cs": "MapTravelConfirm.Reconcile(ModalFallback.FloatedQuestConfirmationWindow())",
        modal / "ArcSeats.cs": "UIWindow? window = SharedWindows.WindowOf(kind);",
        ROOT / "src/GloomhavenVR/WorldUI/WorldUIModule.cs": "MapRoom.MapQuestReadyUp.Install();",
    }
    for source, token in consumers.items():
        code = re.sub(r"//[^\n]*|/\*.*?\*/", "", source.read_text(), flags=re.S)
        assert token in code, "Quest window consumer lost native instance routing: " + str(source)
    with evidence_run() as work:
        sources = [shared, quest, modal / "ModalFallback.4.Tick.cs", Path(__file__), *consumers,
            PROJECT / "Program.cs", PROJECT / "GloomhavenVR.QuestWindowTests.csproj"]
        (work / "source-pins.json").write_text(json.dumps({str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}, indent=2) + "\n")
        (work / "SharedWindows.cs.input").write_text(shared.read_text())
        (work / "QuestConfirmation.cs.input").write_text(quest.read_text())
        for name in ("GloomhavenVR.QuestWindowTests.csproj", "Program.cs"):
            (work / name).write_text((PROJECT / name).read_text())
        (work / "Liveness.cs").write_text("using System; using UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n" + live + "\n" + subject + "\n}")
        project = work / "GloomhavenVR.QuestWindowTests.csproj"
        assert run(project, shared, quest).returncode == 0, "Production quest window routing failed"
        mutations = {
            "shared-authored-id": (shared, "MapRoom.NativeMapQuestSelection.IsConfirmationWindow(window)", "window.ID == UIWindowID.QuestPopup", "Unrelated hover sharing the same ID must remain private"),
            "grab-authored-id": (shared, "return ModalFallback.TryGetGrabFor(WindowOf(kind), out grab);", "return kind == SharedWindowKind.QuestConfirm ? ModalFallback.TryGetGrabById(UIWindowID.QuestPopup, out grab) : ModalFallback.TryGetGrabFor(WindowOf(kind), out grab);", "Pose publication must use the current native grab, not first authored ID"),
            "dead-host": (quest, "return FloatIsLive(window) ? window : null;", "return window;", "Closing current confirmation must never select the other same-ID view"),
        }
        for name, (source, before, after, expected) in mutations.items():
            text = source.read_text()
            assert text.count(before) == 1, "Quest window mutation seam changed: " + name
            mutant = work / (name + ".fixture")
            mutant.write_text(text.replace(before, after))
            with (work / (name + ".log")).open("w") as output:
                result = run(project, mutant if source == shared else shared, mutant if source == quest else quest, output)
            log = (work / (name + ".log")).read_text()
            assert result.returncode != 0 and "Unhandled exception. System.InvalidOperationException: " + expected in log, "Negative control did not reach injected defect: " + name + "\n" + log
            print("Quest window negative control rejected: " + name, flush=True)
        (work / "Liveness.cs").write_text("using System; using UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n" + live + "\n" + subject.replace("&& !hasSubject)", "&& hasSubject)") + "\n}")
        with (work / "inverted-subject.log").open("w") as output:
            result = run(project, shared, quest, output)
        log = (work / "inverted-subject.log").read_text()
        assert result.returncode != 0 and "Unhandled exception. System.InvalidOperationException: A temporary hide retains the current native subject" in log, "Subject-lifetime control did not reach the defect\n" + log
        print("Quest window negative control rejected: inverted native subject lifetime", flush=True)


if __name__ == "__main__":
    main()
