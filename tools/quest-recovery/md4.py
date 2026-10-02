"""Small RFC 1320 MD4 implementation for Unity's managed-script file IDs.

MD4 is used exclusively to reproduce a non-security Unity identifier. Content
integrity and download verification use SHA-256 instead.
"""
import struct


def digest(data):
    original_length = len(data)
    data += b"\x80" + b"\0" * ((55 - original_length) % 64)
    data += struct.pack("<Q", original_length * 8)
    state = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476]

    def rotate(value, count):
        value &= 0xFFFFFFFF
        return ((value << count) | (value >> (32 - count))) & 0xFFFFFFFF

    for start in range(0, len(data), 64):
        words = struct.unpack("<16I", data[start:start + 64])
        a, b, c, d = state
        for round_number in range(3):
            order = (list(range(16)) if round_number == 0 else
                     [j + i for i in range(4) for j in (0, 4, 8, 12)] if round_number == 1 else
                     [0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15])
            shifts = ((3, 7, 11, 19), (3, 5, 9, 13), (3, 9, 11, 15))[round_number]
            for i, index in enumerate(order):
                value = ((b & c) | (~b & d) if round_number == 0 else
                         (b & c) | (b & d) | (c & d) if round_number == 1 else b ^ c ^ d)
                constant = (0, 0x5A827999, 0x6ED9EBA1)[round_number]
                a = rotate(a + value + words[index] + constant, shifts[i % 4])
                a, b, c, d = d, a, b, c
        state = [(old + new) & 0xFFFFFFFF for old, new in zip(state, (a, b, c, d))]
    return struct.pack("<4I", *state)


def script_file_id(namespace, name):
    return struct.unpack("<i", digest(b"s\0\0\0" + namespace.encode("utf-8") + name.encode("utf-8"))[:4])[0]
