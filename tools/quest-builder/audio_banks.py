"""Repair the witnessed startup Vorbis channel header in a small owned sidecar.

The 32 original quad Vorbis clips live in resources.assets, outside bundled-audio
recovery. AssetRipper labelled their four-channel packets mono. B692 sampled one
of those malformed clips during the damage squeak. Rebuild only the container
header, retain every original compressed packet, and import an independent tiny
Android bank; neither Campaign data nor the original installation is rewritten.
"""
from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import threading

from audio import original_clips
from storage import BuildError, _ordinary_owned, canonical, digest, record_file, value_hash, write_json, build_progress

METADATA = "assets/Quest/audio-clips.json"
PREFIX = "assets/QuestAudioClips/"
INDEX = "ghvr-quest-native-audio.tsv"
BANK = "ghvr-quest-native-audio.bundle"
MODULES = {"com.unity.modules.assetbundle": "1.0.0", "com.unity.modules.audio": "1.0.0"}


def _sibling(name):
    spec = importlib.util.spec_from_file_location("quest_audio_" + name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


_vorbis = _sibling("full_audio")


def plan(source, game_info):
    names = ("tools/quest-builder/audio_banks.py",
             "tools/quest-recovery/NativePortableRecovery/Program.cs",
             "tools/quest-recovery/NativePortableRecovery/NativePortableRecovery.csproj",
             "tools/quest-recovery/NativePortableRecovery/packages.lock.json")
    rows = [row for row in game_info.get("files", []) if row.get("path") in ("resources.assets", "resources.resource")]
    if len(rows) != 2 or any(type(row.get("size")) is not int or row["size"] <= 0
            or len(row.get("sha256", "")) != 64 or any(c not in "0123456789abcdef" for c in row["sha256"]) for row in rows):
        raise BuildError("Owned startup audio needs its exact resources.assets/resource inventory.")
    recipe = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "unityModules": MODULES,
              "sources": [record_file(Path(source) / name, name) for name in names]}
    return {"recipe": recipe, "sources": sorted(rows, key=lambda row: row["path"]),
            "producerKey": value_hash({"recipe": recipe, "sources": sorted(rows, key=lambda row: row["path"])})}


def _stamp(path):
    stat = _ordinary_owned(Path(path)).stat()
    return [stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns]


def _receipt(root, expected):
    try:
        receipt = json.loads((root / "completion.json").read_text())
        if receipt["plan"] != expected: return None
        changed = False
        for row in receipt["files"]:
            path = root / row["path"]
            if _stamp(path) != row["stamp"]:
                if record_file(path, row["path"]) != {key: row[key] for key in ("path", "size", "sha256")}: return None
                row["stamp"] = _stamp(path); changed = True
        if changed: write_json(root / "completion.json", receipt)
        return receipt
    except (OSError, ValueError, KeyError, TypeError): return None


def original_vorbis(raw, clip):
    return _vorbis.fsb_vorbis(raw, {"m_Channels": clip["channels"], "m_Frequency": clip["frequency"], "m_Length": clip["duration"]})


def qualify_corrected(raw, corrected, clip):
    channels, rate, frames, packets = original_vorbis(raw, clip)
    actual = _vorbis.ogg_packets(corrected)
    if (len(actual) < 4 or actual[0][:7] != b"\x01vorbis" or len(actual[0]) < 16
            or actual[0][11] != channels or int.from_bytes(actual[0][12:16], "little") != rate
            or actual[3:] != packets):
        raise BuildError("Owned startup Vorbis repair changed original packets, channels or sample rate: " + clip["name"])
    return frames


