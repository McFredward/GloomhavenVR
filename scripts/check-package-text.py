#!/usr/bin/env python3
"""Every text file inside a release zip must open cleanly on a Windows machine.

WHY THIS EXISTS
---------------
The German install guide in the packaged zip rendered "raumgroÃŸes" for "raumgroßes"
(reported 2026-09-03). The bytes in the zip were correct UTF-8 (C3 9F) — the file simply had
no byte-order mark and LF line endings, and the reader is a Windows user who double-clicks a
.txt. Without a BOM, legacy Notepad, WordPad, the 7-Zip and WinRAR viewers and the Explorer
preview pane decode the file as the ANSI code page, and every umlaut becomes two Latin-1
characters. The same string also comes out of scripts/install.ps1 if it ever reads a template
through Windows PowerShell's default (ANSI) decoding and re-encodes it as UTF-8 — a DOUBLE
encoding that no viewer can undo. Both look identical to the person reporting it.

Neither packager could tell. This check can: it reads every .txt entry of the finished zip and
fails the run unless each one is

  1. valid UTF-8 (strict decode),
  2. prefixed with the UTF-8 byte-order mark EF BB BF,
  3. CRLF-terminated on every line, with no bare LF and no bare CR, and
  4. free of the two characters a double encoding always produces — U+00C3 "Ã" (the lead byte
     of every UTF-8 umlaut read as Latin-1) and U+00E2 "â" (the lead byte of every em dash and
     typographic quote). No English or German sentence in these files uses either letter.

Usage: python3 scripts/check-package-text.py dist/GloomhavenVR-<version>.zip
"""

from __future__ import annotations

import sys
import zipfile

BOM = b"\xef\xbb\xbf"
DOUBLE_ENCODED = ("Ã", "â")


def problems(name: str, data: bytes) -> list[str]:
    out: list[str] = []
    if not data.startswith(BOM):
        out.append("no UTF-8 byte-order mark (EF BB BF) — a Windows viewer decodes it as ANSI")
    try:
        text = data[len(BOM):].decode("utf-8", errors="strict") if data.startswith(BOM) \
            else data.decode("utf-8", errors="strict")
    except UnicodeDecodeError as e:
        out.append(f"not valid UTF-8: {e}")
        return out
    crlf = data.count(b"\r\n")
    if data.count(b"\n") != crlf:
        out.append(f"{data.count(b'\n') - crlf} bare LF line ending(s) — must be CRLF")
    if data.count(b"\r") != crlf:
        out.append(f"{data.count(b'\r') - crlf} bare CR — must be CRLF")
    for ch in DOUBLE_ENCODED:
        idx = text.find(ch)
        if idx >= 0:
            snippet = text[max(0, idx - 8):idx + 8].replace("\r", "").replace("\n", " ")
            out.append(f"contains U+{ord(ch):04X} '{ch}' at offset {idx} ('{snippet}') — an umlaut or "
                       "dash was decoded as ANSI and re-encoded (double encoding)")
    return out


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    zip_path = argv[1]
    bad = 0
    seen = 0
    with zipfile.ZipFile(zip_path) as z:
        for info in z.infolist():
            if info.is_dir() or not info.filename.lower().endswith(".txt"):
                continue
            seen += 1
            data = z.read(info.filename)
            found = problems(info.filename, data)
            if found:
                bad += 1
                print(f"error: {info.filename}:", file=sys.stderr)
                for p in found:
                    print(f"  {p}", file=sys.stderr)
            else:
                print(f"text OK: {info.filename} ({len(data)} bytes, UTF-8 with BOM, CRLF)")
    if seen == 0:
        print(f"error: {zip_path} contains no .txt entry at all — INSTALL.txt is load-bearing",
              file=sys.stderr)
        return 1
    if bad:
        print(f"error: {bad} of {seen} text file(s) in {zip_path} would not open cleanly on Windows",
              file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
