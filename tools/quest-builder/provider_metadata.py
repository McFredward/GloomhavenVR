"""Read local installation evidence, never logins, credentials or store APIs.

GOG documents DLC mini-manifests as installed-entitlement markers, including
offline installs. Epic's installation records identify the selected copy, but
bundled DLC bytes and a base-game .item do not identify DLC ownership. Accounts
are separate: installation IDs are not user IDs and never become APK profiles.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re

from storage import BuildError, digest

GOG_DLC_DOCUMENTATION = "https://docs.gog.com/sdk-dlc-discovery/"
EPIC_MANIFEST_DOCUMENTATION = "https://dev.epicgames.com/documentation/unreal-engine/academic-installation-of-unreal-engine"
EPIC_DLC_DOCUMENTATION = "https://www.epicgames.com/help/c-32735058/c-Trending_0/epic-games-launcherdlc-a12640887"
TITLES = {
    "gloomhaven": None,
    "gloomhavenjawsofthelion": "jotl",
    "gloomhavenjawsofthelionalternativeskins": "jotl-skins",
    "gloomhavensoloscenariospackmercenarychallenges": "solo",
    "gloomhavensoloscenariosmercenarychallenges": "solo",
}


def title_key(name: str) -> str | None:
    normalized = re.sub(r"[^a-z0-9]", "", name.lower()) if isinstance(name, str) else ""
    if normalized not in TITLES:
        raise BuildError("Unrecognized Gloomhaven installation/DLC product title; use explicit local DLC ownership.")
    return TITLES[normalized]


def game_root(game: Path) -> Path:
    game = game.resolve()
    return game.parent if game.name.lower().endswith("_data") or (game / "Managed/GH.Runtime.dll").is_file() else game


def _small_json(path: Path) -> dict:
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 128 * 1024:
        raise BuildError("Local provider installation metadata is missing, linked or unexpectedly large.")
    try:
        result = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise BuildError("Invalid local provider installation metadata: " + path.name) from error
    if not isinstance(result, dict):
        raise BuildError("Local provider installation metadata must be an object: " + path.name)
    return result


def gog_installation(game: Path) -> dict | None:
    root = game_root(game)
    files = sorted(root.glob("goggame-*.info"))
    if not files:
        return None
    products = []
    for path in files:
        match = re.fullmatch(r"goggame-([1-9][0-9]{0,19})\.info", path.name)
        data = _small_json(path)
        if not match or str(data.get("gameId")) != match.group(1):
            raise BuildError("GOG mini-manifest product ID differs from its filename.")
        key = title_key(data.get("name", ""))
        root_id = str(data.get("rootGameId", data["gameId"]))
        if not re.fullmatch(r"[1-9][0-9]{0,19}", root_id):
            raise BuildError("GOG mini-manifest has no valid base-product association.")
        products.append({"id": match.group(1), "rootId": root_id, "key": key,
                         "file": path.name, "sha256": digest(path)})
    bases = [product for product in products if product["key"] is None]
    if len(bases) != 1 or bases[0]["rootId"] != bases[0]["id"]:
        raise BuildError("GOG installation needs one unambiguous Gloomhaven base mini-manifest.")
    base = bases[0]
    if any(product["rootId"] != base["id"] for product in products):
        raise BuildError("GOG DLC mini-manifest belongs to a different base game.")
    keys = [product["key"] for product in products if product["key"] is not None]
    if len(keys) != len(set(keys)):
        raise BuildError("Multiple GOG product IDs describe the same DLC; ownership is ambiguous.")
    return {"provider": "gog", "baseProductId": base["id"], "ownedDlcKeys": sorted(keys),
            "ownershipComplete": True, "source": "gog-installed-mini-manifests",
            "evidence": products, "documentation": GOG_DLC_DOCUMENTATION,
            "profileAvailable": False}


def epic_manifest_root() -> Path | None:
    program_data = os.environ.get("PROGRAMDATA")
    if program_data:
        candidate = Path(program_data) / "Epic/EpicGamesLauncher/Data/Manifests"
        if candidate.is_dir():
            return candidate
    return None


def epic_installation(game: Path, manifest_root: Path | None = None) -> dict | None:
    root = game_root(game)
    manifest_root = manifest_root or epic_manifest_root()
    records = []
    if manifest_root:
        if not manifest_root.is_dir() or manifest_root.is_symlink():
            raise BuildError("Epic manifest directory is invalid or linked.")
        for path in sorted(manifest_root.glob("*.item")):
            data = _small_json(path)
            location = data.get("InstallLocation")
            if not isinstance(location, str) or Path(location).resolve() != root:
                continue
            if title_key(data.get("DisplayName", "")) is not None:
                # A launcher add-on installation record is not a complete account
                # entitlement export; never infer other DLC from its absence.
                continue
            fields = ("AppName", "CatalogItemId", "CatalogNamespace")
            if any(not isinstance(data.get(field), str) or not data[field] or len(data[field]) > 256 for field in fields):
                raise BuildError("Epic installation record lacks its original product identifiers.")
            if data.get("MainGameAppName") not in (None, "", data["AppName"]):
                raise BuildError("Epic Gloomhaven base record unexpectedly refers to another application.")
            records.append({"file": path.name, "sha256": digest(path),
                            **{field: data[field] for field in fields}})
    # The game's local installation marker records original catalog identifiers, not
    # an account. It can identify a moved installation without a live launcher.
    markers = sorted((root / ".egstore").glob("*.mancpn")) if (root / ".egstore").is_dir() else []
    for path in markers:
        data = _small_json(path)
        if not all(isinstance(data.get(field), str) and data[field] for field in ("AppName", "CatalogItemId", "CatalogNamespace")):
            raise BuildError("Epic installation marker lacks its catalog identifiers.")
        marker = {field: data[field] for field in ("AppName", "CatalogItemId", "CatalogNamespace")}
        if records and any(any(record[field] != marker[field] for field in marker) for record in records):
            raise BuildError("Epic launcher and selected game installation markers disagree.")
        records.append({"file": ".egstore/" + path.name, "sha256": digest(path), **marker})
    if not records:
        return None
    identities = {(row["AppName"], row["CatalogItemId"], row["CatalogNamespace"]) for row in records}
    if len(identities) != 1:
        raise BuildError("Epic installation product association is ambiguous.")
    return {"provider": "epic", "baseProductId": records[0]["CatalogItemId"],
            "ownedDlcKeys": [], "ownershipComplete": False,
            "source": "epic-local-installation-records", "evidence": records,
            "documentation": EPIC_MANIFEST_DOCUMENTATION, "dlcDocumentation": EPIC_DLC_DOCUMENTATION,
            "profileAvailable": False}


def installation(game: Path, provider: str, metadata_root: Path | None = None) -> dict | None:
    if provider == "gog":
        return gog_installation(game)
    if provider == "epic":
        return epic_installation(game, metadata_root)
    return None
