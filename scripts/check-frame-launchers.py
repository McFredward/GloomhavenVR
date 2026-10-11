#!/usr/bin/env python3
"""Verify that SteamOS can execute the Frame helpers from a release ZIP."""

import ast
import stat
import sys
import zipfile

FRAME_SETUP = "BepInEx/plugins/GloomhavenVR/FrameSetup/"
TOP_LEVEL = {
    "BepInEx",
    "INSTALL.txt",
    "INSTALL-DEUTSCH.txt",
}


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: check-frame-launchers.py RELEASE.zip", file=sys.stderr)
        return 2

    with zipfile.ZipFile(sys.argv[1]) as archive:
        backslash_names = [name for name in archive.namelist() if "\\" in name]
        if backslash_names:
            print(f"error: ZIP entries contain Windows path separators: {backslash_names[:3]}", file=sys.stderr)
            return 1
        # Users extract the release into the game folder. Keep that folder
        # limited to the normal BepInEx tree and two guides. Published updater
        # builds reject any other root file; all Frame setup files belong inside
        # BepInEx so an existing install can update to this archive.
        top_level = {name.lstrip("./").split("/", 1)[0] for name in archive.namelist()}
        if top_level != TOP_LEVEL:
            print(
                f"error: unexpected release ZIP top level: {sorted(top_level)} "
                f"(expected {sorted(TOP_LEVEL)})",
                file=sys.stderr,
            )
            return 1

        for name, prefix in (
            (FRAME_SETUP + "install-steam-frame.sh", b"#!/usr/bin/env bash\n"),
            (FRAME_SETUP + "GloomhavenVR-Setup.desktop", b"[Desktop Entry]\n"),
        ):
            try:
                entry = archive.getinfo(name)
            except KeyError:
                print(f"error: missing {name}", file=sys.stderr)
                return 1
            data = archive.read(entry)
            if not data.startswith(prefix) or b"\r" in data or data.startswith(b"\xef\xbb\xbf"):
                print(f"error: {name} needs plain UTF-8 and LF line endings", file=sys.stderr)
                return 1
            mode = entry.external_attr >> 16
            if entry.create_system != 3 or not stat.S_ISREG(mode) or mode & 0o111 != 0o111:
                print(f"error: {name} lacks executable Unix ZIP permissions", file=sys.stderr)
                return 1

        for name in (FRAME_SETUP + "steam-frame-config.py", FRAME_SETUP + "frame-boot-config.py"):
            try:
                helper = archive.read(name)
            except KeyError:
                print(f"error: missing {name}", file=sys.stderr)
                return 1
            if b"\r" in helper or helper.startswith(b"\xef\xbb\xbf"):
                print(f"error: {name} needs plain UTF-8 and LF line endings", file=sys.stderr)
                return 1
            try:
                ast.parse(helper.decode("utf-8"), filename=name)
            except (SyntaxError, UnicodeDecodeError) as error:
                print(f"error: invalid {name}: {error}", file=sys.stderr)
                return 1

        for name in (
            FRAME_SETUP + "SteamArtwork/library_600x900.png",
            FRAME_SETUP + "SteamArtwork/library_header.png",
            FRAME_SETUP + "SteamArtwork/library_hero.png",
            FRAME_SETUP + "SteamArtwork/logo.png",
            FRAME_SETUP + "SteamArtwork/icon.png",
        ):
            if name not in archive.namelist():
                print(f"error: missing {name}", file=sys.stderr)
                return 1
            if not archive.read(name).startswith(b"\x89PNG\r\n\x1a\n"):
                print(f"error: invalid PNG artwork: {name}", file=sys.stderr)
                return 1

    print("Steam Frame setup: launchers, configuration helper and ZIP permissions verified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
