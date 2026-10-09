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
sys.argv=['native-export',str(base),str(out/'ability-card')]
exec(compile(original,'native-ability-export','exec'),{'__name__':'__main__'})
