#!/usr/bin/env python3
"""Exercise the production excluded-mode notice/cancellation seam with defect controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    text = root / "src/GloomhavenVR/Core/Loc/QuestText.cs"
    names = ("QuestGameScope.cs", "QuestSceneObjects.cs", "QuestScopeObjects.cs")
    sources = {name: (runtime / name).read_text() for name in names}
    controls = [
        ("visible-notice-not-retired", "if (notice.ShowingMessage)", "if (notice.ShowingMessage && notice.Shows < 0)", "unavailable notice replaces visible native failure"),
        ("old-hotkeys-retained", 'NativeNoticeMethod(typeof(ErrorMessage), "ClearHotkeySessions", Type.EmptyTypes).Invoke(notice, null);', "", "native old buttons and hotkeys retired exactly once"),
        ("caller-cancellation-lost", ".Invoke(SaveData.Instance, new object[] { loadMenuOnCancel, onCancelLoad });", ".Invoke(SaveData.Instance, new object[] { loadMenuOnCancel, null });", "caller cancellation exactly once"),
        ("menu-flag-lost", ".Invoke(SaveData.Instance, new object[] { loadMenuOnCancel, onCancelLoad });", ".Invoke(SaveData.Instance, new object[] { false, onCancelLoad });", "original cancellation preserves menu flag"),
        ("duplicate-acknowledgement", "if (completed) return;", "if (completed && notice.Shows < 0) return;", "caller cancellation exactly once"),
        ("german-explanation-lost", "Application.systemLanguage.Equals(SystemLanguage.German)", "false", "localized scope explanation"),
    ]
    output = root / ".planning/debug/quest-guildmaster-notice"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"sources": {name: hashlib.sha256((runtime / name).read_bytes()).hexdigest() for name in names},
             "textSha256": hashlib.sha256(text.read_bytes()).hexdigest(), "cases": [],
             "limit": "Managed native-call seams; original SDK ABI and actual admission IL are separate checks. No headset claim."}
    for name, before, after, expected in [("production", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for filename, source in sources.items():
            if filename == "QuestGameScope.cs" and before:
                assert source.count(before) == 1, "mutation binding drift: " + name
                source = source.replace(before, after, 1)
            (case / filename).write_text(source)
        result = subprocess.run([dotnet, "run", "--project", str(root / "tests/QuestGameScope.Tests/QuestGameScope.Guildmaster.csproj"),
                                 "-c", "Release", "-p:RuntimeSource=" + str(case), "-p:QuestTextSource=" + str(text)],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and (result.returncode != 0 and "FAIL " + expected in result.stdout if expected
                               else result.returncode == 0 and "PASS Quest Guildmaster notice: 145 " in result.stdout)
        proof["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print("PASS " + name, flush=True)
    print("PASS Quest Guildmaster notice: 145 assertions + " + str(len(controls)) + " rejected defects; evidence " + str(run))


if __name__ == "__main__":
    main()
