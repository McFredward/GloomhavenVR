"""Exercise the real twelve-function ARM-style shim contract under host Wine.

Optional original journal replay uses exact native requests from the owner's game.
No engine/operator implementation is supplied by this test.
"""
from __future__ import annotations
import argparse
import ctypes as c
import json
from pathlib import Path
import shutil
import struct
from replay import replay


class View(c.Structure):
    _fields_ = [("x", c.c_float), ("y", c.c_float), ("z", c.c_float)]


class NativeClient:
    def __init__(self, library: Path, executable_directory: Path, payload: Path, writable: Path):
        self.library = c.CDLL(str(library))
        native = self.library
        native.quest_apparance_configure.argtypes = [c.c_char_p] * 3
        native.quest_apparance_last_error.restype = c.c_char_p
        native.ApparanceInitialise.argtypes = [c.c_char_p, c.c_void_p, c.c_int, c.c_int, c.c_int]
        native.ApparanceUpdate.argtypes = [c.c_float, View]
        native.ApparanceCreateEntity.argtypes = [c.c_int]
        native.ApparanceDestroyEntity.argtypes = [c.c_int]
        native.ApparanceEntityBuild.argtypes = [c.c_int, c.c_uint32, c.c_int, c.c_void_p, c.c_int]
        native.ApparanceUpdateAsset.argtypes = [c.c_int, c.c_char_p, c.c_int, c.c_int, c.c_void_p, c.c_int]
        native.ApparanceGetNextAssetRequest.argtypes = [c.POINTER(c.c_int), c.POINTER(c.c_int)]
        native.ApparanceGetNextAssetRequest.restype = c.c_void_p
        for name in ("ApparancePopEntityTask", "ApparancePopEngineTask"):
            getattr(native, name).argtypes = [c.POINTER(c.c_int), c.POINTER(c.c_void_p)]
        self.libc = c.CDLL(None)
        self.libc.free.argtypes = [c.c_void_p]
        self.logs = []
        self.callback_reentries = 0

        @c.CFUNCTYPE(None, c.c_char_p, c.c_int)
        def log(message, level):
            self.logs.append((level, message))
            # Mirrors an original engine callback accessing original running state.
            # The shim must deliver after unlocking and consuming the full reply.
            if level == 0:
                native.ApparanceIsRunning()
                self.callback_reentries += 1
        self.callback = log
        if native.quest_apparance_configure(str(executable_directory).encode(), str(payload).encode(), str(writable).encode()) != 0:
            raise RuntimeError(native.quest_apparance_last_error().decode())

    def request(self, operation: int, payload: bytes = b"") -> bytes:
        native = self.library
        if operation == 1:
            cursor = 0
            def text():
                nonlocal cursor
                length = struct.unpack_from("<I", payload, cursor)[0]
                cursor += 4
                value = payload[cursor:cursor + length]
                cursor += length
                return value
            text()  # Original DLL path is configured once at this shim boundary.
            procedures = text().decode().removeprefix("Z:").replace("\\", "/")
            synthesis, memory, live = struct.unpack_from("<iii", payload, cursor)
            return struct.pack("<i", native.ApparanceInitialise(procedures.encode(), self.callback, synthesis, memory, live))
        if operation == 2:
            return struct.pack("<i", native.ApparanceIsRunning())
        if operation == 3:
            dt, x, y, z = struct.unpack("<ffff", payload)
            native.ApparanceUpdate(dt, View(x, y, z))
        elif operation == 4:
            native.ApparanceSave()
        elif operation == 5:
            native.ApparanceShutdown()
        elif operation == 6:
            return struct.pack("<i", native.ApparanceCreateEntity(struct.unpack("<i", payload)[0]))
        elif operation == 7:
            native.ApparanceDestroyEntity(struct.unpack("<i", payload)[0])
        elif operation == 8:
            handle, procedure, dynamic, size = struct.unpack_from("<iIiI", payload)
            data = c.create_string_buffer(payload[16:])
            native.ApparanceEntityBuild(handle, procedure, size, data, dynamic)
        elif operation in (9, 10):
            size, data = c.c_int(), c.c_void_p()
            function = native.ApparancePopEntityTask if operation == 9 else native.ApparancePopEngineTask
            result = function(c.byref(size), c.byref(data))
            return struct.pack("<ii", result, size.value) + (c.string_at(data, size.value) if size.value else b"")
        elif operation == 11:
            context, length = struct.unpack_from("<iI", payload)
            text = payload[8:8 + length]
            asset, bounds, variants, size = struct.unpack_from("<iiiI", payload, 8 + length)
            frame = c.create_string_buffer(payload[24 + length:]) if size else None
            native.ApparanceUpdateAsset(context, text or None, asset, bounds, frame, variants)
        elif operation == 12:
            context, asset = c.c_int(), c.c_int()
            pointer = native.ApparanceGetNextAssetRequest(c.byref(context), c.byref(asset))
            if not pointer:
                return struct.pack("<iii", context.value, asset.value, -1)
            data = c.string_at(pointer)
            self.libc.free(pointer)
            return struct.pack("<iii", context.value, asset.value, len(data)) + data
        else:
            raise ValueError("Unsupported original native operation")
        return b""


def main():
    parser = argparse.ArgumentParser()
    for name in ("bridge", "worker", "library", "procedures", "journal", "cache", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--wine-directory", type=Path, default=Path("/usr/lib/wine"))
    args = parser.parse_args()
    payload, state = args.cache / "payload", args.cache / "state"
    payload.mkdir(parents=True, exist_ok=True)
    state.mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.worker, payload / "ApparanceWorker.exe")
    shutil.copy2(args.library, payload / "ApparanceEngine.dll")
    copied = args.cache / "Procedures"
    if not copied.exists():
        shutil.copytree(args.procedures, copied)
    client = NativeClient(args.bridge, args.wine_directory, payload, state)
    receipt = replay(client, args.journal, args.library, copied)
    receipt["transport"] = "actual copied-buffer C ABI shim/socketpair/posix_spawn"
    receipt["reentrantLogCallbacks"] = client.callback_reentries
    # Save uses a disposable library copy; original game references stay read-only.
    initial = __import__("protocol").string(__import__("protocol").windows_path(args.library)) + __import__("protocol").string(__import__("protocol").windows_path(copied)) + struct.pack("<iii", 1, 35, 0)
    assert client.request(1, initial) == struct.pack("<i", 1)
    assert client.request(10) == struct.pack("<ii", 0, 0)
    client.request(4)
    client.request(5)
    args.output.write_text(json.dumps(receipt, indent=2) + "\n")
    print({key: value for key, value in receipt.items() if key != "originalCallbacks"})


if __name__ == "__main__":
    main()
