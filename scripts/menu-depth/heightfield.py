#!/usr/bin/env python3
"""Strict, portable GMD1 heightfield container; no model or color pixels are shipped."""
from __future__ import annotations
import struct
import zlib

HEADER = struct.Struct('<4sHHHHIIIII')
MAGIC = b'GMD1'
VERSION = 1
FLAGS = 3  # bit0: bottom-left row origin; bit1: normalized inverse depth.


def encode(width: int, height: int, source_width: int, source_height: int, payload: bytes) -> bytes:
    if not 1 <= width <= 512 or not 1 <= height <= 512:
        raise ValueError('GMD1 axes must be within1..512')
    if not 1 <= source_width <= 16384 or not 1 <= source_height <= 16384:
        raise ValueError('Invalid original sprite dimensions')
    if len(payload) != width * height * 2:
        raise ValueError('Truncated heightfield payload')
    header = HEADER.pack(MAGIC, VERSION, HEADER.size, width, height, FLAGS, len(payload),
                         zlib.crc32(payload), source_width, source_height)
    return header + payload


def decode(data: bytes) -> dict:
    if len(data) < HEADER.size:
        raise ValueError('Truncated GMD1 header')
    magic, version, header_size, width, height, flags, length, checksum, sw, sh = HEADER.unpack_from(data)
    if magic != MAGIC or version != VERSION or header_size != HEADER.size or flags != FLAGS:
        raise ValueError('Unsupported GMD1 identity, flags or version')
    payload = data[header_size:]
    if encode(width, height, sw, sh, payload) != data or length != len(payload) or zlib.crc32(payload) != checksum:
        raise ValueError('Corrupt GMD1 payload or metadata')
    return {'width': width, 'height': height, 'source_width': sw, 'source_height': sh,
            'payload': payload, 'crc32': checksum, 'flags': flags}
