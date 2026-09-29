#!/usr/bin/env python3
"""Export original town-service reference textures without changing game files.

Requires UnityPy. Outputs decoded native-resolution PNGs and a provenance manifest.
This is an offline developer/art workflow, not part of the mod or its installer.
"""

import argparse
import hashlib
import json
from pathlib import Path


TARGETS = {
    "GuildBackground_Merchant": "merchant-original.png",
    "Guild_Background_Temple": "priestess-original.png",
    "Guild_Background_Enchantress": "enchantress-original.png",
    "Portrait_Merchant": "merchant-portrait.png",
    "Portrait_Priestess": "priestess-portrait.png",
    "Portrait_Enchantress": "enchantress-portrait.png",
}


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game_data", type=Path, help="Path to the original GH_Data directory")
    parser.add_argument("output", type=Path, help="New or empty export directory outside GH_Data")
    args = parser.parse_args()
    source = args.game_data.resolve()
    output = args.output.resolve()
    if not (source / "resources.assets").is_file():
        parser.error("game_data must contain the game's resources.assets")
    if output == source or source in output.parents:
        parser.error("Output must be outside the read-only game data directory")
    if output.exists() and any(output.iterdir()):
        parser.error("Output directory is not empty; choose a new directory")

    import UnityPy

    output.mkdir(parents=True, exist_ok=True)
    entries = []
    found = set()
    source_hashes = {}
    for asset_file in sorted(source.glob("*.assets")):
        env = UnityPy.load(str(asset_file))
        for obj in env.objects:
            if obj.type.name != "Texture2D":
                continue
            name = obj.peek_name()
            if name not in TARGETS:
                continue
            if name in found:
                raise RuntimeError(f"Ambiguous duplicate texture {name}; inspect assets manually")
            texture = obj.read()
            pixels = texture.image
            if pixels.size != (texture.m_Width, texture.m_Height):
                raise RuntimeError(f"Unexpected decoded dimensions for {name}: {pixels.size}")
            target = output / TARGETS[name]
            pixels.save(target, format="PNG")
            dependencies = [asset_file]
            stream_data = texture.m_StreamData
            if stream_data and stream_data.path:
                dependency = asset_file.parent / stream_data.path
                if not dependency.is_file():
                    raise RuntimeError(f"Cannot record texture stream provenance: {dependency}")
                dependencies.append(dependency)
            for dependency in dependencies:
                relative = str(dependency.relative_to(source))
                if relative not in source_hashes:
                    source_hashes[relative] = sha256(dependency)
            entries.append({
                "file": target.name,
                "texture_name": name,
                "serialized_file": asset_file.name,
                "path_id": obj.path_id,
                "stream_path": stream_data.path if stream_data else None,
                "width": texture.m_Width,
                "height": texture.m_Height,
                "unity_texture_format": int(texture.m_TextureFormat),
                "png_mode": pixels.mode,
                "sha256": sha256(target),
            })
            found.add(name)
            print(f"{target.name}: {pixels.width}x{pixels.height}, {asset_file.name}:{obj.path_id}")
    missing = sorted(TARGETS.keys() - found)
    manifest = {
        "unitypy_version": UnityPy.__version__,
        "method": "Texture2D decode; no resize, crop, retouch, cutout or generated pixels",
        "sources_sha256": source_hashes,
        "images": sorted(entries, key=lambda entry: entry["file"]),
        "missing_textures": missing,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if missing:
        raise SystemExit("Incomplete export; missing: " + ", ".join(missing))


if __name__ == "__main__":
    main()
