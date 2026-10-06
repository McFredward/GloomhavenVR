#!/usr/bin/env python3
"""Export the original highlighter and its original pooled area read-only."""
import hashlib
import json
from pathlib import Path
import sys

root, out = map(Path, sys.argv[1:])
exporter = root / 'scripts/town-first-picture632-runtime/export-native.py'
source = exporter.read_text()
anchor = "visit(obj('sharedassets4.assets',226),-1)"
assert source.count(anchor) == 1
source = source.replace(anchor, "visit(obj('level4',2849),-1)", 1).replace(
    "'prefab':['sharedassets4.assets',226]", "'prefab':['level4',2849]")
sys.argv = [str(exporter), str(root / 'ressources/GH_Data'), str(out)]
namespace = {'__name__': '__main__'}
exec(compile(source, str(exporter), 'exec'), namespace)
nodes = namespace['nodes']
assert len(nodes) == 11 and nodes[0]['name'] == 'CardHilight'
assert any(node['pathID'] == 2040 for node in nodes), 'Original pooled area is absent'
native_clock = root / 'decompiled/GH.Runtime/UIEnchantressEffect.cs'
if not native_clock.exists():
    # Worktree dependencies deliberately do not copy the entire decompilation.
    native_clock = (root / 'ressources').resolve().parent / 'decompiled/GH.Runtime/UIEnchantressEffect.cs'
clock = native_clock.read_text()
assert 'enchantressEffect.transform.eulerAngles = new Vector3(0f, 0f, val);' in clock
assert 'private float rotationTime = 20f;' in clock
area = namespace['obj']('level4', 15285).read_typetree()
assert area['m_GameObject']['m_PathID'] == 2040
provenance = json.loads((out / 'provenance.json').read_text())
provenance.update(native_clock_sha256=hashlib.sha256(native_clock.read_bytes()).hexdigest(),
                  original_area=['level4', 2040, 15285],
                  native_area_opacity_hover=area['opacityHovered'],
                  native_area_opacity_selected=area['opacitySelected'],
                  limitation='Exact serialized highlighter, ring sprite, pooled area hierarchy and layout. Full card-model construction and native phase/hover clocks are declared fixture inputs. Production fitting/capture/wire/playback; equivalent editor UI shader, not the game effect shader or live headset pixels.')
(out / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
