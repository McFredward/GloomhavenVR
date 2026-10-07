"""Validate immutable authored Android mod banks, including full Campaign town art."""
from __future__ import annotations

import json
from pathlib import Path

from storage import BuildError, record_file, verify_files

MAIN = "gloomhavenvr.bundle"
TOWN = "ghvr-town.bundle"
VOICES = "ghvr-town-voices.bundle"
TOWN_ROOT = "Assets/Bundle/TownServices/"
REQUIRED_TOWN = (
    TOWN_ROOT + "Prefabs/TownMerchant.prefab", TOWN_ROOT + "Prefabs/TownPriestess.prefab",
    TOWN_ROOT + "Prefabs/TownEnchantress.prefab", TOWN_ROOT + "Prefabs/TownWorkTray.prefab",
    TOWN_ROOT + "Shaders/TownNpc.shader", TOWN_ROOT + "Shaders/TownEye.shader",
    TOWN_ROOT + "Shaders/TownCornea.shader", TOWN_ROOT + "Shaders/TownFlame.shader",
    TOWN_ROOT + "town-facial-rig-contract.json",
)


def _safe_path(value: object, roots: tuple[str, ...]) -> bool:
    return (isinstance(value, str) and value.startswith(roots) and "\\" not in value and ":" not in value
            and all(part not in ("", ".", "..") for part in value.split("/")))


def _names(value: object, what: str) -> list[str]:
    if not isinstance(value, list) or not value or not all(isinstance(name, str) for name in value) or len(value) != len(set(value)):
        raise BuildError("Authored Android " + what + " names are empty or duplicated.")
    return value


def bundle_records(receipt: dict) -> list[dict]:
    """Return loader-order file records; legacy startup receipts stay compatible."""
    values = receipt.get("bundles")
    if values is None:
        values = [receipt.get("bundle")]
    if not isinstance(values, list) or not values or not all(isinstance(row, dict) for row in values):
        raise BuildError("Authored Android bundle receipt is empty or invalid.")
    names = [row.get("path") for row in values]
    if any(name not in (MAIN, TOWN, VOICES) for name in names) or len(names) != len(set(names)) or names[0] != MAIN:
        raise BuildError("Authored Android bundle filenames are unexpected, duplicated or out of loader order.")
    if values[0] != receipt.get("bundle"):
        raise BuildError("Legacy main-bank receipt differs from the declared bundle set.")
    return values


