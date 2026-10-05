"""Create the local Windows hardware archive from an already verified player."""
import json
from pathlib import Path
import shutil
import zipfile

from storage import BuildError, digest, write_json


def package(repo, output, apk, details, destination):
    destination = Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    build = details["buildReport"]
    with zipfile.ZipFile(apk) as archive:
        inputs = json.loads(archive.read("assets/Quest/input-manifest.json"))
    number = inputs["mod"]["modBuild"]
    if inputs["inputKey"] != build["inputKey"] or inputs["target"] != build["target"]:
        raise BuildError("Hardware package identity differs from its verified player.")
    root = output / "hardware-package" / inputs["inputKey"] / "GloomhavenVR-Quest-Test"
    if root.exists(): shutil.rmtree(root)
    payload = root / ".planning/debug/quest3"
    payload.mkdir(parents=True)
    name = "GloomhavenVR-Quest-B" + str(number) + ".apk"
    targets = [(apk, payload / name, details["apkSha256"])]
    targets += [(output / row["path"], payload / Path(row["path"]).name, row["sha256"]) for row in details.get("contentFiles", [])]
    for source, target, expected in targets:
        shutil.copyfile(source, target)
        if digest(target) != expected:
            raise BuildError("Hardware package copy differs from verified output: " + target.name)
    handoff = {"schema": 1, **details, "apk": name, "modBuild": number, "inputKey": inputs["inputKey"],
               "contentFiles": [{"path": Path(row["path"]).name, "sha256": row["sha256"], "size": row["size"]}
                                for row in details.get("contentFiles", [])]}
    write_json(payload / "handoff.json", handoff)
    shutil.copyfile(str(apk) + ".build.json", payload / (name + ".build.json"))
    scripts = [prefix + suffix for prefix in ("install-quest-wireless", "collect-quest-logs", "quest-saves")
               for suffix in (".py", ".ps1", ".cmd")]
    for relative in ["scripts/" + name for name in scripts]:
        source = repo / relative
        if not source.is_file(): raise BuildError("Hardware installer source is missing: " + relative)
        target = root / relative; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
    for directory in ("tools/quest-installer", "tools/quest-builder"):
        for source in sorted((repo / directory).iterdir()):
            if source.is_file() and source.suffix in (".py", ".ps1", ".json", ".txt"):
                target = root / directory / source.name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
    (root / "README.txt").write_text(
        "GloomhavenVR Quest hardware package B" + str(number) + "\n\n"
        "Extract the complete GloomhavenVR-Quest-Test folder. Connect and authorize the Quest over USB on the first run.\n"
        "Run scripts/install-quest-wireless.cmd; later installations reconnect over Wi-Fi. Keep the APK and Campaign content ZIP together.\n"
        "Use scripts/collect-quest-logs.cmd after a test and scripts/quest-saves.cmd to export local saves.\n"
        "The installer provisions its own Python and Android platform-tools. Existing app data is retained.\n", encoding="utf-8")
    archive_path = destination / ("GloomhavenVR-Quest-B" + str(number) + "-Windows.zip")
    temporary = archive_path.with_suffix(".zip.new")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
            for path in sorted(root.rglob("*")):
                if path.is_file(): archive.write(path, "GloomhavenVR-Quest-Test/" + path.relative_to(root).as_posix())
        with zipfile.ZipFile(temporary) as archive:
            expected = {"GloomhavenVR-Quest-Test/" + path.relative_to(root).as_posix(): path.stat().st_size
                        for path in root.rglob("*") if path.is_file()}
            actual = {row.filename: row.file_size for row in archive.infolist()}
            if actual != expected: raise BuildError("Windows hardware ZIP lost a required installer or content file.")
        temporary.replace(archive_path)
        for source in payload.iterdir():
            target = destination / source.name
            shutil.copyfile(source, target)
        receipt = {"schema": 1, "archive": archive_path.name, "sha256": digest(archive_path), "size": archive_path.stat().st_size,
                   "modBuild": number, "inputKey": inputs["inputKey"], "target": inputs["target"]}
        write_json(destination / "windows-package.json", receipt)
    finally:
        temporary.unlink(missing_ok=True)
        shutil.rmtree(root)
    return archive_path
