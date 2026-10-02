"""Offline identity capture. No Steam API, tokens or personal avatar requests."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import struct


class ProfileError(ValueError):
    """A local account selection or resource is invalid."""


STEAM_INDIVIDUAL_BASE = 76561197960265728


def dummy_identity() -> dict:
    """Explicit maintainer-authorized hardware-test identity, never a real ID."""
    return {"schema": 1, "provider": "steam", "steamId": "0", "accountId": 0,
            "displayName": "Quest Local Test (DUMMY)", "isDummy": True,
            "source": "maintainer-authorized-dummy"}


def validate_identity(value: dict) -> dict:
    """Preserve the full ID separately from its uint32 account number."""
    if not isinstance(value, dict):
        raise ProfileError("The local profile must be a JSON object.")
    if set(value) - {"schema", "provider", "steamId", "accountId", "displayName", "source", "logoSha256"}:
        raise ProfileError("Unexpected profile fields; provide identity only, without credentials.")
    raw = value.get("steamId")
    if not isinstance(raw, str) or not re.fullmatch(r"[0-9]{17}", raw):
        raise ProfileError("steamId must be the full 17-digit Steam individual ID as a string.")
    number = int(raw)
    account = number - STEAM_INDIVIDUAL_BASE
    if not 0 < account <= 0xFFFFFFFF:
        raise ProfileError("steamId is not a public-universe individual account ID.")
    if "accountId" in value and (type(value["accountId"]) is not int or value["accountId"] != account):
        raise ProfileError("accountId does not match the full Steam ID.")
    name = value.get("displayName")
    if not isinstance(name, str) or not name.strip() or len(name.encode("utf-8")) > 256:
        raise ProfileError("displayName must be non-empty and at most 256 UTF-8 bytes.")
    if any(ord(c) < 32 or ord(c) == 127 for c in name):
        raise ProfileError("displayName contains control characters.")
    if value.get("schema", 1) != 1 or value.get("provider", "steam") != "steam":
        raise ProfileError("Only the local Steam profile schema 1 is supported.")
    return {"schema": 1, "provider": "steam", "steamId": raw,
            "accountId": account, "displayName": name, "source": "explicit-local-profile"}


def parse_vdf(raw: str) -> dict:
    """Read quoted KeyValues objects, comments and escaped quotes strictly."""
    token = re.compile(r'\s+|//[^\n]*|"((?:[^"\\]|\\.)*)"|([{}])')
    values = []
    pos = 0
    while pos < len(raw):
        match = token.match(raw, pos)
        if not match:
            raise ProfileError("Unsupported or malformed loginusers.vdf syntax.")
        pos = match.end()
        if match.group(1) is not None:
            values.append(re.sub(r'\\([\\"])', r'\1', match.group(1)))
        elif match.group(2):
            values.append(match.group(2))
    index = 0

    def object_body(nested: bool = False) -> dict:
        nonlocal index
        result = {}
        while index < len(values):
            key = values[index]
            index += 1
            if key == "}":
                if not nested:
                    raise ProfileError("Unexpected VDF closing brace.")
                return result
            if key == "{" or index >= len(values):
                raise ProfileError("Missing VDF value.")
            value = values[index]
            index += 1
            if value == "{":
                value = object_body(True)
            elif value == "}":
                raise ProfileError("Missing VDF value before closing brace.")
            if key in result:
                raise ProfileError("Duplicate VDF key; account selection is ambiguous.")
            result[key] = value
        if nested:
            raise ProfileError("Missing VDF closing brace.")
        return result

    return object_body()


def capture_steam_profile(steam_root: Path, selected_id: str | None) -> dict:
    path = steam_root / "config" / "loginusers.vdf"
    if not path.is_file():
        raise ProfileError("Steam config/loginusers.vdf was not found; supply --profile-json instead.")
    users = parse_vdf(path.read_text(encoding="utf-8-sig")).get("users")
    if not isinstance(users, dict) or not users:
        raise ProfileError("The local Steam account cache is empty.")
    # A remembered account is not evidence of which account should personalize the APK.
    # MostRecent also changes under account switching; never choose it implicitly.
    if selected_id is None:
        if len(users) != 1:
            raise ProfileError("Multiple cached Steam accounts; select one explicitly with --steam-id.")
        selected_id = next(iter(users))
    entry = users.get(selected_id)
    if not isinstance(entry, dict):
        raise ProfileError("The selected Steam account is not present in loginusers.vdf.")
    result = validate_identity({"steamId": selected_id, "displayName": entry.get("PersonaName")})
    result["source"] = "loginusers.vdf"
    return result


def read_logo(path: Path) -> tuple[bytes, str]:
    raw = path.read_bytes()
    if len(raw) < 33 or len(raw) > 4 * 1024 * 1024 or raw[:8] != b"\x89PNG\r\n\x1a\n":
        raise ProfileError("The static Steam logo must be a PNG of at most 4 MiB.")
    if raw[12:16] != b"IHDR":
        raise ProfileError("The Steam logo PNG is missing its image header.")
    width, height = struct.unpack(">II", raw[16:24])
    if not 1 <= width <= 4096 or not 1 <= height <= 4096:
        raise ProfileError("The Steam logo image dimensions are invalid or too large.")
    return raw, hashlib.sha256(raw).hexdigest()


def load_profile(profile_json: Path | None, steam_root: Path | None,
                 selected_id: str | None, logo: Path) -> tuple[dict, bytes]:
    if bool(profile_json) == bool(steam_root):
        raise ProfileError("Supply either --profile-json or --steam-root, exactly one.")
    if profile_json:
        if profile_json.stat().st_size > 4096:
            raise ProfileError("The identity JSON is unexpectedly large; do not supply an account export.")
        result = validate_identity(json.loads(profile_json.read_text(encoding="utf-8-sig")))
    else:
        result = capture_steam_profile(steam_root, selected_id)
    raw, logo_hash = read_logo(logo)
    result["logoSha256"] = logo_hash
    return result, raw
