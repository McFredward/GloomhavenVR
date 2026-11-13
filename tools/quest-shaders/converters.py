"""Verify and unpack the pinned open-source-only Windows shader converters."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ARCHIVE_SHA256 = '61f7d664384b12663fb4fb799ffb8566bf11e99e15ce72c7afb00d5b62199f2d'
BINARIES = {'vkd3d-compiler.exe': '53e9e8e46630fe849fb75163f2237fe6a1d9cdff88953bd2e650d5b64351cb96',
            'spirv-cross.exe': '3dc3d73f0d6db3a31d09e76c5dea7fb63931a1da5edb3f3f54a8e4922aa6d464'}


def _hash(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def ensure(cache, tool_archive=None, platform=None):
    """Return {vkd3d: Path, spirv_cross: Path} without a Windows SDK install.

    Windows release archives supply the checked tool ZIP beside the builder or
    explicitly through tool_archive. Linux proof workers may use the witnessed
    distro tools; the returned paths are passed into full_shaders.inventory.
    """
    platform = platform or ('win64' if os.name == 'nt' else 'linux')
    if platform == 'linux':
        paths = {'vkd3d': shutil.which('vkd3d-compiler'), 'spirv_cross': shutil.which('spirv-cross')}
        if not all(paths.values()):
            raise RuntimeError('Linux Campaign shader recovery needs vkd3d-compiler1.2 and SPIRV-Cross SDK1.3.239; use build_converters.py for source provenance.')
        version = subprocess.run([paths['vkd3d'], '--version'], capture_output=True, text=True, check=True).stdout
        if 'version 1.2 ' not in version:
            raise RuntimeError('Linux vkd3d shader converter version differs from its proof.')
        return {name: Path(path) for name, path in paths.items()}
    if platform != 'win64':
        raise RuntimeError('No validated Campaign shader converter package for this host platform.')
    cache = Path(cache).resolve()
    destination = cache / 'quest-converters-win64-v1'
    if (destination / 'manifest.json').is_file() and all((destination / name).is_file() and _hash(destination / name) == digest for name, digest in BINARIES.items()):
        return {'vkd3d': destination / 'vkd3d-compiler.exe', 'spirv_cross': destination / 'spirv-cross.exe'}
    archive = Path(tool_archive) if tool_archive else Path(__file__).parent / 'quest-converters-win64-v1.zip'
    if not archive.is_file():
        raise RuntimeError('The Windows builder archive is missing its checked quest-converters-win64-v1.zip; provide tool_archive from the builder release.')
    if _hash(archive) != ARCHIVE_SHA256:
        raise RuntimeError('Pinned Windows converter package bytes differ.')
    cache.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='quest-converters-', dir=cache) as temporary:
        staged = Path(temporary)
        with zipfile.ZipFile(archive) as package:
            if len(package.infolist()) > 32 or sum(row.file_size for row in package.infolist()) > 32 * 1024 * 1024:
                raise RuntimeError('Converter package exceeds its pinned layout bounds.')
            for row in package.infolist():
                parts = row.filename.split('/')
                if any(value in ('', '.', '..') for value in parts) or '\\' in row.filename or ':' in row.filename:
                    raise RuntimeError('Converter package contains an unsafe path.')
                target = staged.joinpath(*parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(package.read(row))
        manifest = json.loads((staged / 'manifest.json').read_text())
        if manifest.get('schema') != 1 or manifest.get('platform') != 'win64' or manifest.get('proprietaryGameBytesIncluded') is not False:
            raise RuntimeError('Converter package provenance contract is invalid.')
        for name, data in manifest['files'].items():
            target = staged / name
            if not target.is_file() or target.stat().st_size != data['bytes'] or _hash(target) != data['sha256']:
                raise RuntimeError('Checked converter package file differs: ' + name)
        for name, digest in BINARIES.items():
            if _hash(staged / name) != digest or (staged / name).read_bytes()[:2] != b'MZ':
                raise RuntimeError('Checked converter Windows executable differs: ' + name)
        if destination.exists():
            shutil.rmtree(destination)
        shutil.copytree(staged, destination)
    return {'vkd3d': destination / 'vkd3d-compiler.exe', 'spirv_cross': destination / 'spirv-cross.exe'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--tool-archive', type=Path)
    args = parser.parse_args()
    print(json.dumps({name: str(path) for name, path in ensure(args.cache, args.tool_archive).items()}, sort_keys=True))


if __name__ == '__main__':
    main()
