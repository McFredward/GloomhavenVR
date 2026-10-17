"""Real metadata recommendation for Chrome; no APK, ZIP or PC ownership test.

Keep this independent from the Python unit fixture: the browser must consume
the actual recommendation after a long retained-content update history, rather
than proving only that a hand-written ``inputsReady`` boolean is rendered.
"""
from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
import state
import update_recommendation as hints


def recommendations():
    with tempfile.TemporaryDirectory(prefix="quest-home-metadata-") as temporary:
        root = Path(temporary)
        store = state.Store(root / ".ghvrq")
        build = store.root / "build"
        game = root / "Owned game"
        game.mkdir()
        (game / "globalgamemanagers").write_bytes(b"PC installation fixture")
        games = [{"id": "owned", "provider": "gog", "gameRoot": str(game)}]
        source = {"kind": "bundled-release", "modBuild": 683,
                  "modVersion": "1.1.1-dev", "sourceCommit": "a" * 40}
        input_key, apk_sha, bank_sha = "b" * 64, "c" * 64, "d" * 64
        bank = None
        previous = None
        for index in range(13):
            key = format(index + 1, "064x")
            apk = build / ("updates" if previous else "builds") / key / "GloomhavenVR-Quest.apk"
            apk.parent.mkdir(parents=True)
            apk.write_bytes(b"Metadata-only APK fixture; never read.")
            relative = apk.relative_to(build).as_posix()
            details = {"inputKey": input_key, "modBuild": 670 + index,
                       "package": "dev.gloomhavenvr.quest", "isDiagnostic": False,
                       "apkSha256": apk_sha}
            rows = [{"path": relative, "size": apk.stat().st_size, "sha256": apk_sha}]
            if previous:
                details.update(contentFiles=[], retainedGameContent=True, baseApk=str(previous))
            else:
                bank = apk.parent / "GloomhavenVR-Quest-content.zip"
                bank.write_bytes(b"Metadata-only content fixture; never read.")
                details["contentFiles"] = [{"path": bank.relative_to(build).as_posix(),
                                            "size": bank.stat().st_size, "sha256": bank_sha}]
                details["installationContract"] = {"schema": 1, "inputKey": input_key,
                    "gameArchiveSha256": bank_sha, "modArchiveSha256": "e" * 64,
                    "modFileCount": 3, "modEntryBytesVerified": True}
                rows.extend(details["contentFiles"])
            receipt = "receipts/build/" + key + ".json"
            state.atomic_json(build / receipt, {"schema": 1, "stage": "build", "key": key,
                                                "details": details, "outputs": rows})
            state.atomic_json(build / "latest-build.json", {"schema": 1, "apk": relative,
                                                           "receipt": receipt, "details": details})
            previous = apk
        selected = discovery.default_apk(store)
        assert selected and selected["path"] == str(apk) and selected["modBuild"] == 682

        def game_data(path):
            if path != game or not (path / "globalgamemanagers").is_file():
                raise ValueError("No fixture PC installation.")
            return path

        helper = SimpleNamespace(game_data=game_data)
        original_open = Path.open
        opened = []

        def metadata_only(path, *args, **kwargs):
            assert path.suffix == ".json", "Homepage opened payload bytes: " + str(path)
            opened.append(path.relative_to(build).as_posix())
            return original_open(path, *args, **kwargs)

        def observe(name):
            opened.clear()
            with patch.object(Path, "open", metadata_only), \
                 patch.object(Path, "glob", side_effect=AssertionError("No directory scan")), \
                 patch.object(Path, "rglob", side_effect=AssertionError("No recursive scan")), \
                 patch.object(Path, "iterdir", side_effect=AssertionError("No directory enumeration")), \
                 patch.object(state, "digest", side_effect=AssertionError("No payload hashing")):
                recommendation = hints.recommend(ROOT, store, source, selected, games, [], helper)
            return {"name": name, "receiptCount": 13, "metadataReads": list(opened),
                    "discovery": {"schema": 1, "event": "discovery", "games": games,
                        "defaultApk": selected, "modSource": dict(source),
                        "updateRecommendation": recommendation}}

        cases = [observe("twelve-retained-updates")]
        source["modBuild"] = 682
        cases.append(observe("current-runtime"))
        source["modBuild"] = 683
        bank.unlink()
        cases.append(observe("missing-content"))
        bank.write_bytes(b"Metadata-only content fixture; never read.")
        details["baseApk"] = str(apk)
        state.atomic_json(build / receipt, {"schema": 1, "stage": "build", "key": key,
                                            "details": details, "outputs": rows})
        cases.append(observe("cyclic-base"))
        return cases


if __name__ == "__main__":
    print(json.dumps(recommendations()))
