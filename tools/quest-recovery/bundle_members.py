"""Read UnityFS serialized member names without loading texture/mesh payloads.

The field order follows the UnityFS reader in the pinned UnityPy package. Only
the compressed block/directory metadata is decompressed; asset bytes remain in
the read-only original bundle. Unsupported formats/compression fail explicitly.
"""
from pathlib import Path
import struct

from recover import RecoveryError


def _cstring(stream):
    result = bytearray()
    while True:
        value = stream.read(1)
        if not value or len(result) > 65536:
            raise RecoveryError("Invalid UnityFS metadata string.")
        if value == b"\0":
            return result.decode("utf-8")
        result.extend(value)


def serialized_members(path):
    with Path(path).open("rb") as stream:
        if _cstring(stream) != "UnityFS":
            raise RecoveryError("Full recovery currently requires UnityFS bundle metadata.")
        version = struct.unpack(">I", stream.read(4))[0]
        engine, revision = _cstring(stream), _cstring(stream)
        size, compressed, uncompressed, flags = struct.unpack(">QIII", stream.read(20))
        if size != Path(path).stat().st_size or not 0 < compressed <= 32 * 1024 * 1024 or not 0 < uncompressed <= 128 * 1024 * 1024:
            raise RecoveryError("Invalid or excessive UnityFS directory metadata.")
        if version >= 7:
            stream.seek((stream.tell() + 15) & ~15)
        if flags & 0x80:
            stream.seek(size - compressed)
        data = stream.read(compressed)
    compression = flags & 0x3f
    if compression == 0:
        metadata = data
    elif compression in (2, 3):
        try:
            import lz4.block
        except ImportError as error:
            raise RecoveryError("LZ4 is required for UnityFS block metadata.") from error
        metadata = lz4.block.decompress(data, uncompressed_size=uncompressed)
    else:
        raise RecoveryError("Unsupported original UnityFS directory compression: " + str(compression))
    if len(metadata) != uncompressed:
        raise RecoveryError("UnityFS directory metadata size differs.")
    import io
    stream = io.BytesIO(metadata)
    stream.seek(16)  # Original data hash.
    count = struct.unpack(">I", stream.read(4))[0]
    if count > 1_000_000:
        raise RecoveryError("Excessive UnityFS block count.")
    stream.seek(stream.tell() + count * 10)
    count = struct.unpack(">I", stream.read(4))[0]
    if count > 100_000:
        raise RecoveryError("Excessive UnityFS node count.")
    nodes = []
    for _ in range(count):
        offset, size, flags = struct.unpack(">QQI", stream.read(20))
        name = _cstring(stream)
        if flags & 4:  # UnityFS serialized assets file, excluding .resS data.
            nodes.append({"name": name, "offset": offset, "bytes": size})
    if stream.tell() != len(metadata):
        raise RecoveryError("UnityFS directory metadata has an unknown trailing layout.")
    return nodes
