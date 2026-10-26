#!/usr/bin/env python3
"""Read-only, source-bound inventory of native narrative and compendium art."""
from __future__ import annotations

import argparse
from collections import Counter
import gc
import hashlib
import json
from pathlib import Path


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path: Path, value) -> None:
    temporary = path.with_suffix(path.suffix + '.pending')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def rect(value) -> list[float]:
    return [float(value.x), float(value.y), float(value.width), float(value.height)]


def vector(value) -> list[float]:
    return [float(value.x), float(value.y)]


def canonical_asset(path: str) -> str:
    path = path.replace('\\', '/').lower()
    prefix = 'assets/content/gui/loadouts/'
    return path.removeprefix(prefix) if path.startswith(prefix) else path


def export_sprite(obj, asset_path: str, bundle: Path, output: Path) -> dict:
    """Keep the native logical sprite coordinate system, not its packed atlas rect."""
    from PIL import Image
    sprite = obj.read()
    texture = sprite.m_RD.texture.read()
    logical = tuple(round(value) for value in (sprite.m_Rect.width, sprite.m_Rect.height))
    if min(logical) <= 0:
        raise ValueError('Zero-sized native sprite: ' + asset_path)
    full_texture = (texture.m_Width, texture.m_Height) == logical and sprite.m_Rect.x == sprite.m_Rect.y == 0
    if full_texture:
        image = texture.image.convert('RGBA')
        reconstruction = 'native-full-texture'
    else:
        cropped = sprite.image.convert('RGBA')
        image = Image.new('RGBA', logical)
        offset = sprite.m_RD.textureRectOffset
        x, y = round(offset.x), logical[1] - round(offset.y) - cropped.height
        if x < 0 or y < 0 or x + cropped.width > logical[0] + 1 or y + cropped.height > logical[1] + 1:
            raise ValueError('Packed sprite cannot be mapped to its original logical rect: ' + asset_path)
        image.paste(cropped, (x, y))
        reconstruction = 'native-trimmed-sprite-padded-to-logical-rect'
    if image.size != logical:
        raise ValueError('Native sprite logical dimensions changed')
    raw_sha = hashlib.sha256(image.tobytes()).hexdigest()
    source_id = raw_sha[:32]
    target = output / 'sources' / (source_id + '.png')
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        with Image.open(target) as previous:
            if previous.size != image.size or hashlib.sha256(previous.convert('RGBA').tobytes()).hexdigest() != raw_sha:
                raise ValueError('Conflicting or corrupt source snapshot: ' + str(target))
    else:
        image.save(target)
    return {
        'source_id': source_id, 'asset_path': asset_path.replace('\\', '/'),
        'canonical_asset': canonical_asset(asset_path), 'bundle': bundle.name,
        'sprite_path_id': obj.path_id, 'sprite_name': sprite.m_Name,
        'texture_name': texture.m_Name, 'texture_dimensions': [texture.m_Width, texture.m_Height],
        'logical_dimensions': list(logical), 'sprite_rect': rect(sprite.m_Rect),
        'texture_rect': rect(sprite.m_RD.textureRect),
        'texture_rect_offset': vector(sprite.m_RD.textureRectOffset),
        'sprite_pivot': vector(sprite.m_Pivot), 'packing_settings': int(sprite.m_RD.settingsRaw),
        'downscale_multiplier': float(sprite.m_RD.downscaleMultiplier),
        'raster_reconstruction': reconstruction, 'rgba_sha256': raw_sha,
        'source_snapshot': str(target.relative_to(output)), 'source_png_sha256': digest(target),
    }


