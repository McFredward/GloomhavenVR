"""Change only Android version fields in a compiled AXML manifest.

The string pool is extended, retaining every old index, resource map, namespace,
activity, permission and package attribute. No XML-to-AXML recompile is needed.
"""
import struct
from storage import BuildError

ANDROID = "http://schemas.android.com/apk/res/android"


def _length(data, offset, utf8):
    unit = 1 if utf8 else 2
    value = int.from_bytes(data[offset:offset + unit], "little"); offset += unit
    marker = 0x80 if utf8 else 0x8000
    if value & marker:
        next_value = int.from_bytes(data[offset:offset + unit], "little"); offset += unit
        value = ((value & (marker - 1)) << (8 if utf8 else 16)) | next_value
    return value, offset


def _encode_length(value, utf8):
    if utf8:
        if value >= 32768: raise BuildError("Android version string is excessive.")
        return bytes([(value >> 8) | 0x80, value & 0xff]) if value >= 128 else bytes([value])
    if value >= 32768: raise BuildError("Android version string is excessive.")
    return struct.pack("<H", value)


def _pool(chunk, added):
    kind, header, size, count, styles, flags, start, style_start = struct.unpack_from("<HH6I", chunk)
    if kind != 1 or header < 28 or size != len(chunk) or count > 65536 or header + 4 * (count + styles) > start:
        raise BuildError("Android manifest string pool is invalid.")
    offsets = struct.unpack_from("<" + "I" * count, chunk, header)
    strings = []; utf8 = bool(flags & 0x100)
    for offset in offsets:
        position = start + offset
        length, position = _length(chunk, position, utf8)
        if utf8: length, position = _length(chunk, position, True)
        byte_length = length if utf8 else length * 2
        raw = chunk[position:position + byte_length]
        if len(raw) != byte_length: raise BuildError("Android manifest string is truncated.")
        strings.append(raw.decode("utf-8" if utf8 else "utf-16le"))
    end = style_start or len(chunk)
    if not start <= end <= len(chunk): raise BuildError("Android manifest string/style boundaries are invalid.")
    data = chunk[start:end]
    new_offset = len(data)
    # Sorted flag no longer applies when appending; all existing indices survive.
    encoded = added.encode("utf-8" if utf8 else "utf-16le")
    unit_length = len(added.encode("utf-16le")) // 2
    encoded = _encode_length(unit_length, utf8) + (_encode_length(len(encoded), True) if utf8 else b"") + encoded + (b"\0" if utf8 else b"\0\0")
    data += encoded
    data += b"\0" * (-len(data) % 4)
    prefix = bytearray(chunk[:start]); prefix[header + count * 4:header + count * 4] = struct.pack("<I", new_offset)
    new_start = start + 4
    new_style_start = new_start + len(data) if style_start else 0
    cooked = prefix + data + chunk[end:]
    struct.pack_into("<I", cooked, 4, len(cooked)); struct.pack_into("<I", cooked, 8, count + 1)
    struct.pack_into("<I", cooked, 16, flags & ~1); struct.pack_into("<I", cooked, 20, new_start); struct.pack_into("<I", cooked, 24, new_style_start)
    return bytes(cooked), strings, count


def update_versions(raw, version_code, version_name, *, package="dev.gloomhavenvr.quest"):
    if type(version_code) is not int or not 0 < version_code <= 0x7fffffff or not isinstance(version_name, str) or not 0 < len(version_name) <= 128:
        raise BuildError("Android update version is invalid.")
    if len(raw) < 8 or len(raw) > 4 * 1024 * 1024 or struct.unpack_from("<HHI", raw) != (3, 8, len(raw)):
        raise BuildError("A compiled Android XML manifest is required.")
    chunks = []; offset = 8; strings = None; added_index = None; counts = {"package": 0, "versionCode": 0, "versionName": 0}
    while offset < len(raw):
        if offset + 8 > len(raw): raise BuildError("Android XML chunk is truncated.")
        kind, header, size = struct.unpack_from("<HHI", raw, offset)
        if size < header or header < 8 or offset + size > len(raw): raise BuildError("Android XML chunk boundary is invalid.")
        chunk = bytearray(raw[offset:offset + size]); offset += size
        if kind == 1:
            if strings is not None: raise BuildError("Android XML repeats its string pool.")
            chunk, strings, added_index = _pool(chunk, version_name)
        elif kind == 0x102:
            if strings is None or header != 16 or len(chunk) < 36: raise BuildError("Android XML element header is invalid.")
            element = struct.unpack_from("<I", chunk, 20)[0]
            if element >= len(strings): raise BuildError("Android XML element string index is invalid.")
            if strings[element] == "manifest":
                attribute_start, attribute_size, count = struct.unpack_from("<HHH", chunk, 24)
                if attribute_size < 20 or 16 + attribute_start + count * attribute_size > len(chunk): raise BuildError("Android manifest attributes are truncated.")
                for index in range(count):
                    position = 16 + attribute_start + index * attribute_size
                    ns, name, value = struct.unpack_from("<III", chunk, position)
                    if name >= len(strings): raise BuildError("Android manifest attribute index is invalid.")
                    key = strings[name]; namespace = "" if ns == 0xffffffff else strings[ns]
                    data_type = chunk[position + 15]; data = struct.unpack_from("<I", chunk, position + 16)[0]
                    if key == "package" and namespace == "":
                        if data_type != 3 or data >= len(strings) or strings[data] != package: raise BuildError("Android APK package does not match the Quest update target.")
                        counts[key] += 1
                    elif key in ("versionCode", "versionName") and namespace == ANDROID:
                        counts[key] += 1
                        if key == "versionCode":
                            struct.pack_into("<I", chunk, position + 8, 0xffffffff); chunk[position + 15] = 0x10; struct.pack_into("<I", chunk, position + 16, version_code)
                        else:
                            struct.pack_into("<I", chunk, position + 8, added_index); chunk[position + 15] = 3; struct.pack_into("<I", chunk, position + 16, added_index)
        chunks.append(bytes(chunk))
    if counts != {"package": 1, "versionCode": 1, "versionName": 1}:
        raise BuildError("Android manifest needs unique package/version fields.")
    result = b"".join(chunks)
    return struct.pack("<HHI", 3, 8, len(result) + 8) + result
