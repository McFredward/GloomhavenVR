"""Build the pinned open-source Windows converter package on a Linux worker.

The player consumes the resulting checked ZIP and needs no C/C++ compiler. This
developer recipe uses MinGW-w64, CMake, Ninja, Make and official upstream source
archives. It builds only vkd3d's shader compiler; no Vulkan loader is linked.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

SOURCES = {
    'vkd3d-1.2.tar.xz': ('https://dl.winehq.org/vkd3d/source/vkd3d-1.2.tar.xz',
        'b04b030fcbf0f2dacc933c76c74b449bffef1fc1a18d50254ef1ad3e380df96b'),
    'SPIRV-Cross-sdk-1.3.239.0.tar.gz': ('https://codeload.github.com/KhronosGroup/SPIRV-Cross/tar.gz/refs/tags/sdk-1.3.239.0',
        'a1695022880e7ef3c2d407647f79876045dc2a3ed012753adc71ead5cc5178ba'),
    'Vulkan-Headers-sdk-1.3.239.0.tar.gz': ('https://codeload.github.com/KhronosGroup/Vulkan-Headers/tar.gz/refs/tags/sdk-1.3.239.0',
        '865fa8e8e8314fcca60777a92f50bd0cf612205a36e719d6975482d3366f619e'),
    'SPIRV-Headers-sdk-1.3.239.0.tar.gz': ('https://codeload.github.com/KhronosGroup/SPIRV-Headers/tar.gz/refs/tags/sdk-1.3.239.0',
        'fdaf6670e311cd1c08ae90bf813e89dd31630205bc60030ffd25fb0af39b51fe'),
}


def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def command(args, cwd, logfile):
    with Path(logfile).open('w') as log:
        result = subprocess.run(list(map(str, args)), cwd=cwd, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError('Pinned converter source build failed: ' + str(logfile))


def build(root):
    root = Path(root).resolve()
    if os.name == 'nt':
        raise RuntimeError('This source recipe cross-compiles on Linux; Windows players use the generated checked package.')
    for tool in ('x86_64-w64-mingw32-gcc', 'x86_64-w64-mingw32-g++', 'cmake', 'ninja', 'make'):
        if shutil.which(tool) is None:
            raise RuntimeError('Developer converter source build requires ' + tool)
    for directory in ('sources', 'source', 'build/vkd3d-win64', 'build/spirv-cross-win64'):
        (root / directory).mkdir(parents=True, exist_ok=True)
    for name, (url, expected) in SOURCES.items():
        archive = root / 'sources' / name
        if not archive.is_file():
            temporary = archive.with_suffix(archive.suffix + '.partial')
            urllib.request.urlretrieve(url, temporary)
            if digest(temporary) != expected:
                temporary.unlink()
                raise RuntimeError('Official converter source archive hash differs: ' + name)
            temporary.replace(archive)
        if digest(archive) != expected:
            raise RuntimeError('Cached official converter source archive hash differs: ' + name)
        with tarfile.open(archive) as package:
            package.extractall(root / 'source', filter='data')
    vkd3d = root / 'source/vkd3d-1.2'
    working = root / 'build/vkd3d-win64'
    includes = '-I' + str(root / 'source/Vulkan-Headers-sdk-1.3.239.0/include')
    includes += ' -I' + str(root / 'source/SPIRV-Headers-sdk-1.3.239.0/include')
    # The configure probe covers the excluded D3D12 backend. Its standard
    # loader soname is not an imported dependency of the shader-only target.
    command([vkd3d / 'configure', '--host=x86_64-w64-mingw32', '--disable-tests', '--disable-demos',
        '--disable-shared', '--enable-static', '--disable-maintainer-mode', 'CPPFLAGS=' + includes,
        'CFLAGS=-O2 -static-libgcc', 'LDFLAGS=-static', 'ac_cv_lib_soname_vulkan=vulkan-1.dll'],
        working, working / 'configure-output.log')
    command(['make', '-j4', 'include/private/vkd3d_version.h', 'vkd3d-compiler.exe'], working, working / 'build-output.log')
    cross = root / 'build/spirv-cross-win64'
    command(['cmake', '-S', root / 'source/SPIRV-Cross-sdk-1.3.239.0', '-B', cross, '-G', 'Ninja',
        '-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc',
        '-DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++', '-DCMAKE_BUILD_TYPE=Release',
        '-DCMAKE_EXE_LINKER_FLAGS=-static', '-DSPIRV_CROSS_ENABLE_TESTS=OFF', '-DSPIRV_CROSS_ENABLE_C_API=OFF'],
        root, cross / 'configure-output.log')
    command(['cmake', '--build', cross, '--parallel', '4'], root, cross / 'build-output.log')
    return package(root)


def package(root):
    """Include complete corresponding upstream sources and the build recipe."""
    root = Path(root).resolve()
    binaries = {'vkd3d-compiler.exe': root / 'build/vkd3d-win64/vkd3d-compiler.exe',
                'spirv-cross.exe': root / 'build/spirv-cross-win64/spirv-cross.exe'}
    for path in binaries.values():
        if path.read_bytes()[:2] != b'MZ':
            raise RuntimeError('Converter package requires real Windows PE executables.')
    files = dict(binaries)
    for name, (_, expected) in SOURCES.items():
        path = root / 'sources' / name
        if digest(path) != expected:
            raise RuntimeError('Source provenance changed before converter packaging.')
        files['sources/' + name] = path
    files['licenses/vkd3d-LGPL-2.1.txt'] = root / 'source/vkd3d-1.2/COPYING'
    files['licenses/SPIRV-Cross-Apache-2.0.txt'] = root / 'source/SPIRV-Cross-sdk-1.3.239.0/LICENSE'
    files['build_converters.py'] = Path(__file__)
    manifest = {'schema': 1, 'platform': 'win64', 'vkd3dVersion': '1.2', 'spirvCrossVersion': 'sdk-1.3.239.0',
        'proprietaryGameBytesIncluded': False, 'sourceArchives': {name: {'url': url, 'sha256': expected} for name, (url, expected) in SOURCES.items()},
        'files': {name: {'sha256': digest(path), 'bytes': path.stat().st_size} for name, path in files.items()}}
    destination = root / 'quest-converters-win64-v1.zip'
    with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        values = {'manifest.json': (json.dumps(manifest, sort_keys=True, indent=2) + '\n').encode()}
        values.update((name, path.read_bytes()) for name, path in files.items())
        for name, data in sorted(values.items()):
            info = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    (root / 'package-proof.json').write_text(json.dumps({'archive': str(destination), 'sha256': digest(destination),
        'bytes': destination.stat().st_size, 'manifest': manifest}, sort_keys=True, indent=2) + '\n')
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--package-existing-build', action='store_true')
    args = parser.parse_args()
    result = package(args.cache) if args.package_existing_build else build(args.cache)
    print(str(result))
    print('SHA256=' + digest(result))


if __name__ == '__main__':
    main()
