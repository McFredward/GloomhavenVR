"""Pinned Android-only adapters for GNU imports in the isolated Wine worker."""
from __future__ import annotations
import hashlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE_SHA256 = "20416efe952bd194223644a45a0a17ea61f8f9903046c82dd7460777281e39ae"
HEADER_SHA256 = "df36586d7fc837ca3da34c39744269e1ec40ec2c3dc8f30b3cc764daffee225c"
ANCHOR = "#ifdef ANDROID\nvoid ctSetup()"
MAPPINGS = {
    "GO(__errno_location, pFv)": "GO2(__errno_location, pFv, __errno)",
    "GO(__assert_fail, vFppup)": "GOM(__assert_fail, vFppup)",
    "GO(__ctype_b_loc, pFv)": "GOM(__ctype_b_loc, pFv)",
    "GO(__ctype_tolower_loc, pFv)": "GOM(__ctype_tolower_loc, pFv)",
    "GO(__ctype_toupper_loc, pFv)": "GOM(__ctype_toupper_loc, pFv)",
    "GOW(dcgettext, pFppi)": "GOWM(dcgettext, pFppi)",
    "GO(initstate_r, iFupLp)": "GOM(initstate_r, iFupLp)",
    "GOW(random_r, iFpp)": "GOWM(random_r, iFpp)",
}
REQUIRED_EXPORTS = frozenset(("my___assert_fail", "my___ctype_b_loc", "my___ctype_tolower_loc",
                             "my___ctype_toupper_loc", "my_dcgettext", "my_initstate_r", "my_random_r"))


def adapt(source: bytes, header: bytes) -> tuple[bytes, bytes]:
    if hashlib.sha256(source).hexdigest() != SOURCE_SHA256 or hashlib.sha256(header).hexdigest() != HEADER_SHA256:
        raise RuntimeError("Box64 GNU/Bionic adapters require the audited source revision.")
    text, table = source.decode(), header.decode()
    if text.count(ANCHOR) != 1 or any(table.count(original + "\n") != 1 for original in MAPPINGS):
        raise RuntimeError("Pinned Box64 GNU/Bionic adapter anchors differ.")
    text = text.replace(ANCHOR, '#ifdef ANDROID\n#include "quest_bionic_abi.c"\nvoid ctSetup()')
    for original, mapped in MAPPINGS.items():
        table = table.replace(original + "\n", "#ifdef ANDROID\n" + mapped + "\n#else\n" + original + "\n#endif\n")
    return text.encode(), table.encode()


def apply(root: Path) -> None:
    directory = Path(root) / "src/wrapped"
    source, header = directory / "wrappedlibc.c", directory / "wrappedlibc_private.h"
    changed, mapped = adapt(source.read_bytes(), header.read_bytes())
    source.write_bytes(changed); header.write_bytes(mapped)
    (directory / "quest_bionic_abi.c").write_bytes((HERE / "bionic_abi.c").read_bytes())


def require_exports(exports: set[str]) -> None:
    missing = REQUIRED_EXPORTS - exports
    if missing:
        raise RuntimeError("Box64 Android GNU adapter exports are missing: " + ", ".join(sorted(missing)))
