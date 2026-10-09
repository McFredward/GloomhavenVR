#!/usr/bin/env python3
"""Export the exact current original desktop ability-card prefab, without game callbacks."""
import sys
from pathlib import Path
root,out=map(Path,sys.argv[1:])
base=root/'ressources/GH_Data'
original=(root/'scripts/town-first-picture632-runtime/export-native.py').read_text()
original=original.replace("environments, lookup = {}, {}", """environments, lookup = {}, {}
bundle=UnityPy.load(str(base/'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle'))
bundle.typetree_generator=generator
for native in bundle.objects:
    name=native.assets_file.name
    environments[name]=bundle
    lookup.setdefault(name,{})[native.path_id]=native
""")
original=original.replace("(base/name).read_bytes()", "(base/'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle' if name.startswith('CAB-') else base/name).read_bytes()")
original=original.replace("visit(obj('sharedassets4.assets',226),-1)", "visit(obj('CAB-5b84b5b74775062cd7460715663ea0e6',-7204637168609111676),-1)",1)
original=original.replace("'prefab':['sharedassets4.assets',226]", "'prefab':['misc_gui_assets_all.bundle',-7204637168609111676]")
for label,path_id in [('ability-card',-7204637168609111676),('text-container',-1266472648965326324),('preview-text',-6477758006666907710),('enhancement-container',-410446020127428728),('enhancement',8526426084191958360),('xp-container',5332165634470536154),('duration-res',-2846590613678398817),('enhanced-area',-4841324548927422935)]:
    script=original.replace('-7204637168609111676',str(path_id))
    sys.argv=['native-export',str(base),str(out/label)]
    exec(compile(script,'native-ability-export','exec'),{'__name__':'__main__'})
