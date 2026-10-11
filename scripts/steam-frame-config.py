#!/usr/bin/env python3
"""Install the Steam-side GloomhavenVR Frame shortcut without third-party modules.

Steam does not expose a supported command-line shortcut editor. This helper edits
the account's two local KeyValues files only while Steam is stopped. It patches
the original text file and the binary shortcut file in place at field boundaries,
leaving unrelated settings and shortcuts byte-for-byte intact.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile
import time
import zlib
from dataclasses import dataclass


GAME_APP_ID = "780290"
SHORTCUT_NAME = "GloomhavenVR"
STEAM_ID64_BASE = 76561197960265728
OVERRIDE = 'WINEDLLOVERRIDES="winhttp=n,b"'
MAX_CONFIG_BYTES = 64 * 1024 * 1024


class ConfigError(Exception):
    """A Steam configuration cannot be safely identified or modified."""


@dataclass
class TextToken:
    value: str
    kind: str
    start: int
    end: int


@dataclass
class TextNode:
    key: str
    value: str | None
    value_token: TextToken | None
    children: list[TextNode] | None
    close: int | None


def quoted(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def tokenize_text(data: str) -> list[TextToken]:
    tokens: list[TextToken] = []
    pos = 0
    while pos < len(data):
        if data[pos].isspace():
            pos += 1
        elif data.startswith("//", pos):
            end = data.find("\n", pos)
            pos = len(data) if end < 0 else end
        elif data[pos] in "{}":
            tokens.append(TextToken(data[pos], data[pos], pos, pos + 1))
            pos += 1
        elif data[pos] == '"':
            start = pos
            pos += 1
            value: list[str] = []
            while pos < len(data):
                ch = data[pos]
                if ch == '"':
                    pos += 1
                    break
                if ch == "\\" and pos + 1 < len(data):
                    nxt = data[pos + 1]
                    if nxt in ('"', "\\"):
                        value.append(nxt)
                        pos += 2
                        continue
                value.append(ch)
                pos += 1
            else:
                raise ConfigError("Unterminated quoted field in Steam text VDF")
            tokens.append(TextToken("".join(value), "word", start, pos))
        else:
            start = pos
            while pos < len(data) and not data[pos].isspace() and data[pos] not in '{}"':
                pos += 1
            if pos == start:
                raise ConfigError("Unexpected token in Steam text VDF")
            tokens.append(TextToken(data[start:pos], "word", start, pos))
    return tokens


def parse_text(data: str) -> list[TextNode]:
    tokens = tokenize_text(data)
    index = 0

    def entries(nested: bool) -> tuple[list[TextNode], int | None]:
        nonlocal index
        result: list[TextNode] = []
        while index < len(tokens):
            key = tokens[index]
            if key.kind == "}":
                if not nested:
                    raise ConfigError("Unexpected closing brace in Steam text VDF")
                index += 1
                return result, key.start
            if key.kind != "word" or index + 1 >= len(tokens):
                raise ConfigError("Invalid key/value pair in Steam text VDF")
            index += 1
            value = tokens[index]
            index += 1
            if value.kind == "{":
                children, close = entries(True)
                result.append(TextNode(key.value, None, None, children, close))
            elif value.kind == "word":
                result.append(TextNode(key.value, value.value, value, None, None))
            else:
                raise ConfigError("Invalid value in Steam text VDF")
        if nested:
            raise ConfigError("Unterminated object in Steam text VDF")
        return result, None

    parsed, _ = entries(False)
    return parsed


def one_child(nodes: list[TextNode], key: str) -> TextNode | None:
    matches = [node for node in nodes if node.key.casefold() == key.casefold()]
    if len(matches) > 1:
        raise ConfigError(f"Duplicate {key} keys in Steam VDF")
    return matches[0] if matches else None


def find_path(nodes: list[TextNode], path: list[str]) -> tuple[TextNode | None, int]:
    current = nodes
    for depth, part in enumerate(path):
        node = one_child(current, part)
        if node is None:
            return None, depth
        if node.children is None:
            raise ConfigError(f"Steam VDF {part} is not an object")
        current = node.children
    return node, len(path)


def update_launch_options(existing: str) -> str:
    # A leftover flag on the original entry would defeat Frame's flat/VR split.
    existing = re.sub(r"(?<!\S)--gloomhavenvr(?=\s|$)", "", existing, flags=re.IGNORECASE).strip()
    first_command = True

    def command_once(match: re.Match[str]) -> str:
        nonlocal first_command
        if first_command:
            first_command = False
            return "%command%"
        return ""

    existing = re.sub(r"%command%", command_once, existing, flags=re.IGNORECASE).strip()
    if not existing:
        return f"{OVERRIDE} %command%"
    match = re.search(r"(?<!\S)WINEDLLOVERRIDES=(?:\"([^\"]*)\"|'([^']*)'|(\S+))", existing)
    if match:
        overrides = next((group for group in match.groups() if group is not None), "")
        parts = overrides.split(";")
        found = False
        for index, part in enumerate(parts):
            if part.split("=", 1)[0].casefold() == "winhttp":
                parts[index] = "winhttp=n,b"
                found = True
        if not found:
            parts.append("winhttp=n,b")
        merged = ";".join(part for part in parts if part)
        result = existing[:match.start()] + f'WINEDLLOVERRIDES="{merged}"' + existing[match.end():]
        if "%command%" not in result:
            insertion = match.start() + len(f'WINEDLLOVERRIDES="{merged}"')
            result = result[:insertion] + " %command%" + result[insertion:]
        return result
    if "%command%" in existing:
        return f"{OVERRIDE} {existing}"
    return f"{OVERRIDE} %command% {existing}"


def patch_localconfig(raw: bytes) -> bytes:
    data = raw.decode("utf-8", "surrogateescape")
    path = ["UserLocalConfigStore", "Software", "Valve", "Steam", "Apps", GAME_APP_ID]
    parsed = parse_text(data)
    root = one_child(parsed, path[0])
    if root is None or root.children is None:
        raise ConfigError("Steam localconfig.vdf has no UserLocalConfigStore object")
    parent = root
    for depth, part in enumerate(path[1:], 1):
        child = one_child(parent.children or [], part)
        if child is None:
            if parent.close is None:
                raise ConfigError("Steam localconfig.vdf has an invalid object")
            newline = "\r\n" if "\r\n" in data else "\n"
            indent = "\t" * depth
            remaining = path[depth:]
            lines = []
            for level, name in enumerate(remaining, depth):
                lines.extend(["\t" * level + quoted(name), "\t" * level + "{"])
            lines.append("\t" * (depth + len(remaining)) + quoted("LaunchOptions") + "\t\t" + quoted(update_launch_options("")))
            for level in range(depth + len(remaining) - 1, depth - 1, -1):
                lines.append("\t" * level + "}")
            addition = newline + newline.join(lines) + newline + "\t" * (depth - 1)
            return (data[:parent.close] + addition + data[parent.close:]).encode("utf-8", "surrogateescape")
        if child.children is None:
            raise ConfigError(f"Steam localconfig.vdf {part} is not an object")
        parent = child
    launch = one_child(parent.children or [], "LaunchOptions")
    if launch is not None:
        if launch.value_token is None or launch.value is None:
            raise ConfigError("Steam LaunchOptions is not a string")
        new = update_launch_options(launch.value)
        if new == launch.value:
            return raw
        token = launch.value_token
        result = data[:token.start] + quoted(new) + data[token.end:]
    else:
        if parent.close is None:
            raise ConfigError("Steam game entry has no closing brace")
        newline = "\r\n" if "\r\n" in data else "\n"
        result = (data[:parent.close] + newline + "\t" * 6 + quoted("LaunchOptions")
                  + "\t\t" + quoted(update_launch_options("")) + newline + "\t" * 5
                  + data[parent.close:])
    return result.encode("utf-8", "surrogateescape")


@dataclass
class BinNode:
    key: str
    kind: int
    value: str | int | bytes | None
    children: list[BinNode] | None
    start: int
    end: int
    close: int | None


def read_c_string(data: bytes, pos: int) -> tuple[str, int]:
    end = data.find(b"\0", pos)
    if end < 0:
        raise ConfigError("Unterminated string in Steam binary VDF")
    return data[pos:end].decode("utf-8", "surrogateescape"), end + 1


def parse_binary(data: bytes) -> list[BinNode]:
    pos = 0

    def entries(nested: bool) -> tuple[list[BinNode], int | None]:
        nonlocal pos
        result: list[BinNode] = []
        while pos < len(data):
            start = pos
            kind = data[pos]
            pos += 1
            if kind == 8:
                if not nested:
                    # Steam normally includes one implicit-root terminator.
                    if pos != len(data) and any(byte != 8 for byte in data[pos:]):
                        raise ConfigError("Trailing data in Steam binary VDF")
                    return result, start
                return result, start
            key, pos = read_c_string(data, pos)
            close = None
            children = None
            if kind == 0:
                children, close = entries(True)
                value = None
            elif kind == 1:
                value, pos = read_c_string(data, pos)
            elif kind in (2, 3, 4, 6):
                if pos + 4 > len(data):
                    raise ConfigError("Truncated Steam binary VDF field")
                value = struct.unpack_from("<i", data, pos)[0] if kind == 2 else data[pos:pos + 4]
                pos += 4
            elif kind in (7, 10):
                if pos + 8 > len(data):
                    raise ConfigError("Truncated Steam binary VDF field")
                value = data[pos:pos + 8]
                pos += 8
            elif kind == 5:
                # Valve's WString payload is UTF-16LE terminated by two zero bytes.
                while pos + 1 < len(data) and data[pos:pos + 2] != b"\0\0":
                    pos += 2
                if pos + 1 >= len(data):
                    raise ConfigError("Truncated Steam binary VDF wide string")
                pos += 2
                value = data[start:pos]
            else:
                raise ConfigError(f"Unsupported Steam binary VDF type {kind}")
            result.append(BinNode(key, kind, value, children, start, pos, close))
        if nested:
            raise ConfigError("Unterminated object in Steam binary VDF")
        return result, None

    nodes, _ = entries(False)
    return nodes


def bin_field(nodes: list[BinNode], key: str) -> BinNode | None:
    found = [node for node in nodes if node.key.casefold() == key.casefold()]
    if len(found) > 1:
        raise ConfigError(f"Duplicate {key} fields in Steam shortcuts.vdf")
    return found[0] if found else None


def bin_string(key: str, value: str) -> bytes:
    return b"\x01" + key.encode("utf-8") + b"\0" + value.encode("utf-8") + b"\0"


def bin_int(key: str, value: int) -> bytes:
    return b"\x02" + key.encode("utf-8") + b"\0" + struct.pack("<I", value & 0xFFFFFFFF)


def bin_object(key: str, contents: bytes) -> bytes:
    return b"\x00" + key.encode("utf-8") + b"\0" + contents + b"\x08"


def shortcut_appid(exe: str) -> int:
    return zlib.crc32((exe + SHORTCUT_NAME).encode("utf-8")) | 0x80000000


def patch_shortcuts(raw: bytes, launcher: Path, icon: Path) -> tuple[bytes, int]:
    if not raw:
        raw = bin_object("shortcuts", b"") + b"\x08"
    roots = parse_binary(raw)
    shortcuts = [node for node in roots if node.key.casefold() == "shortcuts"]
    if len(shortcuts) != 1 or shortcuts[0].children is None or shortcuts[0].close is None:
        raise ConfigError("Steam shortcuts.vdf has no valid shortcuts root")
    root = shortcuts[0]
    entries = root.children
    for entry in entries:
        if entry.kind != 0 or entry.children is None or not entry.key.isdigit():
            raise ConfigError("Unexpected shortcut entry in Steam shortcuts.vdf")
    exe = quoted(str(launcher))
    matches = [entry for entry in entries if isinstance((name := bin_field(entry.children or [], "AppName")), BinNode)
               and name.value == SHORTCUT_NAME]
    if len(matches) > 1:
        exact = [entry for entry in matches if (field := bin_field(entry.children or [], "Exe")) and field.value == exe]
        if len(exact) != 1:
            raise ConfigError("Multiple GloomhavenVR Steam shortcuts; cannot choose one safely")
        matches = exact
    used_ids = {field.value & 0xFFFFFFFF for entry in entries
                if (field := bin_field(entry.children or [], "appid")) and isinstance(field.value, int)}
    if matches:
        target = matches[0]
        id_field = bin_field(target.children or [], "appid")
        appid = id_field.value & 0xFFFFFFFF if id_field and isinstance(id_field.value, int) else shortcut_appid(exe)
        while appid in used_ids and id_field is None:
            appid = (appid + 1) | 0x80000000
        edits: list[tuple[int, int, bytes]] = []
        desired = {
            "appid": (2, appid),
            "AppName": (1, SHORTCUT_NAME),
            "Exe": (1, exe),
            "StartDir": (1, str(launcher.parent)),
            "icon": (1, str(icon)),
            "OpenVR": (2, 1),
            "IsHidden": (2, 0),
        }
        missing = b""
        for key, (kind, value) in desired.items():
            field = bin_field(target.children or [], key)
            replacement = bin_string(field.key if field else key, value) if kind == 1 else bin_int(field.key if field else key, value)
            if field is None:
                missing += replacement
            elif field.kind != kind or field.value != value:
                edits.append((field.start, field.end, replacement))
        if missing:
            edits.append((target.close, target.close, missing))
        result = raw
        for start, end, replacement in sorted(edits, reverse=True):
            result = result[:start] + replacement + result[end:]
        return result, appid
    appid = shortcut_appid(exe)
    while appid in used_ids:
        appid = (appid + 1) | 0x80000000
    index = max((int(entry.key) for entry in entries), default=-1) + 1
    content = b"".join((
        bin_int("appid", appid),
        bin_string("AppName", SHORTCUT_NAME),
        bin_string("Exe", exe),
        bin_string("StartDir", str(launcher.parent)),
        bin_string("icon", str(icon)),
        bin_string("ShortcutPath", ""),
        bin_string("LaunchOptions", ""),
        bin_int("IsHidden", 0),
        bin_int("AllowDesktopConfig", 1),
        bin_int("AllowOverlay", 1),
        bin_int("OpenVR", 1),
        bin_int("Devkit", 0),
        bin_string("DevkitGameID", ""),
        bin_int("DevkitOverrideAppID", 0),
        bin_int("LastPlayTime", 0),
        bin_string("FlatpakAppID", ""),
        bin_object("tags", b""),
    ))
    addition = bin_object(str(index), content)
    return raw[:root.close] + addition + raw[root.close:], appid


def find_steam_root(explicit: Path | None) -> Path:
    if explicit:
        root = explicit.expanduser().resolve()
        if not (root / "userdata").is_dir():
            raise ConfigError(f"Steam userdata folder not found: {root / 'userdata'}")
        return root
    for candidate in (Path.home() / ".local/share/Steam", Path.home() / ".steam/steam", Path.home() / ".steam/root"):
        if (candidate / "userdata").is_dir():
            return candidate.resolve()
    raise ConfigError("Steam installation not found under this user's home directory")


def select_account(root: Path, explicit: str | None) -> Path:
    userdata = root / "userdata"
    if explicit:
        if not explicit.isdigit() or not (userdata / explicit / "config/localconfig.vdf").is_file():
            raise ConfigError(f"Steam account {explicit} has no localconfig.vdf")
        return userdata / explicit
    candidates = [path for path in userdata.iterdir()
                  if path.is_dir() and path.name.isdigit() and (path / "config/localconfig.vdf").is_file()]
    if not candidates:
        raise ConfigError("No Steam account with localconfig.vdf was found")
    if len(candidates) == 1:
        return candidates[0]
    loginusers = root / "config/loginusers.vdf"
    if loginusers.is_file():
        raw = read_bounded(loginusers)
        parsed = parse_text(raw.decode("utf-8", "surrogateescape"))
        users = one_child(parsed, "users")
        if users and users.children:
            recent = []
            for user in users.children:
                if user.children and user.key.isdigit():
                    flag = one_child(user.children, "MostRecent")
                    if flag and flag.value == "1":
                        account = str(int(user.key) - STEAM_ID64_BASE)
                        recent.extend(path for path in candidates if path.name == account)
            if len(recent) == 1:
                return recent[0]
    raise ConfigError("Several Steam accounts found; pass --account-id with the userdata directory number")


def read_bounded(path: Path) -> bytes:
    if path.stat().st_size > MAX_CONFIG_BYTES:
        raise ConfigError(f"Steam configuration is unexpectedly large: {path}")
    return path.read_bytes()


def steam_running(proc_root: Path) -> bool:
    if not proc_root.is_dir():
        return False
    for process in proc_root.iterdir():
        if not process.name.isdigit():
            continue
        try:
            if process.stat().st_uid != os.getuid():
                continue
            name = (process / "comm").read_text(encoding="utf-8").strip()
        except (OSError, UnicodeError):
            continue
        if name in {"steam", "steamwebhelper", "steam.exe"}:
            return True
    return False


def backup_and_replace(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        stamp = time.strftime("%Y%m%d-%H%M%S", time.localtime())
        backup = path.with_name(f"{path.name}.gloomhavenvr-backup-{stamp}-{os.getpid()}")
        shutil.copy2(path, backup)
    fd, temporary = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as file:
            file.write(content)
            file.flush()
            os.fsync(file.fileno())
        if path.exists():
            shutil.copymode(path, temporary)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steam-root", type=Path, help="Steam installation root (for alternate installs/tests)")
    parser.add_argument("--account-id", help="Steam userdata account ID when several accounts are present")
    parser.add_argument("--game-path", type=Path, required=True)
    parser.add_argument("--launcher", type=Path, required=True)
    parser.add_argument("--icon", type=Path, required=True)
    parser.add_argument("--logo", type=Path, required=True)
    parser.add_argument("--portrait", type=Path, required=True,
                        help="600x900 Steam Library capsule for the VR shortcut")
    parser.add_argument("--header", type=Path, required=True,
                        help="wide Steam Library capsule for the VR shortcut")
    parser.add_argument("--hero", type=Path, required=True,
                        help="Steam Library details background for the VR shortcut")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--needs-update", action="store_true",
                        help="print only yes/no without changing Steam files")
    parser.add_argument("--allow-running", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--proc-root", type=Path, default=Path("/proc"), help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if args.dry_run and args.needs_update:
        parser.error("--dry-run and --needs-update cannot be combined")
    try:
        game = args.game_path.expanduser().resolve()
        if not (game / "GH.exe").is_file():
            raise ConfigError(f"GH.exe not found in {game}")
        root = find_steam_root(args.steam_root)
        account = select_account(root, args.account_id)
        config = account / "config"
        localconfig = config / "localconfig.vdf"
        shortcuts = config / "shortcuts.vdf"
        launcher = args.launcher.expanduser().absolute()
        icon = args.icon.expanduser().absolute()
        logo = args.logo.expanduser().absolute()
        portrait = args.portrait.expanduser().absolute()
        header = args.header.expanduser().absolute()
        hero = args.hero.expanduser().absolute()
        old_local = read_bounded(localconfig)
        old_shortcuts = read_bounded(shortcuts) if shortcuts.exists() else b""
        new_local = patch_localconfig(old_local)
        new_shortcuts, appid = patch_shortcuts(old_shortcuts, launcher, icon)
        # Steam's non-Steam shortcut artwork is keyed by the shortcut's own
        # AppID, not Gloomhaven's 780290. Keep the AppID stable when repairing
        # an existing shortcut so that every library view updates in place.
        art = {
            config / "grid" / f"{appid}p.png": portrait,
            config / "grid" / f"{appid}.png": header,
            config / "grid" / f"{appid}_hero.png": hero,
            config / "grid" / f"{appid}_logo.png": logo,
            config / "grid" / f"{appid}_icon.png": icon,
        }
        changed = []
        if new_local != old_local:
            changed.append((localconfig, new_local))
        if new_shortcuts != old_shortcuts:
            changed.append((shortcuts, new_shortcuts))
        if not args.dry_run:
            for source in (launcher, *art.values()):
                if not source.is_file():
                    raise ConfigError(f"Required Frame launcher/artwork missing: {source}")
            for destination, source in art.items():
                data = source.read_bytes()
                if not destination.is_file() or destination.read_bytes() != data:
                    changed.append((destination, data))
        if args.needs_update:
            print("yes" if changed else "no")
            return 0
        if not args.dry_run:
            if changed and steam_running(args.proc_root) and not args.allow_running:
                raise ConfigError("Steam is still running; close it before writing its shortcut/config files")
            for destination, data in changed:
                backup_and_replace(destination, data)
        print(f"Steam account: {account.name}")
        print(f"Steam shortcuts file: {shortcuts}")
        print(f"GloomhavenVR shortcut before this check: {'current' if new_shortcuts == old_shortcuts else 'missing or stale'}")
        print(f"Gloomhaven launch options before this check: {'current' if new_local == old_local else 'missing or stale'}")
        print(f"GloomhavenVR shortcut AppID: {appid}")
        print(f"Steam configuration {'would change' if args.dry_run else 'changed'}: {len(changed)} file(s)")
        print(f"Steam restart required: {'yes' if changed else 'no'}")
        return 0
    except (ConfigError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
