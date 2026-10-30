"""Small explicit package companions; these bytes do not claim neural/media quality."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

# Independent portable GMD1 golden, not a copy of the packaging validator.
GOLDEN = bytes.fromhex('474d44310100200003000200030000000c00000090f3f8230c000000080000000000ffff0080002000c03930')


def write_companions(fixture: Path, source: Path) -> dict[str, bytes]:
    roster_data = (source / 'packaging/menu-depth-files.txt').read_bytes()
    paths = [line.strip() for line in roster_data.decode().splitlines() if line.strip() and not line.startswith('#')]
    assert len(paths) == len(set(paths)) == 516 and paths.count('Story/manifest.json') == 1
    images = []
    payloads = {}
    for name in paths:
        if name == 'Story/manifest.json':
            continue
        payloads[name] = GOLDEN
        source_id = Path(name).stem
        images.append({'source_id': source_id, 'source_dimensions': [12, 8],
                       'depth_dimensions': [3, 2], 'depth_file': source_id + '.gmd',
                       'depth_bytes': len(GOLDEN), 'depth_sha256': hashlib.sha256(GOLDEN).hexdigest()})
    payloads['Story/manifest.json'] = (json.dumps({'schema': 1, 'status': 'complete', 'format': 'GMD1',
        'fixture_scope': 'Tiny format-valid layout companions; no inference or artwork-quality claim.',
        'images': images}, sort_keys=True) + '\n').encode()
    roster = fixture / 'packaging/menu-depth-files.txt'
    roster.parent.mkdir(parents=True, exist_ok=True)
    roster.write_bytes(roster_data)
    for name, data in payloads.items():
        path = fixture / 'src/GloomhavenVR/Assets/MenuDepth' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    for name in ('menu-depth-files.py', 'menu-depth-files.ps1', 'install.ps1'):
        path = fixture / 'scripts' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((source / 'scripts' / name).read_bytes())
    return payloads