def _validate_bank(path, proof):
    import UnityPy
    environment = UnityPy.load(str(path))
    roots, clips = [], {}
    for obj in environment.objects:
        if obj.assets_file.unity_version != "2021.3.5f1" or int(obj.assets_file.target_platform) != 13:
            raise BuildError("Corrected audio bank lost its native Android target.")
        if obj.type.name == "AssetBundle": roots.append(obj.read_typetree())
        elif obj.type.name == "AudioClip": clips[obj.path_id] = obj.read_typetree()
        else: raise BuildError("Corrected audio bank contains an unexpected native object: " + obj.type.name)
    if len(roots) != 1 or roots[0].get("m_Name") != BANK:
        raise BuildError("Corrected audio bank lacks its native AssetBundle container.")
    references = {row[1]["asset"]["m_PathID"] for row in roots[0].get("m_Container", []) if row[1]["asset"]["m_FileID"] == 0}
    expected = {hashlib.sha256(row["name"].encode()).hexdigest()[:32]: row for row in proof}
    if references != set(clips) or {row["m_Name"] for row in clips.values()} != set(expected):
        raise BuildError("Corrected audio bank differs from its exact original clip identities.")
    for fields in clips.values():
        row = expected[fields["m_Name"]]
        if (fields["m_Channels"] != row["channels"] or fields["m_Frequency"] != row["frequency"]
                or abs(fields["m_Length"] * row["frequency"] - row["samples"]) > 2
                or fields["m_BitsPerSample"] != 16 or fields["m_LoadType"] != 0 or fields["m_CompressionFormat"] != 0
                or not fields["m_PreloadAudioData"] or fields["m_LoadInBackground"] or fields["m_Ambisonic"]):
            raise BuildError("Corrected native audio import differs from the original timeline/channels: " + row["name"])


EDITOR = r'''using System; using System.IO; using System.Text; using UnityEditor; using UnityEngine;
public static class QuestNativeAudioBuild {
 public static void Build() {
  var args=Environment.GetCommandLineArgs(); string output=args[Array.IndexOf(args,"-audioOutput")+1];
  string[] rows=File.ReadAllLines("audio-input.tsv"); var paths=new string[rows.Length];
  for(int i=0;i<rows.Length;i++) {
   string[] row=rows[i].Split('\t'); string path=row[0]; paths[i]=path;
   var importer=(AudioImporter)AssetImporter.GetAtPath(path); importer.forceToMono=false; importer.ambisonic=false;
   importer.loadInBackground=false; importer.preloadAudioData=true;
   var settings=importer.defaultSampleSettings; settings.loadType=(AudioClipLoadType)int.Parse(row[4]);
   settings.compressionFormat=AudioCompressionFormat.PCM; settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
   importer.defaultSampleSettings=settings; importer.SetOverrideSampleSettings("Android",settings); importer.SaveAndReimport();
   var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
   if(clip==null || clip.channels!=int.Parse(row[3]) || clip.frequency!=int.Parse(row[2]) || clip.samples!=int.Parse(row[1]))
    throw new InvalidDataException("Owned native audio import changed channels/rate/timeline: "+path);
   Debug.Log("GHVRQ_AUDIO "+(i+1)+"/"+rows.Length+" "+clip.name);
  }
  Directory.CreateDirectory(output);
  var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="ghvr-quest-native-audio.bundle",assetNames=paths}},
   BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.Android);
  if(manifest==null)throw new InvalidDataException("Owned Android audio bank failed");
 }
}'''