def scan(data_root: Path, output: Path, include_compendium: bool = True) -> dict:
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    output.mkdir(parents=True, exist_ok=True)
    generator = TypeTreeGenerator('2021.3.5f1')
    generator.load_local_dll_folder(str(data_root / 'Managed'))
    original = UnityPy.load(str(data_root / 'level1'))
    original.typetree_generator = generator
    owner = next(obj for obj in original.objects if obj.path_id == 11386)
    tools = owner.read_typetree()
    narrative = tools['narrativeImages']
    if len({row['id'].lower() for row in narrative}) != len(narrative):
        raise ValueError('Native narrative IDs are not case-insensitively unique')
    bundles = sorted((data_root / 'StreamingAssets/aa/StandaloneWindows64').glob('loadouts_assets_*.bundle'))
    if include_compendium:
        bundles += [data_root / 'StreamingAssets/aa/StandaloneWindows64/compendiumscreenshots_assets_all.bundle']
    artwork, bundle_pins = [], []
    for bundle in bundles:
        env = UnityPy.load(str(bundle))
        asset_by_object = {(pointer.m_FileID, pointer.m_PathID): name for name, pointer in env.container.items()}
        sprites = [obj for obj in env.objects if obj.type.name == 'Sprite']
        for obj in sprites:
            sprite = obj.read()
            pointer = sprite.m_RD.texture
            asset = asset_by_object.get((pointer.m_FileID, pointer.m_PathID)) or asset_by_object.get((0, obj.path_id))
            if asset is None:
                raise ValueError('Native sprite has no exact asset/texture container binding: ' + sprite.m_Name)
            record = export_sprite(obj, asset, bundle, output)
            record['kind'] = 'compendium' if bundle.name.startswith('compendium') else 'narrative'
            artwork.append(record)
        bundle_pins.append({'path': str(bundle.relative_to(data_root)), 'bytes': bundle.stat().st_size,
                            'sha256': digest(bundle), 'sprites': len(sprites)})
        print(json.dumps({'bundle': bundle.name, 'sprites': len(sprites)}), flush=True)
        del env, sprites
        gc.collect()
    by_path = {}
    for art in artwork:
        key = art['canonical_asset']
        if key in by_path:
            raise ValueError('Ambiguous exact native image path: ' + key)
        by_path[key] = art
    rows = []
    for row in narrative:
        picture = row['picture']
        if picture['m_PathID']:
            raise ValueError('Unexpected embedded narrative picture needs explicit export: ' + row['id'])
        key = row['pathPicture'].replace('\\', '/').lower() + '.png'
        if key not in by_path:
            raise ValueError('Native narrative config has no matching actual sprite: ' + row['id'] + ' / ' + key)
        rows.append({'id': row['id'], 'path_picture': row['pathPicture'],
                     'source_id': by_path[key]['source_id'], 'canonical_asset': key})
    embedded_envs = {}
    for field in ('defaultEventImage', 'defaultCityEventImage'):
        pointer = tools[field]
        if not pointer['m_PathID'] or pointer['m_FileID'] <= 0:
            raise ValueError('Missing native event fallback: ' + field)
        filename = Path(owner.assets_file.externals[pointer['m_FileID'] - 1].path).name
        if filename not in embedded_envs:
            embedded_envs[filename] = UnityPy.load(str(data_root / filename))
        obj = next(obj for obj in embedded_envs[filename].objects if obj.path_id == pointer['m_PathID'])
        art = export_sprite(obj, 'Native/' + field, data_root / filename, output)
        art['kind'] = 'event-fallback'; art['native_field'] = field
        artwork.append(art)
    referred = {row['source_id'] for row in rows}
    extra = [art for art in artwork if art['kind'] == 'narrative' and art['source_id'] not in referred]
    if extra:
        raise ValueError('Loadout sprite has no native narrative ID: ' + ','.join(art['asset_path'] for art in extra))
    unique = {}
    for art in artwork:
        previous = unique.setdefault(art['source_id'], art)
        if previous['rgba_sha256'] != art['rgba_sha256'] or previous['logical_dimensions'] != art['logical_dimensions']:
            raise ValueError('Source ID collision')
    result = {'schema': 1, 'native_owner': {'asset': 'level1', 'path_id': 11386, 'class': 'UIInfoTools'},
        'unitypy_version': UnityPy.__version__, 'narrative_configs': rows,
        'artwork': artwork, 'unique_sources': len(unique),
        'counts': dict(Counter(art['kind'] for art in artwork)), 'source_bundles': bundle_pins,
        'native_owner_sha256': digest(data_root / 'level1'),
        'game_runtime_sha256': digest(data_root / 'Managed/GH.Runtime.dll'),
        'embedded_asset_pins': [{'path': name, 'sha256': digest(data_root / name)} for name in embedded_envs]}
    write_json(output / 'catalogue.json', result)
    print(json.dumps({'status': 'complete', 'narrative_ids': len(rows), 'artwork': len(artwork),
                      'unique_sources': len(unique), 'counts': result['counts']}), flush=True)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--without-compendium', action='store_true')
    args = parser.parse_args()
    scan(args.data_root.resolve(), args.out.resolve(), not args.without_compendium)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
