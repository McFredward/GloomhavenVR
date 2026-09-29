#!/usr/bin/env python3
"""Author a separate glTF material variant without touching mesh or texture bytes.

Hunyuan's legal specularColorFactor=2 doubles dielectric F0 relative to glTF's
default. A factor of 1 is a deliberate less-glossy art proposal, not a format fix.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    if options.output.exists():
        parser.error("Output exists; do not overwrite authored or original assets")
    data = options.input.read_bytes()
    if data[:4] != b"glTF" or struct.unpack_from("<I", data, 4)[0] != 2:
        raise ValueError("Expected GLB 2")
    offset, chunks, changes = 12, [], []
    while offset < len(data):
        size, kind = struct.unpack_from("<II", data, offset)
        content = data[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            doc = json.loads(content)
            for index, material in enumerate(doc.get("materials", [])):
                specular = material.get("extensions", {}).get("KHR_materials_specular", {})
                if specular.get("specularColorFactor") == [2, 2, 2]:
                    changes.append({"material": index, "specularColorFactor_before": [2, 2, 2], "after": [1, 1, 1]})
                    specular["specularColorFactor"] = [1, 1, 1]
            content = json.dumps(doc, separators=(",", ":")).encode()
            content += b" " * (-len(content) % 4)
        chunks.append(struct.pack("<II", len(content), kind) + content)
        offset += 8 + size
    if not changes:
        raise ValueError("No expected Hunyuan specular factor found; no silent material change")
    payload = b"".join(chunks)
    result = struct.pack("<4sII", b"glTF", 2, len(payload) + 12) + payload
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_bytes(result)
    report = {"input": str(options.input), "input_sha256": hashlib.sha256(data).hexdigest(),
              "output_sha256": hashlib.sha256(result).hexdigest(), "changes": changes,
              "geometry_and_texture_chunks": "byte-identical; only JSON material factor changed",
              "intent": "Authored nonmetal reflectance proposal, requires visual review; original factor 2 is legal glTF."}
    options.output.with_suffix(".json").write_text(json.dumps(report, indent=2) + "\n")
    print(options.output)


if __name__ == "__main__":
    main()