def _codecs(builder, args, source, output):
    """Use the existing pinned managed codec without importing recovery's CLI.

    Compact snapshots retain only these three compiler inputs, never a complete
    recovery project or its original game assets. The immutable executable set
    is stamp-qualified on warm runs and hashed once when a file stamp changes.
    """
    folder = source / "tools/quest-recovery/NativePortableRecovery"
    inputs = [folder / name for name in ("NativePortableRecovery.csproj", "Program.cs", "packages.lock.json")]
    key = hashlib.sha256(b"".join(path.read_bytes() for path in inputs)).hexdigest()
    root = output / "tool-cache/native-audio-codecs" / key
    retained = _receipt(root, {"sourceFingerprint": key})
    dotnet = str(builder.tool_path(args.dotnet, "dotnet"))
    if retained is None:
        builder.command([dotnet, "build", str(inputs[0]), "--output", str(root), "-p:RestoreLockedMode=true",
            "--nologo", "--verbosity", "quiet", "-p:BaseIntermediateOutputPath=" + str(root / "obj") + os.sep],
            output / "logs/native-audio-codecs.log", cwd=source)
        files = [record_file(path, path.relative_to(root).as_posix()) for path in sorted(root.iterdir())
                 if path.is_file() and path.name != "completion.json"]
        if not any(row["path"] == "NativePortableRecovery.dll" for row in files):
            raise BuildError("Pinned native audio codec did not produce its assembly.")
        for row in files: row["stamp"] = _stamp(root / row["path"])
        write_json(root / "completion.json", {"plan": {"sourceFingerprint": key}, "files": files})
    return [dotnet, str(root / "NativePortableRecovery.dll")]


def _watch(log, stop, operation):
    import re
    offset = 0
    while not stop.wait(.25):
        try:
            with log.open(encoding="utf-8", errors="replace") as stream:
                stream.seek(offset); lines = stream.readlines(); offset = stream.tell()
            for line in lines:
                match = re.search(r"GHVRQ_AUDIO (\d+)/(\d+) (.+)", line)
                if match:
                    done, total, name = match.groups()
                    build_progress.event("native-audio", 300 + int(done) * 650 // int(total), 1000,
                        "work-shares", "Import corrected original audio " + done + "/" + total + ": " + name, operation=operation)
        except OSError: pass


