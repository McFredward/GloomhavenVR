"""Emit an owned ARM64 renameat helper without a compiler or libc dependency.

Android toybox mv may copy a directory after EXDEV, and emulated-storage inode
numbers may change across views. Neither is an acceptable reason to copy an
11-GB game bank while retaining a few KB of saves. This executable contains only
argument-count validation, renameat(AT_FDCWD, old, AT_FDCWD, new), and exit(errno).
It cannot copy, enumerate, hash, or remove any payload. The caller supplies two
validated transaction-owned directory paths. An EXDEV result is explicit.
"""
from __future__ import annotations

import struct

# Linux ARM64 syscall ABI: renameat=38, exit=93, AT_FDCWD=-100.
# Each word is an instruction, checked against NDK clang/llvm-objdump in the
# native-rename controls. The branch rejects malformed argv before dereference.
INSTRUCTIONS = (
    0xF94003E0,  # ldr x0, [sp]             ; argc
    0xF1000C1F,  # cmp x0, #3
    0x54000060,  # b.eq rename              ; skip error exit
    0xD28002C0,  # mov x0, #22              ; EINVAL
    0x14000008,  # b exit
    0xF9400BE1,  # ldr x1, [sp, #16]        ; old argv[1]
    0xF9400FE3,  # ldr x3, [sp, #24]        ; new argv[2]
    0x92800C60,  # mov x0, #-100
    0x92800C62,  # mov x2, #-100
    0xD28004C8,  # mov x8, #38
    0xD4000001,  # svc #0
    0xCB0003E0,  # neg x0, x0               ; 0 or positive errno
    0xD2800BA8,  # exit: mov x8, #93
    0xD4000001,  # svc #0
)


def executable():
    """Return a static ELF64 image, with one RX segment and 16-KB alignment."""
    base = 0x400000
    length = 64 + 56 + len(INSTRUCTIONS) * 4
    ident = b"\x7fELF\x02\x01\x01\x00" + b"\x00" * 8
    header = struct.pack("<16sHHIQQQIHHHHHH", ident, 2, 183, 1, base + 120, 64, 0, 0, 64, 56, 1, 0, 0, 0)
    segment = struct.pack("<IIQQQQQQ", 1, 5, 0, base, base, length, length, 16384)
    return header + segment + struct.pack("<" + "I" * len(INSTRUCTIONS), *INSTRUCTIONS)
