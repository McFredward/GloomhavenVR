#!/usr/bin/env python3
"""Install the Steam-side GloomhavenVR Frame shortcut without third-party modules.

Original app options are edited only while Steam is stopped; a live client can
flush its cached localconfig over an apparently successful external write. Frame
setup queues an exact-generation repair for Steam's managed pre-start phase.
Unrelated fields are kept and the VR launcher rejects an incomplete repair.
"""

from __future__ import annotations

import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import stat
import struct
import sys
import tempfile
import time
import uuid
import zlib
from dataclasses import dataclass


GAME_APP_ID = "780290"
SHORTCUT_NAME = "GloomhavenVR"
STEAM_ID64_BASE = 76561197960265728
OVERRIDE = 'WINEDLLOVERRIDES="winhttp=n,b"'
MAX_CONFIG_BYTES = 64 * 1024 * 1024
MAX_MANAGED_BYTES = 64 * 1024
MANAGED_PATHS = ("game_path", "steam_root", "launcher", "icon", "logo", "portrait", "header", "hero")


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
        if "%command%" in existing[:match.start()]:
            # Behind %command% this is a game argument, not an environment
            # assignment. Preserve other text, but put the repaired override
            # in the supported environment prefix of the native UI suggestion.
            remaining = (existing[:match.start()] + existing[match.end():]).strip()
            return f'WINEDLLOVERRIDES="{merged}" {remaining}'
        result = existing[:match.start()] + f'WINEDLLOVERRIDES="{merged}"' + existing[match.end():]
        if "%command%" not in result:
            insertion = match.start() + len(f'WINEDLLOVERRIDES="{merged}"')
            result = result[:insertion] + " %command%" + result[insertion:]
        return result
    if "%command%" in existing:
        return f"{OVERRIDE} {existing}"
    return f"{OVERRIDE} %command% {existing}"


def valid_launch_options(existing: str, required: str) -> bool:
    """Accept harmless spelling differences, but only a real environment prefix."""
    try:
        lexer = shlex.shlex(existing, posix=True, punctuation_chars=";&|<>()")
        lexer.whitespace_split = True
        lexer.commenters = ""
        tokens = list(lexer)
        expected = shlex.split(required)
    except ValueError:
        return False  # Never accept an unterminated shell quote.
    if tokens != expected or tokens.count("%command%") != 1:
        return False
    if any(token.casefold() == "--gloomhavenvr" for token in tokens):
        return False  # Including a quoted flag on the original flat entry.
    # Quoting/escaping the complete substituted command can turn it into one
    # invalid executable name; require the actual standalone Steam placeholder.
    if len(re.findall(r"(?<!\S)%command%(?=\s|$)", existing)) != 1:
        return False
    command_index = tokens.index("%command%")
    if "`" in existing or "$(" in existing or any(token.startswith("#") for token in tokens[:command_index]):
        return False
    assignments = []
    for token in tokens[:command_index]:
        if not re.match(r"[A-Za-z_][A-Za-z0-9_]*=", token):
            break  # Later assignments would be arguments to a shell wrapper.
        assignments.append(token)
    overrides = [token for token in assignments if token.startswith("WINEDLLOVERRIDES=")]
    if len(overrides) != 1:
        return False
    # Unquoted shell controls and substitutions are ambiguous launch wrappers;
    # guide native UI correction instead of pretending to interpret/rewrite them.
    if any(re.fullmatch(r"[;&|<>()]+", token) for token in tokens):
        return False
    return True


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


def launch_options(raw: bytes) -> str:
    parsed = parse_text(raw.decode("utf-8", "surrogateescape"))
    node, _ = find_path(parsed, ["UserLocalConfigStore", "Software", "Valve", "Steam", "Apps", GAME_APP_ID])
    if node is None:
        return ""
    launch = one_child(node.children or [], "LaunchOptions")
    if launch is None:
        return ""
    if launch.value is None:
        raise ConfigError("Steam LaunchOptions is not a string")
    return launch.value


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


def read_bounded(path: Path, max_bytes: int = MAX_CONFIG_BYTES) -> bytes:
    # Nonblocking open plus fstat also protects the launcher from a FIFO swapped
    # in between a path-level check and open; only regular config/art inputs exist.
    descriptor = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode):
            raise ConfigError(f"Steam configuration/input is not a regular file: {path}")
        if info.st_size > max_bytes:
            raise ConfigError(f"Steam configuration/input is unexpectedly large: {path}")
        data = stream.read(max_bytes + 1)
        if len(data) > max_bytes:
            raise ConfigError(f"Steam configuration/input is unexpectedly large: {path}")
        return data


