#!/usr/bin/env python3
"""Export the actual desktop ItemCard hierarchy and native Leather_Armor artwork.

The game-created item model/layout and native shader execution are declared ports;
serialized original hierarchy, images and material descriptors retain provenance.
"""
import json
import sys
from pathlib import Path
root,out=map(Path,sys.argv[1:])
base=root/'ressources/GH_Data'
original=(root/'scripts/town-first-picture632-runtime/export-native.py').read_text()
original=original.replace('environments, lookup = {}, {}', '''environments, lookup = {}, {}
asset_paths={}
for filename in ['misc_gui_assets_all.bundle','itemconfigs_backgrounds_assets_leather_armor.bundle','itemconfigs_backgrounds_assets_blackcard.bundle']:
    source_path=base/'StreamingAssets/aa/StandaloneWindows64'/filename
    bundle=UnityPy.load(str(source_path));bundle.typetree_generator=generator
    for native in bundle.objects:
        name=native.assets_file.name
        environments[name]=bundle
        lookup.setdefault(name,{})[native.path_id]=native
        asset_paths[name]=source_path
''')
original=original.replace("(base/name).read_bytes()", "asset_paths.get(name,base/name).read_bytes()")
original=original.replace("rect=next(x for x in components if x.type.name=='RectTransform');rt=rect.read_typetree()", "rect=next((x for x in components if x is not None and x.type.name=='RectTransform'),None)\n    if rect is None:return # Native particle transform is not an observer Graphic.\n    rt=rect.read_typetree()")
original=original.replace("visit(obj('sharedassets4.assets',226),-1)", """visit(obj('CAB-5b84b5b74775062cd7460715663ea0e6',1064783067020900303),-1)
# The native ImageAddressableLoader assigns precisely this original public art.
# Loading is a source input; the proof never creates a generic card front.
art=next(o for objects in lookup.values()for o in objects.values()if o.type.name=='Sprite'and o.read().m_Name=='Leather_Armor')
ident=art.assets_file.name+'-'+str(art.path_id)+'.png';art.read().image.save(out/ident)
nodes[0]['graphics'][0]['image']=ident
""")
original=original.replace('Exported original enhancement row:', 'Exported original merchant ItemCard:')
original=original.replace("'prefab':['sharedassets4.assets',226]", "'prefab':['misc_gui_assets_all.bundle',1064783067020900303],'art':['itemconfigs_backgrounds_assets_leather_armor.bundle',-5575648043477861759]")
sys.argv=['native-merchant-export',str(base),str(out/'item-card')]
scope={'__name__':'__main__'}
exec(compile(original,'native-merchant-export','exec'),scope)

provenance_path=out/'item-card/provenance.json'
provenance=json.loads(provenance_path.read_text())
provenance['execution_ports']=['Native dynamic item model/action layout is an input; fresh ItemCardUI.UpdateState is audited but not executed.', 'GUI/AbilityCard_Shd property table is retained; actual fragment execution uses Unity UI/Default in the editor fixture.', 'Original serialized TMP style/layout uses the editor TMP font atlas.']
provenance_path.write_text(json.dumps(provenance,indent=2)+'\n')

# Exact ItemCardEffects arrays locate renderer ports by native component identity.
components=[scope['resolve'](scope['obj']('CAB-5b84b5b74775062cd7460715663ea0e6',1064783067020900303),x['component'])for x in scope['obj']('CAB-5b84b5b74775062cd7460715663ea0e6',1064783067020900303).read_typetree()['m_Component']]
effects=next(c for c in components if c.type.name=='MonoBehaviour' and 'fgFx' in c.read_typetree() and 'imgComp' in c.read_typetree())
tree=effects.read_typetree()
def renderer_index(pointer):
    component=scope['resolve'](effects,pointer)
    if component is None:return -1
    game_object=scope['resolve'](component,component.read_typetree()['m_GameObject'])
    return next(i for i,n in enumerate(scope['nodes'])if n['pathID']==game_object.path_id)
refs={'images':[renderer_index(p)for p in tree['imgComp']],'texts':[renderer_index(p)for p in tree['txtComp']],'foreground':renderer_index(tree['fgFx'])}
(out/'item-card/native-effects.json').write_text(json.dumps(refs,indent=2)+'\n')
import struct
with (out/'item-card/native-effects.bin').open('wb')as b:
    for name in ['images','texts']:
        b.write(struct.pack('<H',len(refs[name])))
        for i in refs[name]:b.write(struct.pack('<h',i))
    b.write(struct.pack('<h',refs['foreground']))
provenance['fresh_effects']={'pathID':effects.path_id,'references':refs}
provenance['execution_ports'].append('Exact native ItemCardEffects.Initialize/RestoreCard execute; fgFx zero-FX fragment output is explicitly transparent. Other animated FX shaders/controllers remain ports.')
provenance_path.write_text(json.dumps(provenance,indent=2)+'\n')
