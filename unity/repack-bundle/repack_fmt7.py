#!/usr/bin/env python3
# repack_fmt7.py — downgrade a UnityFS AssetBundle wrapper from format 8 to format 7.
#
# WHY THIS EXISTS
# --------------
# The game (Gloomhaven, Flaming Fowl) runs Unity **2021.3.5f1**. Its runtime cannot read
# UnityFS archive **format version 8** (introduced later in the 2021.3.x line, together with
# the `BlockInfoNeedPaddingAtStart` flag 0x200). Loading a format-8 bundle fails at startup with:
#     "Unable to read header from archive file: .../gloomhavenvr.bundle"
#     "Failed to read data for the AssetBundle ..."
# and the mod falls back to procedural visuals (old control board, no custom hands).
#
# Our headless bundle build on clawmachine uses the Unity **2021.3.45f1** editor (the only
# version whose Personal license activates headlessly — the fresh 2021.3.5 editor demands an
# online re-activation that fails on this box because it has no OS keyring, so Unity Hub's
# access token never reaches the licensing daemon: "Error: Access token is unavailable").
# 2021.3.45 writes format-8 bundles. So we build with 2021.3.45, then run THIS script to
# re-wrap the archive as format 7 WITHOUT touching the inner data.
#
# WHAT IT DOES (and does NOT do)
# ------------------------------
# - Reads the format-8 bundle, replaces each parsed inner SerializedFile with a RAW byte
#   reader of its ORIGINAL bytes, so UnityPy writes them back VERBATIM (no object
#   re-serialization). The inner SerializedFile and its .resS blob stay byte-identical —
#   only the outer UnityFS container changes.
# - Sets wrapper version = 7 and clears the format-8-only `BlockInfoNeedPaddingAtStart` flag.
# - Re-compresses blocks with LZ4 (well within 2021.3.5's reader support).
# Result: a format-7 bundle carrying the exact assets 2021.3.45 produced, readable by 2021.3.5.
#
# VERIFIED: inner SerializedFile md5 unchanged before/after; all objects re-read cleanly;
# header version byte == 07; no 0x200 flag.
#
# USAGE
#   python3 repack_fmt7.py <in_fmt8.bundle> <out_fmt7.bundle>
# Requires UnityPy (pip install UnityPy). On clawmachine: /home/claw/unitypy-venv/bin/python.

import sys, hashlib
import UnityPy
from UnityPy.streams import EndianBinaryReader
from UnityPy.enums import ArchiveFlags


def repack(src: str, out: str) -> None:
    env = UnityPy.load(src)
    f = env.file
    if f.version == 7:
        print(f"[repack] {src} is already format 7 — copying through unchanged")

    # Replace each parsed SerializedFile with a raw reader over its ORIGINAL bytes so
    # BundleFile.save writes them verbatim (see UnityPy files/BundleFile.py save_fs: raw
    # EndianBinaryReader -> f.bytes; a SerializedFile object -> f.save() which re-serializes).
    for name, sf in list(f.files.items()):
        if type(sf).__name__ == "SerializedFile":
            raw = bytes(sf.reader.bytes)
            nr = EndianBinaryReader(raw)
            nr.flags = getattr(sf, "flags", 4)  # DirectoryInfo node flags (4 = serialized file)
            nr.name = name
            f.files[name] = nr
            print(f"[repack] inner {name}: {len(raw)} bytes, md5 {hashlib.md5(raw).hexdigest()[:10]} (verbatim)")

    f.version = 7
    f.dataflags = ArchiveFlags(int(f.dataflags) & ~int(ArchiveFlags.BlockInfoNeedPaddingAtStart))
    data = f.save(packer="lz4")
    with open(out, "wb") as fh:
        fh.write(data)

    verbyte = data[8:12].hex()
    print(f"[repack] wrote {out}: {len(data)} bytes, header version byte 0x{verbyte}")

    # self-check: re-read and confirm every object parses and inner bytes are unchanged
    env2 = UnityPy.load(out)
    n = ok = 0
    for o in env2.objects:
        n += 1
        try:
            o.read(); ok += 1
        except Exception as e:  # noqa: BLE001
            print(f"[repack] WARN object {o.type.name} failed to re-read: {e!r}")
    assert env2.file.version == 7, "output is not format 7"
    assert int(env2.file.dataflags) & int(ArchiveFlags.BlockInfoNeedPaddingAtStart) == 0, "padding flag still set"
    assert ok == n, f"only {ok}/{n} objects re-read"
    print(f"[repack] OK: format {env2.file.version}, {ok}/{n} objects re-read cleanly")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    repack(sys.argv[1], sys.argv[2])