def steam_running(proc_root: Path) -> bool:
    return bool(steam_processes(proc_root))


def steam_processes(proc_root: Path, strict: bool = False) -> dict[str, str]:
    """Identify a Steam lifetime, including PID reuse, without relying on D-Bus."""
    result: dict[str, str] = {}
    if not proc_root.is_dir():
        if strict:
            raise ConfigError("Steam process census is unavailable; refusing an unverified offline write")
        return result
    try:
        processes = list(proc_root.iterdir())
    except OSError as error:
        if strict:
            raise ConfigError("Steam process census cannot be read") from error
        return result
    for process in processes:
        if not process.name.isdigit():
            continue
        try:
            if process.stat().st_uid != os.getuid():
                continue
            name = (process / "comm").read_text(encoding="utf-8").strip()
        except (OSError, UnicodeError) as error:
            if not process.exists():
                continue  # The process exited during the census.
            if strict:
                raise ConfigError(f"Steam process census cannot inspect PID {process.name}") from error
            continue
        if name in {"steam", "steamwebhelper", "steam.exe"}:
            try:
                # Field22 is starttime; the process name in parentheses may
                # itself contain spaces or ')', so split after its last ')'.
                start = (process / "stat").read_text().rsplit(")", 1)[1].split()[19]
                if not start.isdigit():
                    raise ValueError("Invalid process start ticks")
            except (OSError, UnicodeError, IndexError, ValueError) as error:
                if not process.exists():
                    continue  # A webhelper exited between comm and stat.
                if strict:
                    raise ConfigError(f"Steam process lifetime cannot be verified for PID {process.name}") from error
                start = "unknown"
            result[process.name] = start
    return result


def strict_steam_processes(proc_root: Path) -> dict[str, str]:
    """A failed census must never be mistaken for an offline Steam client."""
    return steam_processes(proc_root, strict=True)


def steam_boot_id(proc_root: Path) -> str | None:
    try:
        value = (proc_root / "sys/kernel/random/boot_id").read_text().strip()
    except (OSError, UnicodeError):
        return None
    return value if re.fullmatch(r"[0-9a-f-]{36}", value) else None


def show_launch_options_instruction(required: str, reason: str) -> int:
    print(f"VR launch stopped: {reason}", file=sys.stderr)
    print("Rerun GloomhavenVR Steam Frame setup to repair the original game's launch options automatically.", file=sys.stderr)
    print("Run setup from Desktop mode and let its managed Steam restart finish before retrying GloomhavenVR.", file=sys.stderr)
    print("GloomhavenVR Steam Frame Setup im Desktop-Modus erneut ausführen und den automatischen Steam-Neustart abwarten.", file=sys.stderr)
    return 3


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


@contextmanager
def managed_setup_lock(path: Path):
    """Serialize a rerun with an older service invocation's completion receipt."""
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path.with_name(f".{path.name}.lock"),
                         os.O_CREAT | os.O_WRONLY | os.O_APPEND | os.O_NONBLOCK, 0o600)
    with os.fdopen(descriptor, "a") as lock:
        if not stat.S_ISREG(os.fstat(lock.fileno()).st_mode):
            raise ConfigError("Managed Steam setup lock is not a regular file")
        fcntl.flock(lock.fileno(), fcntl.LOCK_EX)
        yield


def helper_sha256() -> str:
    return hashlib.sha256(read_bounded(Path(__file__))).hexdigest()