def produce(builder, args, source, game, output, game_info, editor):
    source, game, output, editor = [Path(path).absolute() for path in (source, game, output, editor)]
    expected = plan(source, game_info); root = output / "cache/quest-native-audio" / expected["producerKey"]
    operation = "update-art" if getattr(args, "command", None) == "update-mod" else "native-audio"
    if operation == "native-audio": build_progress.operation(operation, detail="Restore original sound-effect channels")
    retained = _receipt(root, expected)
    if retained is not None:
        build_progress.event("native-audio", 1000, 1000, "work-shares", "Retained corrected original audio bank", operation=operation)
        if operation == "native-audio": build_progress.operation(operation, complete=True, detail="Corrected original audio retained")
        return root, retained["manifest"]
    build_progress.event("native-audio", 0, 1000, "work-shares", "Restore original sound-effect channels", operation=operation, status="start")
    clips = [clip for clip in original_clips(game / "resources.assets") if clip["compression"] == 1 and clip["channels"] == 4]
    if not clips or len(clips) > 128 or any(clip["loadType"] != 0 or clip["duration"] > 60 for clip in clips):
        raise BuildError("Owned startup quad Vorbis family changed; qualify the native load contract first.")
    command = _codecs(builder, args, source, output)
    project = root / "project"; builder.project_access.wait_for_editor(project, build_progress)
    (project / "Assets/Editor").mkdir(parents=True, exist_ok=True); (project / "Assets/Audio").mkdir(exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    write_json(project / "Packages/manifest.json", {"dependencies": MODULES})
    (project / "Assets/Editor/QuestNativeAudioBuild.cs").write_text(EDITOR)
    (root / "banks").mkdir(exist_ok=True)
    rows, proof, index = [], [], ["GHVRQ_AUDIO_1"]
    bank_path = _ordinary_owned(game / "resources.resource")
    with bank_path.open("rb") as stream:
        for number, clip in enumerate(sorted(clips, key=lambda item: item["name"])):
            if clip["offset"] + clip["size"] > bank_path.stat().st_size: raise BuildError("Owned audio span exceeds resources.resource.")
            stream.seek(clip["offset"]); raw = stream.read(clip["size"])
            key = hashlib.sha256(clip["name"].encode()).hexdigest()[:32]
            input_path = root / (key + ".fsb"); path = project / ("Assets/Audio/" + key + ".ogg")
            try:
                input_path.write_bytes(raw)
                builder.command(command + ["fsb", str(input_path), str(path), str(clip["channels"])],
                    output / "logs" / ("native-audio-" + key + ".log"), cwd=source)
                frames = qualify_corrected(raw, path.read_bytes(), clip)
            finally: input_path.unlink(missing_ok=True)
            # A stable GUID also avoids reimporting retained small-project clips.
            Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + key + "\nAudioImporter:\n  serializedVersion: 7\n")
            relative = path.relative_to(project).as_posix()
            rows.append("\t".join(map(str, (relative, frames, clip["frequency"], clip["channels"], clip["loadType"]))))
            index.append("\t".join((base64.b64encode(clip["name"].encode()).decode(), relative.lower(), str(frames), str(clip["frequency"]), str(clip["channels"]))))
            proof.append({"name": clip["name"], "originalPathId": clip["pathId"], "originalResourceSha256": hashlib.sha256(raw).hexdigest(),
                          "oggSha256": digest(path), "channels": clip["channels"], "frequency": clip["frequency"], "samples": frames,
                          "compressedPacketsPreserved": True})
            build_progress.event("native-audio", (number + 1) * 300 // len(clips), 1000, "work-shares",
                                 "Restore original audio channels " + str(number + 1) + "/" + str(len(clips)) + ": " + clip["name"], operation=operation)
    (project / "audio-input.tsv").write_text("\n".join(rows) + "\n")
    log = output / "logs/native-audio-unity.log"; log.unlink(missing_ok=True)
    stop = threading.Event(); watcher = threading.Thread(target=_watch, args=(log, stop, operation), daemon=True); watcher.start()
    try:
        builder.command(builder.unity_launcher(editor) + ["-quit", "-projectPath", str(project), "-buildTarget", "Android",
            "-executeMethod", "QuestNativeAudioBuild.Build", "-audioOutput", str(root / "banks"), "-logFile", str(log)],
            output / "logs/native-audio-launch.log", cwd=source)
    finally: stop.set(); watcher.join()
    _validate_bank(root / "banks" / BANK, proof)
    (root / "banks" / INDEX).write_text("\n".join(index) + "\n")
    files = [record_file(root / "banks" / name, name) for name in (BANK, INDEX)]
    if sum(row["size"] for row in files) > 64 * 1024 * 1024: raise BuildError("Corrected original audio sidecar exceeds its bound.")
    manifest = {"schema": 1, "target": "Android", "producerKey": expected["producerKey"], "files": files}
    write_json(root / "audio-proof.json", {"schema": 1, "clips": proof, "resampled": False, "downmixed": False, "headsetVerified": False})
    saved = [{**row, "path": "banks/" + row["path"]} for row in files]
    for row in saved: row["stamp"] = _stamp(root / row["path"])
    write_json(root / "completion.json", {"plan": expected, "files": saved, "manifest": manifest})
    build_progress.event("native-audio", 1000, 1000, "work-shares", "Corrected original audio bank completed", operation=operation)
    if operation == "native-audio": build_progress.operation(operation, complete=True, detail="Corrected original audio completed")
    return root, manifest


def replacements(root, manifest):
    return {METADATA: canonical(manifest), **{PREFIX + row["path"]: Path(root) / "banks" / row["path"] for row in manifest["files"]}}


def stage(project, root, manifest):
    folder = Path(project) / "Assets/StreamingAssets"; target = folder / "QuestAudioClips"; target.mkdir(parents=True, exist_ok=True)
    for row in manifest["files"]:
        destination = target / row["path"]
        if destination.is_file() and destination.stat().st_size == row["size"] and digest(destination) == row["sha256"]: continue
        shutil.copyfile(Path(root) / "banks" / row["path"], destination)
    write_json(folder / "Quest/audio-clips.json", manifest)
