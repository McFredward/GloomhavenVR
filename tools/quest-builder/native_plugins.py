"""The small, signed native-program contract shared by staging, Unity and APK checks."""
import re
from pathlib import Path
import struct

from storage import BuildError, digest

PREFIX = "Assets/Quest/Plugins/Android/arm64-v8a/"
BACKENDS = ("proton-arm64ec-fex", "box64-wine9")
PROTON_REQUIRED = {"libquest_proton.so", "libquest_proton_server.so", "libqn.so", "libqw.so", "libqs.so"}


def validate(document, *, backend=None):
    if (not isinstance(document, dict) or document.get("schema") != 1
            or document.get("backend") not in BACKENDS
            or (backend is not None and document["backend"] != backend)):
        raise BuildError("Procedural native contract differs from the selected backend.")
    rows = document.get("files")
    if not isinstance(rows, list) or not 3 <= len(rows) <= 64:
        raise BuildError("Procedural native contract has an invalid file inventory.")
    names = set()
    for row in rows:
        if (not isinstance(row, dict) or not isinstance(row.get("path"), str)
                or not row["path"].startswith(PREFIX)
                or not re.fullmatch(r"lib[A-Za-z0-9_]+\.so", row["path"][len(PREFIX):])
                or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))
                or type(row.get("size")) is not int or row["size"] < 64):
            raise BuildError("Procedural native contract contains an invalid program record.")
        name = row["path"][len(PREFIX):]
        if name in names:
            raise BuildError("Procedural native contract repeats a native program: " + name)
        names.add(name)
    if not {"libQuestApparance.so", "libopus_egpv.so"}.issubset(names):
        raise BuildError("Procedural native contract lacks the original game/voice ABI.")
    if document["backend"] == "box64-wine9" and not {"libquest_box64.so", "libquest_wineserver.so"}.issubset(names):
        raise BuildError("Explicit Box64 comparison lacks its packaged executables.")
    if document["backend"] == "proton-arm64ec-fex" and names.intersection({"libquest_box64.so", "libquest_wineserver.so"}):
        raise BuildError("Proton native contract contains the old Box64 executables.")
    if document["backend"] == "proton-arm64ec-fex" and not PROTON_REQUIRED.issubset(names):
        raise BuildError("Proton native contract lacks its required loader/server/Unix modules.")
    return rows


def verify_staged(project: Path, document, *, backend=None):
    rows = validate(document, backend=backend)
    for row in rows:
        path = project / row["path"]
        if path.is_symlink() or not path.is_file() or path.stat().st_size != row["size"] or digest(path) != row["sha256"]:
            raise BuildError("Staged procedural native program changed: " + row["path"])
        with path.open("rb") as source:
            header = source.read(64)
        if header[:7] != b"\x7fELF\x02\x01\x01" or struct.unpack_from("<HHI", header, 16) != (3, 183, 1):
            raise BuildError("Procedural native program is not an Android ARM64 shared/PIE ELF: " + row["path"])
    return rows
