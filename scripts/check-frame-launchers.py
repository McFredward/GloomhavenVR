#!/usr/bin/env python3
"""Verify that SteamOS can execute the Frame helpers from a release ZIP."""

import stat
import sys
import zipfile


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: check-frame-launchers.py RELEASE.zip", file=sys.stderr)
        return 2

    with zipfile.ZipFile(sys.argv[1]) as archive:
        for name, prefix in (
            ("install-steam-frame.sh", b"#!/usr/bin/env bash\n"),
            ("GloomhavenVR-Setup.desktop", b"[Desktop Entry]\n"),
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

    print("Steam Frame launchers: LF and executable ZIP permissions verified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