def managed_request_sha256(receipt: dict) -> str:
    immutable = {key: receipt[key] for key in
                 ("schema", "generation", "app_id", "helper_sha256", "request", "old_session")}
    return hashlib.sha256(json.dumps(immutable, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def read_managed_setup(path: Path) -> dict:
    try:
        receipt = json.loads(read_bounded(path, MAX_MANAGED_BYTES))
        if (not isinstance(receipt, dict) or receipt.get("schema") != 2
                or receipt.get("phase") not in {"pending", "applied", "failed"}
                or receipt.get("app_id") != GAME_APP_ID
                or not re.fullmatch(r"[0-9a-f]{32}", receipt.get("generation", ""))
                or not re.fullmatch(r"[0-9a-f]{64}", receipt.get("helper_sha256", ""))):
            raise ValueError("Invalid managed setup header")
        request = receipt["request"]
        if (not isinstance(request, dict) or set(request) != set((*MANAGED_PATHS, "account_id"))
                or not isinstance(request["account_id"], str) or not request["account_id"].isdigit()):
            raise ValueError("Invalid managed setup arguments")
        for name in MANAGED_PATHS:
            value = request[name]
            if not isinstance(value, str) or not value or "\0" in value or not Path(value).is_absolute():
                raise ValueError("Invalid managed setup path")
        previous = receipt["old_session"]
        if (not isinstance(previous, dict) or not isinstance(previous.get("processes"), dict)
                or not previous["processes"]
                or not re.fullmatch(r"[0-9a-f-]{36}", previous.get("boot_id", ""))
                or any(not isinstance(pid, str) or not pid.isdigit()
                       or not isinstance(start, str) or not start.isdigit()
                       for pid, start in previous["processes"].items())
                or receipt.get("request_sha256") != managed_request_sha256(receipt)):
            raise ValueError("Invalid managed setup session or request digest")
        if receipt["phase"] != "pending":
            result = receipt["result"]
            if (not isinstance(result, dict)
                    or any(result.get(key) != receipt[key] for key in
                           ("generation", "request_sha256", "helper_sha256"))):
                raise ValueError("Managed setup result belongs to another request")
            if receipt["phase"] == "applied" and not re.fullmatch(
                    r"[0-9a-f-]{36}", result.get("applied_boot_id", "")):
                raise ValueError("Managed setup result has no verified boot identity")
        elif receipt.get("result") is not None:
            raise ValueError("Pending managed setup has a stale completion result")
        return receipt
    except (ValueError, KeyError, TypeError) as error:
        raise ConfigError("Managed Steam setup receipt is invalid; rerun automatic setup") from error


def write_managed_setup(path: Path, receipt: dict) -> None:
    data = (json.dumps(receipt, sort_keys=True, indent=2) + "\n").encode()
    if len(data) > MAX_MANAGED_BYTES:
        raise ConfigError("Managed Steam setup request is unexpectedly large")
    backup_and_replace(path, data)


def managed_boot_id(proc_root: Path) -> str:
    boot = steam_boot_id(proc_root)
    if boot is None:
        raise ConfigError("Steam boot identity cannot be verified; rerun automatic setup")
    return boot


def queue_managed_setup(path: Path, args: argparse.Namespace, root: Path, account: Path) -> int:
    processes = strict_steam_processes(args.proc_root)
    if not processes:
        raise ConfigError("Managed Steam setup requires the live client identified by the installer")
    request = {name: str(getattr(args, name).expanduser().resolve()) for name in MANAGED_PATHS
               if name != "steam_root"}
    request.update(steam_root=str(root), account_id=account.name)
    for name in ("launcher", "icon", "logo", "portrait", "header", "hero"):
        if not Path(request[name]).is_file():
            raise ConfigError(f"Required Frame launcher/artwork missing: {request[name]}")
    receipt = {"schema": 2, "phase": "pending", "generation": uuid.uuid4().hex,
               "app_id": GAME_APP_ID, "helper_sha256": helper_sha256(), "request": request,
               "old_session": {"boot_id": managed_boot_id(args.proc_root), "processes": processes},
               "result": None}
    receipt["request_sha256"] = managed_request_sha256(receipt)
    with managed_setup_lock(path):
        write_managed_setup(path, receipt)
    print(f"Managed Steam setup queued: generation={receipt['generation']} account={account.name}")
    print(f"Managed setup generation: {receipt['generation']}")
    return 0


def managed_setup_paths(receipt: dict) -> tuple[dict, Path, Path]:
    if receipt["helper_sha256"] != helper_sha256():
        raise ConfigError("Managed Steam setup helper changed; rerun automatic setup")
    request = receipt["request"]
    game = Path(request["game_path"])
    if not (game / "GH.exe").is_file():
        raise ConfigError(f"GH.exe not found in {game}")
    for name in ("launcher", "icon", "logo", "portrait", "header", "hero"):
        if not Path(request[name]).is_file():
            raise ConfigError(f"Required Frame launcher/artwork missing: {request[name]}")
    root = find_steam_root(Path(request["steam_root"]))
    account = select_account(root, None)
    if account.name != request["account_id"]:
        raise ConfigError("Steam account changed since setup was queued; rerun automatic setup")
    return request, root, account


def verify_managed_setup(path: Path, proc_root: Path, *, require_client: bool = True,
                         expected_generation: str | None = None) -> int:
    receipt = read_managed_setup(path)
    if expected_generation is not None and receipt["generation"] != expected_generation:
        raise ConfigError("Managed Steam setup generation changed; the original request is not verified")
    if receipt["phase"] != "applied":
        raise ConfigError(f"Automatic Steam setup is {receipt['phase']}; rerun setup and wait for its managed restart")
    request, _, account = managed_setup_paths(receipt)
    current = strict_steam_processes(proc_root)
    boot = managed_boot_id(proc_root)
    if require_client and not current:
        raise ConfigError("New Steam client has not started; wait for the managed restart")
    previous = receipt["old_session"]
    if boot == previous["boot_id"] and any(current.get(pid) == start for pid, start in previous["processes"].items()):
        raise ConfigError("Steam's old setup session is still active; wait for the managed restart")
    raw = read_bounded(account / "config/localconfig.vdf")
    existing = launch_options(raw)
    if not valid_launch_options(existing, update_launch_options(existing)):
        raise ConfigError("Gloomhaven launch options are missing or stale; rerun automatic setup")
    shortcuts = account / "config/shortcuts.vdf"
    old_shortcuts = read_bounded(shortcuts)
    expected, appid = patch_shortcuts(old_shortcuts, Path(request["launcher"]), Path(request["icon"]))
    if old_shortcuts != expected:
        raise ConfigError("GloomhavenVR shortcut is missing or stale; rerun automatic setup")
    art = {f"{appid}p.png": "portrait", f"{appid}.png": "header", f"{appid}_hero.png": "hero",
           f"{appid}_logo.png": "logo", f"{appid}_icon.png": "icon"}
    for name, source in art.items():
        if read_bounded(account / "config/grid" / name) != read_bounded(Path(request[source])):
            raise ConfigError("GloomhavenVR artwork readback differs; rerun automatic setup")
    print(f"Managed Steam setup verified: generation={receipt['generation']} account={account.name}")
    return 0


def apply_managed_setup(path: Path, proc_root: Path) -> int:
    # The persistent nonfatal startup hook remains safe after offline setup removes
    # its request. A corrupt present request is an error, never a missing-job pass.
    if not path.exists():
        return 0
    with managed_setup_lock(path):
        receipt = read_managed_setup(path)
        if receipt["phase"] == "applied":
            # Do not force an old completed request over later intentional edits.
            return verify_managed_setup(path, proc_root, require_client=False)
        try:
            request, _, account = managed_setup_paths(receipt)
            boot = managed_boot_id(proc_root)
            if strict_steam_processes(proc_root):
                raise ConfigError("Steam is still running; managed setup requires the offline pre-start phase")
            merged = launch_options(patch_localconfig(read_bounded(account / "config/localconfig.vdf")))
            if not valid_launch_options(merged, merged):
                raise ConfigError("Custom launch-option quoting/wrapper is unsupported; original options were preserved")
            # Resolve fresh disk data only now, after the previous client's flush.
            # Do not persist or replay an old localconfig/shortcuts snapshot.
            arguments = []
            for key, value in request.items():
                arguments.extend(("--" + key.replace("_", "-"), value))
            arguments.extend(("--proc-root", str(proc_root)))
            if main(arguments) != 0:
                raise ConfigError("Automatic Steam configuration write failed; rerun setup")
            if strict_steam_processes(proc_root):
                raise ConfigError("Steam started before managed setup completed; rerun setup")
            receipt["phase"] = "applied"
            receipt["result"] = {key: receipt[key] for key in
                                 ("generation", "request_sha256", "helper_sha256")}
            receipt["result"].update(applied_boot_id=boot, applied_at=time.time())
            write_managed_setup(path, receipt)
            verify_managed_setup(path, proc_root, require_client=False)
        except (ConfigError, OSError) as error:
            receipt["phase"] = "failed"
            receipt["result"] = {key: receipt[key] for key in
                                 ("generation", "request_sha256", "helper_sha256")}
            receipt["result"]["error"] = str(error)[:512]
            write_managed_setup(path, receipt)
            raise
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steam-root", type=Path, help="Steam installation root (for alternate installs/tests)")
    parser.add_argument("--account-id", help="Steam userdata account ID when several accounts are present")
    parser.add_argument("--game-path", type=Path)
    parser.add_argument("--launcher", type=Path)
    parser.add_argument("--icon", type=Path)
    parser.add_argument("--logo", type=Path)
    parser.add_argument("--portrait", type=Path,
                        help="600x900 Steam Library capsule for the VR shortcut")
    parser.add_argument("--header", type=Path,
                        help="wide Steam Library capsule for the VR shortcut")
    parser.add_argument("--hero", type=Path,
                        help="Steam Library details background for the VR shortcut")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--needs-update", action="store_true",
                        help="print only yes/no without changing Steam files")
    parser.add_argument("--shortcut-only", action="store_true",
                        help="prepare only the shortcut/artwork; never edit original app options")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--required-launch-options", action="store_true",
                       help="print the complete original-app launch options to save in Steam")
    modes.add_argument("--check-launch-options", action="store_true",
                       help="read-only VR launch check; refuse missing options instead of starting flat")
    modes.add_argument("--record-running-setup", type=Path,
                       help="record a pending native-UI setup until the current Steam session has restarted")
    modes.add_argument("--queue-managed-setup", type=Path,
                       help="queue an automatic repair for the managed Steam pre-start phase")
    modes.add_argument("--apply-managed-setup", type=Path,
                       help="apply the queued request while Steam is stopped")
    modes.add_argument("--verify-managed-setup", type=Path,
                       help="verify the exact automatic repair after Steam restarts")
    parser.add_argument("--pending-setup", type=Path,
                        help="pending setup guard to check before forwarding a VR launch")
    parser.add_argument("--expected-generation",
                        help="verify the exact generation returned when setup was queued")
    parser.add_argument("--allow-running", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--proc-root", type=Path, default=Path("/proc"), help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if args.dry_run and args.needs_update:
        parser.error("--dry-run and --needs-update cannot be combined")
    try:
        if args.expected_generation is not None:
            if not args.verify_managed_setup or not re.fullmatch(r"[0-9a-f]{32}", args.expected_generation):
                raise ConfigError("--expected-generation requires --verify-managed-setup and a queue generation")
        if args.apply_managed_setup or args.verify_managed_setup:
            if args.dry_run or args.needs_update or args.shortcut_only:
                raise ConfigError("Managed apply/verify cannot be combined with partial or dry-run modes")
            path = args.apply_managed_setup or args.verify_managed_setup
            if args.apply_managed_setup:
                return apply_managed_setup(path, args.proc_root)
            with managed_setup_lock(path):
                return verify_managed_setup(path, args.proc_root, expected_generation=args.expected_generation)
        if args.game_path is None:
            raise ConfigError("--game-path is required when not applying or verifying a managed request")
        game = args.game_path.expanduser().resolve()
        if not (game / "GH.exe").is_file():
            raise ConfigError(f"GH.exe not found in {game}")
        root = find_steam_root(args.steam_root)
        account = select_account(root, args.account_id)
        config = account / "config"
        localconfig = config / "localconfig.vdf"
        shortcuts = config / "shortcuts.vdf"
        old_local = read_bounded(localconfig)
        if args.required_launch_options or args.check_launch_options or args.record_running_setup:
            existing = launch_options(old_local)
            required = update_launch_options(existing)
            if args.required_launch_options:
                print(required)
                return 0
            if args.record_running_setup:
                processes = steam_processes(args.proc_root)
                # An empty/unknown process receipt still records pending setup,
                # and later checks fail closed. Never call live setup complete
                # just because process inspection could not identify Steam.
                receipt = {"schema": 1, "processes": processes, "boot_id": steam_boot_id(args.proc_root)}
                backup_and_replace(args.record_running_setup, (json.dumps(receipt) + "\n").encode())
                return 0
            if args.pending_setup and args.pending_setup.exists():
                try:
                    pending = json.loads(read_bounded(args.pending_setup))
                    if isinstance(pending, dict) and pending.get("schema") == 2:
                        with managed_setup_lock(args.pending_setup):
                            receipt = read_managed_setup(args.pending_setup)
                            if (receipt["request"]["steam_root"] != str(root)
                                    or receipt["request"]["account_id"] != account.name
                                    or receipt["request"]["game_path"] != str(game)):
                                raise ConfigError("Managed Steam setup belongs to another game/account")
                            return verify_managed_setup(args.pending_setup, args.proc_root)
                    previous = pending["processes"]
                    previous_boot = pending["boot_id"]
                    if (pending["schema"] != 1 or not isinstance(previous, dict)
                            or (previous_boot is not None and (not isinstance(previous_boot, str)
                                                               or not re.fullmatch(r"[0-9a-f-]{36}", previous_boot)))
                            or any(not isinstance(pid, str) or not pid.isdigit()
                                   or not isinstance(start, str) or (not start.isdigit() and start != "unknown")
                                   for pid, start in previous.items())):
                        raise ValueError("Invalid pending setup receipt")
                    current = steam_processes(args.proc_root)
                    current_boot = steam_boot_id(args.proc_root)
                    # A Frame power restart also changes the boot ID. PID and
                    # start ticks may coincidentally recur on the next boot.
                    # It also safely retires an empty/unknown old process census
                    # caused by a transient shutdown race or denied proc read.
                    rebooted = previous_boot is not None and current_boot is not None and current_boot != previous_boot
                    if not rebooted:
                        if (not previous or not current
                                or (previous_boot is not None and current_boot is None)):
                            return show_launch_options_instruction(required, "Steam's setup session restart has not been verified.")
                        if any(pid in current and (start == "unknown" or current[pid] == "unknown" or current[pid] == start)
                               for pid, start in previous.items()):
                            return show_launch_options_instruction(required, "Old live setup needs replacement with automatic managed setup.")
                except (ConfigError, OSError, ValueError, KeyError, TypeError):
                    return show_launch_options_instruction(required, "The pending Steam setup receipt cannot be verified.")
            if valid_launch_options(existing, required):
                return 0
            if not valid_launch_options(required, required):
                return show_launch_options_instruction(f"{OVERRIDE} %command%", "Custom shell quoting/wrapper is unsupported; existing settings have not been overwritten.")
            return show_launch_options_instruction(required, "Gloomhaven's Steam launch options are missing or stale.")
        for name in ("launcher", "icon", "logo", "portrait", "header", "hero"):
            if getattr(args, name) is None:
                raise ConfigError(f"--{name} is required when configuring the Steam shortcut")
        if args.queue_managed_setup:
            if args.dry_run or args.needs_update or args.shortcut_only:
                raise ConfigError("Managed queue requires a complete setup, not a partial or dry-run mode")
            return queue_managed_setup(args.queue_managed_setup, args, root, account)
        launcher = args.launcher.expanduser().absolute()
        icon = args.icon.expanduser().absolute()
        logo = args.logo.expanduser().absolute()
        portrait = args.portrait.expanduser().absolute()
        header = args.header.expanduser().absolute()
        hero = args.hero.expanduser().absolute()
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
        if new_local != old_local and not args.shortcut_only:
            changed.append((localconfig, new_local))
        if new_shortcuts != old_shortcuts:
            changed.append((shortcuts, new_shortcuts))
        if not args.dry_run:
            for source in (launcher, *art.values()):
                if not source.is_file():
                    raise ConfigError(f"Required Frame launcher/artwork missing: {source}")
            for destination, source in art.items():
                data = read_bounded(source)
                if not destination.is_file() or read_bounded(destination) != data:
                    changed.append((destination, data))
        if args.needs_update:
            print("yes" if changed else "no")
            return 0
        if not args.dry_run:
            if changed and strict_steam_processes(args.proc_root):
                # --allow-running historically allowed localconfig writes too.
                # That lost the override when Frame restarted and Steam flushed
                # its old in-memory options. Never restore that unsafe exception:
                # Offline writes or the managed pre-start transaction are required.
                if any(destination == localconfig for destination, _ in changed):
                    raise ConfigError("Steam is still running; rerun automatic Frame setup for a managed offline repair")
                if not args.allow_running:
                    raise ConfigError("Steam is still running; close it before writing its shortcut/config files")
            for destination, data in changed:
                backup_and_replace(destination, data)
        print(f"Steam account: {account.name}")
        print(f"Steam shortcuts file: {shortcuts}")
        print(f"GloomhavenVR shortcut before this check: {'current' if new_shortcuts == old_shortcuts else 'missing or stale'}")
        print(f"Gloomhaven launch options before this check: {'current' if new_local == old_local else 'missing or stale'}")
        if args.shortcut_only:
            print("Original Gloomhaven launch options were not written; a complete automatic setup is still required.")
        print(f"GloomhavenVR shortcut AppID: {appid}")
        print(f"Steam configuration {'would change' if args.dry_run else 'changed'}: {len(changed)} file(s)")
        print(f"Steam restart required: {'yes' if changed else 'no'}")
        return 0
    except (ConfigError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
