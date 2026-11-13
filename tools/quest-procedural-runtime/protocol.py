"""Host proof client for the same copied-buffer protocol used by the ARM64 shim."""
from __future__ import annotations
import os
from pathlib import Path
import select
import struct
import subprocess
import time

MAGIC, VERSION, MAX_PAYLOAD = 0x52504847, 1, 128 * 1024 * 1024
HEADER = struct.Struct("<IHHIIi")


def string(value: str | bytes) -> bytes:
    data = value.encode("utf-8") if isinstance(value, str) else value
    if b"\0" in data:
        raise ValueError("NUL in worker path/descriptor")
    return struct.pack("<I", len(data)) + data


def windows_path(value: Path) -> str:
    """Use the private Wine prefix's default Z: mapping; no original file is changed."""
    return "Z:" + str(Path(value).resolve()).replace("/", "\\")


class Client:
    def __init__(self, command: list[str], environment: dict[str, str], stderr, timeout: float = 30):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=stderr, env=environment)
        self.sequence = 0
        self.timeout = timeout
        self.logs: list[tuple[int, bytes]] = []
        self.journal: list[dict] = []

    def _read(self, size: int, deadline: float) -> bytes:
        chunks = bytearray()
        while len(chunks) < size:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("Original engine worker response timed out")
            ready, _, _ = select.select([self.process.stdout], [], [], remaining)
            if not ready:
                raise TimeoutError("Original engine worker response timed out")
            part = os.read(self.process.stdout.fileno(), size - len(chunks))
            if not part:
                raise RuntimeError(f"Original engine worker ended ({self.process.poll()})")
            chunks += part
        return bytes(chunks)

    def request(self, operation: int, payload: bytes = b"") -> bytes:
        if len(payload) > MAX_PAYLOAD:
            raise ValueError("Worker request exceeds its copied-buffer limit")
        self.sequence += 1
        self.process.stdin.write(HEADER.pack(MAGIC, VERSION, operation, self.sequence, len(payload), 0) + payload)
        self.process.stdin.flush()
        deadline = time.monotonic() + self.timeout
        while True:
            magic, version, returned, sequence, length, status = HEADER.unpack(self._read(HEADER.size, deadline))
            if magic != MAGIC or version != VERSION or length > MAX_PAYLOAD:
                raise RuntimeError("Original engine worker returned an incompatible/oversized frame")
            content = self._read(length, deadline)
            if returned == 0x100:
                self.logs.append((status, content))
                continue
            if returned != operation or sequence != self.sequence:
                raise RuntimeError("Original engine worker response does not match its request")
            self.journal.append({"operation": operation, "input": payload, "output": content, "status": status})
            if status:
                raise RuntimeError(f"Original engine worker operation {operation} failed ({status})")
            return content

    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.terminate()
                self.process.wait(timeout=5)
