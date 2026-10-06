#!/usr/bin/env python3
"""Exercise the actual Unity native importer contract; no Player/APK is built."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    output = ROOT / ".planning/debug/quest-native-plugins"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
    ndk = unity.parent / "Data/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/linux-x86_64"
    source = run / "native.c"
    source.write_text("int importer_fixture_only(void) { return 42; }\n")
    native = run / "importer-only.so"
    compiled = subprocess.run([str(ndk / "bin/clang"), "--target=aarch64-linux-android29",
        "--sysroot=" + str(ndk / "sysroot"), "-shared", "-fPIC", "-O2", "-Wall", "-Wextra", "-Werror",
        "-Wl,--no-undefined", str(source), "-o", str(native)], capture_output=True, text=True)
    (run / "ndk.log").write_text(compiled.stdout + compiled.stderr)
    if compiled.returncode: raise SystemExit("NDK importer fixture failed: " + str(run / "ndk.log"))
    project = run / "unity"
    editor = project / "Assets/Editor"
    resources = project / "Assets/Quest/Resources"
    plugins = project / "Assets/Quest/Plugins/Android/arm64-v8a"
    for directory in (editor, resources, plugins, project / "Packages", project / "ProjectSettings"):
        directory.mkdir(parents=True, exist_ok=True)
    production = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestNativePluginContract.cs"
    shutil.copyfile(production, editor / production.name)
    names = ["libQuestApparance.so", "libopus_egpv.so", "libquest_proton.so", "libquest_proton_server.so", "libqn.so", "libqw.so", "libqs.so"]
    data = native.read_bytes()
    rows = []
    for name in names:
        path = plugins / name
        path.write_bytes(data)
        rows.append({"path": path.relative_to(project).as_posix(), "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    (resources / "quest-procedural-native.json").write_text(json.dumps({"schema": 1, "backend": "proton-arm64ec-fex", "files": rows}))
    (editor / "QuestNativePluginFixture.cs").write_text(r'''
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GloomhavenVR.Quest.Editor;
public static class QuestNativePluginFixture
{
    static int checks;
    static void Check(bool value, string context) { if (!value) throw new Exception(context); checks++; }
    static void Reject(Action action, string context)
    {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new Exception("Missing rejection: " + context);
    }
    static void Save(QuestNativePluginContract.Contract value)
    { File.WriteAllText(QuestNativePluginContract.ResourcePath, JsonUtility.ToJson(value)); }
    public static void Run()
    {
        string result = Environment.GetEnvironmentVariable("QUEST_NATIVE_PLUGIN_RESULT");
        try
        {
            string original = File.ReadAllText(QuestNativePluginContract.ResourcePath);
            var good = QuestNativePluginContract.Configure("proton-arm64ec-fex");
            Check(good.files.Length == 7, "actual contract count");
            foreach (var file in good.files)
            {
                var importer = AssetImporter.GetAtPath(file.path) as PluginImporter;
                Check(importer != null, "actual native importer");
                Check(!importer.GetCompatibleWithAnyPlatform(), "AnyPlatform disabled");
                Check(!importer.GetCompatibleWithEditor(), "editor native execution disabled");
                Check(importer.GetCompatibleWithPlatform(BuildTarget.Android), "Android package inclusion");
                Check(importer.GetPlatformData(BuildTarget.Android, "CPU") == "ARM64", "ARM64 CPU");
                Check(!importer.isPreloaded, "explicit process/native execution only");
            }
            Reject(() => QuestNativePluginContract.Configure("box64-wine9"), "backend identity");
            var changed = JsonUtility.FromJson<QuestNativePluginContract.Contract>(original);
            changed.files[0].sha256 = new string('0', 64); Save(changed);
            Reject(() => QuestNativePluginContract.Configure("proton-arm64ec-fex"), "actual bytes");
            changed = JsonUtility.FromJson<QuestNativePluginContract.Contract>(original);
            changed.files = changed.files.Concat(new[] { changed.files[0] }).ToArray(); Save(changed);
            Reject(() => QuestNativePluginContract.Configure("proton-arm64ec-fex"), "duplicate program");
            changed = JsonUtility.FromJson<QuestNativePluginContract.Contract>(original);
            changed.files = changed.files.Where(f => !f.path.EndsWith("/libquest_proton.so")).ToArray(); Save(changed);
            Reject(() => QuestNativePluginContract.Configure("proton-arm64ec-fex"), "required loader");
            changed = JsonUtility.FromJson<QuestNativePluginContract.Contract>(original);
            changed.files[0].path = QuestNativePluginContract.Prefix + "../libevil.so"; Save(changed);
            Reject(() => QuestNativePluginContract.Configure("proton-arm64ec-fex"), "nonlocal path");
            File.WriteAllText(QuestNativePluginContract.ResourcePath, original);
            File.WriteAllText(result, "PASS actual native PluginImporter: " + checks + " assertions; no Player/APK/native execution.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText(result, "FAIL " + error + "\n"); EditorApplication.Exit(1); }
    }
}
''')
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    result_path = run / "results.txt"
    environment = dict(os.environ, QUEST_NATIVE_PLUGIN_RESULT=str(result_path))
    command = [str(unity), "-batchmode", "-nographics", "-projectPath", str(project),
        "-executeMethod", "QuestNativePluginFixture.Run", "-logFile", str(run / "unity.log")]
    print("Quest native plugin evidence: " + str(run), flush=True)
    try:
        result = subprocess.run(command, env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=180)
        if result_path.is_file(): print(result_path.read_text(), end="", flush=True)
        if result.returncode or not result_path.is_file(): raise SystemExit("FAIL actual Unity native importer: " + str(run / "unity.log"))
    finally:
        for cache in ("Library", "Temp"): shutil.rmtree(project / cache, ignore_errors=True)
    (run / "source-hashes.json").write_text(json.dumps({"production": hashlib.sha256(production.read_bytes()).hexdigest(),
        "fixtureNative": hashlib.sha256(data).hexdigest(), "apkBuilt": False, "androidExecutionVerified": False}, indent=2))


if __name__ == "__main__": main()