def validate_bundle_set(folder: Path, authored: Path, source_files: list[dict], *, full_game: bool = False) -> dict:
    """Verify bank bytes, exact source selection, shader includes and native contract."""
    try:
        receipt = json.loads((folder / "quest-mod-bundles.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise BuildError("Authored Android mod bank receipt is missing or unreadable.") from error
    contract = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "bundleName": MAIN,
                "graphicsApi": "Vulkan" if full_game else "OpenGLES3", "colorSpace": "Linear", "stereoRenderingPath": "SinglePass",
                "typeTreesEnabled": True, "chunkBasedCompression": True, "townBanksIncluded": full_game}
    if not isinstance(receipt, dict) or any(receipt.get(name) != value for name, value in contract.items()):
        raise BuildError("Authored mod banks have an unsupported Android rendering/full-game contract.")
    records = bundle_records(receipt)
    expected = [MAIN, TOWN, VOICES] if full_game else [MAIN]
    if [row["path"] for row in records] != expected:
        raise BuildError("Full Campaign requires its complete main, town art and town voice Android bank set.")
    known = {row["path"]: row for row in source_files}
    declared = receipt.get("sourceFiles")
    if not isinstance(declared, list) or not declared or not all(isinstance(row, dict) for row in declared):
        raise BuildError("Authored mod bank compiler-input receipt is empty or invalid.")
    names = [row.get("path") for row in declared]
    if not all(isinstance(name, str) for name in names) or len(names) != len(set(names)):
        raise BuildError("Authored mod bank compiler-input receipt is duplicated.")
    roots = ("Assets/", "Packages/", "ProjectSettings/") if full_game else ("Assets/",)
    for row in declared:
        if not _safe_path(row.get("path"), roots) or row != known.get(row["path"]):
            raise BuildError("Authored mod banks reference unknown or changed compiler input.")
    if not verify_files(authored, declared):
        raise BuildError("Authored mod art changed during Android compilation.")
    source_names = set(names)
    for row in records:
        path = folder / row["path"]
        if path.is_symlink() or not path.is_file() or row != record_file(path, row["path"]):
            raise BuildError("Authored Android bank bytes differ from their receipt: " + row["path"])
        with path.open("rb") as stream:
            if stream.read(8) != b"UnityFS\0":
                raise BuildError("Authored mod bank is not a real Unity AssetBundle: " + row["path"])

    banks = receipt.get("banks")
    if banks is None and not full_game:
        banks = [{"bundleName": MAIN, "assetNames": receipt.get("assetNames"),
                  "requiredAssetNames": receipt.get("requiredAssetNames"), "dependencies": [], "bundle": receipt["bundle"]}]
    if not isinstance(banks, list) or not all(isinstance(bank, dict) for bank in banks) or [bank.get("bundleName") for bank in banks] != expected:
        raise BuildError("Authored Android per-bank source selection is missing or incomplete.")
    assets_by_bank = {}
    for bank, row in zip(banks, records):
        name = bank["bundleName"]
        assets = _names(bank.get("assetNames"), name + " asset")
        required = _names(bank.get("requiredAssetNames"), name + " required asset")
        if not set(required).issubset(assets) or not set(assets).issubset(source_names) or bank.get("bundle") != row:
            raise BuildError("Authored mod bank is missing required or proven source assets: " + name)
        if any(not _safe_path(asset, ("Assets/Bundle/",)) for asset in assets):
            raise BuildError("Authored mod bank has an unsafe source asset path: " + name)
        dependencies = bank.get("dependencies")
        if (not isinstance(dependencies, list) or not all(isinstance(dependency, str) for dependency in dependencies)
                or len(dependencies) != len(set(dependencies))
                or any(dependency not in expected or dependency == name for dependency in dependencies)):
            raise BuildError("Authored Android bank requires an unshipped or invalid dependency: " + name)
        if name == MAIN:
            if any(asset.startswith(TOWN_ROOT) for asset in assets) or dependencies:
                raise BuildError("Main Android mod bank unexpectedly depends on a town bank.")
            if assets != receipt.get("assetNames") or required != receipt.get("requiredAssetNames"):
                raise BuildError("Legacy main-bank source selection differs from the bank set.")
            if full_game:
                selected = {path for path in known if path.startswith("Assets/Bundle/") and not path.startswith(TOWN_ROOT)
                            and Path(path).suffix.lower() not in (".md", ".txt", ".gitkeep", ".meta", ".cginc")
                            and not Path(path).name.startswith(".") and path + ".meta" in known}
                if not selected.issubset(assets):
                    raise BuildError("Full Android main bank omits current authored mod art.")
        elif name == TOWN:
            selected = {path for path in known if (path.startswith(TOWN_ROOT + "Prefabs/") and path.endswith(".prefab")
                        or path.startswith(TOWN_ROOT + "Shaders/") and path.endswith(".shader")
                            and "/" not in path[len(TOWN_ROOT + "Shaders/"):]
                        or path == TOWN_ROOT + "town-facial-rig-contract.json")}
            if not set(REQUIRED_TOWN).issubset(required) or set(assets) != selected:
                raise BuildError("Town Android art bank omits its original authored prefab/shader/facial selection.")
        else:
            selected = {path for path in known if path.startswith(TOWN_ROOT + "Audio/") and Path(path).suffix.lower() in (".wav", ".json")}
            if set(assets) != selected or set(required) != selected or not any(path.lower().endswith(".wav") for path in assets) or not any(path.lower().endswith(".json") for path in assets):
                raise BuildError("Town Android voice bank omits its original clips or metadata selection.")
        assets_by_bank[name] = set(assets)
    for index, name in enumerate(expected):
        if any(assets_by_bank[name] & assets_by_bank[other] for other in expected[:index]):
            raise BuildError("One authored source asset is assigned to multiple Android banks.")
    if full_game:
        shader_includes = {path for path in known if path.startswith("Assets/Bundle/") and path.endswith(".cginc")}
        if not shader_includes.issubset(source_names):
            raise BuildError("Full Android mod bank receipt omits authored shader compiler include text.")
        for asset in set.union(*assets_by_bank.values()):
            if asset + ".meta" in known and asset + ".meta" not in source_names:
                raise BuildError("Full Android mod bank receipt omits an authored asset import/GUID contract.")
    return receipt
