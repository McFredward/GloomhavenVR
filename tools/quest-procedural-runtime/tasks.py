"""Strict original task decoder used only for deterministic host proof comparison.

Original Apparance.Net.Entity.DecodeAddObject defines this stream. Normalize only
opaque object-set keys and numeric resource handles by their exact descriptor.
Every parameter, group name, frame, mesh, UV, colour, triangle and tier byte stays
unchanged. Runtime forwards the untouched native stream.
"""
from __future__ import annotations
import struct


def canonical_task(data: bytes, resources: dict[int, int]) -> bytes:
    output = bytearray(data)
    cursor = 0
    keys = {}

    def take(size):
        nonlocal cursor
        if size < 0 or cursor + size > len(output):
            raise ValueError("Truncated original native task")
        start = cursor
        cursor += size
        return start

    def integer():
        return struct.unpack_from("<i", output, take(4))[0]

    def marker(value):
        if integer() != value:
            raise ValueError(f"Original native task section differs: {value:08x}")

    def resource():
        position = cursor
        value = integer()
        if value:
            if value not in resources:
                raise ValueError(f"Native task uses unknown resource handle {value}")
            struct.pack_into("<i", output, position, resources[value])
        return value

    while cursor < len(output):
        marker(0x41414141)
        position = cursor
        key = integer()
        if key in keys:
            raise ValueError("Repeated object-set key needs lifecycle-aware fixture")
        keys[key] = len(keys) + 1
        struct.pack_into("<i", output, position, keys[key])
        take(16)  # original tier and three-float origin
        marker(0x4f4f4f4f)
        count = integer()
        for unused in range(count):
            group = integer()
            if group > 0:
                take(integer())  # original UTF-8 group bytes
            if resource():
                take(integer())  # exact nested ParameterCollection bytes
        marker(0x47474747)
        vertices = integer()
        if vertices:
            marker(0x56565656); take(vertices * 12)
            marker(0x4e4e4e4e); take(vertices * 12)
            marker(0x43434343)
            colours = integer(); take(colours * vertices * 4)
            marker(0x55555555)
            uvs = integer(); take(uvs * vertices * 8)
            marker(0x50505050)
            parts = integer()
            for unused in range(parts):
                marker(0x54545454); resource()
                take(28)  # original min/max bounds and base vertex
                take(integer() * 4)
    return bytes(output)
