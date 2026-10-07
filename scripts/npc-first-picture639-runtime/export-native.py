#!/usr/bin/env python3
"""Read original town presentation hierarchies without running game controllers.

This reuses the established binary grammar, but exports whole native inventory,
confirmation, highlight and item-slot hierarchies. No module is shrunk to text.
Fonts use the editor-equivalent TMP asset (explicit pixel-proof limitation).
"""
import hashlib, json, sys
from pathlib import Path
root, output = map(Path, sys.argv[1:])
exporter = root / 'scripts/town-first-picture632-runtime/export-native.py'
original = exporter.read_text()
anchor = "visit(obj('sharedassets4.assets',226),-1)"
for label, asset_file, path_id in [
    ('row', 'sharedassets4.assets',226),
    ('inventory','level4',2240),
    ('highlight','level4',2849),
    ('mage-confirm','level4',2743),
    ('merchant-confirm','level4',2776),
    ('item-card','sharedassets6.assets',2005),
]:
    target = output / label
    script = original.replace(anchor, f"visit(obj({asset_file!r},{path_id}),-1)",1).replace(
        "'prefab':['sharedassets4.assets',226]", f"'prefab':[{asset_file!r},{path_id}]")
    sys.argv = [str(exporter),str(root/'ressources/GH_Data'),str(target)]
    namespace={'__name__':'__main__'}
    exec(compile(script,str(exporter),'exec'),namespace)
    provenance=json.loads((target/'provenance.json').read_text())
    provenance['exporter_sha256']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    (target/'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
