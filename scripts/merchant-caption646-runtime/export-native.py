#!/usr/bin/env python3
"""Read the game's serialized merchant-row hierarchy without executing controllers.

Exports original hierarchy/RectTransforms, uGUI colours/images, TMP serialized
appearance and actual shared material properties. The runtime fixture uses the
editor's TMP font to exercise Unity; this is not an exact original font-atlas
pixel proof. Source pathIDs and hashes make that limitation explicit.
"""
import hashlib, json, sys, struct
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base, out = map(Path, sys.argv[1:]); out.mkdir(parents=True, exist_ok=True)
generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base/'Managed'))
environments, lookup = {}, {}
def env(name):
    if name not in environments:
        e = UnityPy.load(str(base/name)); e.typetree_generator=generator
        environments[name]=e; lookup[name]={o.path_id:o for o in e.objects}
    return environments[name]
def obj(name, ident):
    env(name); return lookup[name][ident]
def resolve(source, ptr):
    if not ptr['m_PathID']: return None
    name=source.assets_file.name
    if ptr['m_FileID']:
        name=Path(source.assets_file.externals[ptr['m_FileID']-1].path).name
    return obj(name,ptr['m_PathID'])
def texture(source, ptr, sprite=False):
    asset=resolve(source,ptr)
    if asset is None:return ''
    ident=asset.assets_file.name+'-'+str(asset.path_id)+'.png'
    if not (out/ident).exists():asset.read().image.save(out/ident)
    return ident
materials={}
def material(source,ptr):
    asset=resolve(source,ptr)
    if asset is None:return ''
    key=asset.assets_file.name+':'+str(asset.path_id)
    if key not in materials:
        tree=asset.read_typetree(); saved=tree['m_SavedProperties']
        materials[key]={'key':key,'name':tree['m_Name'],
          'floats':[{'name':k,'value':v}for k,v in saved['m_Floats']],
          'colors':[{'name':k,'value':v}for k,v in saved['m_Colors']],
          'textures':[{'name':k,'image':texture(asset,v['m_Texture']),
             'offset':v['m_Offset'],'scale':v['m_Scale']}for k,v in saved['m_TexEnvs']]}
    return key
nodes=[]
def visit(game_object,parent):
    tree=game_object.read_typetree(); components=[resolve(game_object,x['component'])for x in tree['m_Component']]
    rect=next(x for x in components if x.type.name=='RectTransform');rt=rect.read_typetree()
    record={'name':tree['m_Name'],'pathID':game_object.path_id,'parent':parent,'active':tree['m_IsActive'],
      'position':rt['m_LocalPosition'],'rotation':rt['m_LocalRotation'],'scale':rt['m_LocalScale'],
      'anchorMin':rt['m_AnchorMin'],'anchorMax':rt['m_AnchorMax'],'anchored':rt['m_AnchoredPosition'],
      'size':rt['m_SizeDelta'],'pivot':rt['m_Pivot'],'graphics':[],'groups':[]}
    for component in components:
        if component.type.name=='CanvasGroup':
            c=component.read_typetree();record['groups'].append({'alpha':c['m_Alpha'],'ignoreParents':c['m_IgnoreParentGroups']})
        if component.type.name!='MonoBehaviour':continue
        c=component.read_typetree()
        if 'm_Color'not in c:continue
        graphic={'enabled':bool(c['m_Enabled']),'color':c['m_Color'],'material':material(component,c['m_Material']),
           'kind':'','image':'','text':'','fontSize':0,'alignment':0,'fontStyle':0,'spacing':0,'margin':{'x':0,'y':0,'z':0,'w':0}}
        if 'm_text'in c:
            graphic.update(kind='tmp',text=c['m_text'],fontSize=c['m_fontSize'],
                alignment=c['m_HorizontalAlignment']|c['m_VerticalAlignment'],fontStyle=c['m_fontStyle'],
                spacing=c['m_characterSpacing'],margin=c['m_margin'],material=material(component,c['m_sharedMaterial']))
        elif 'm_Sprite'in c:graphic.update(kind='image',image=texture(component,c['m_Sprite'],True))
        elif 'm_Texture'in c:graphic.update(kind='raw',image=texture(component,c['m_Texture']))
        else:continue
        record['graphics'].append(graphic)
    index=len(nodes);nodes.append(record)
    for ptr in rt['m_Children']:
        transform=resolve(rect,ptr); visit(resolve(transform,transform.read_typetree()['m_GameObject']),index)
visit(obj('sharedassets4.assets',330),-1)
payload={'nodes':nodes,'materials':list(materials.values())}
(out/'native-row.json').write_text(json.dumps(payload,indent=2)+'\n')
# Explicit portable fixture grammar; external Assembly.LoadFile types are not
# registered with Unity's editor JsonUtility serializer.
with (out/'native-row.bin').open('wb') as binary:
    def number(fmt,*values):binary.write(struct.pack('<'+fmt,*values))
    def string(value):
        data=value.encode('utf-8');number('H',len(data));binary.write(data)
    def vector(value,keys):number('f'*len(keys),*(value[x]for x in keys))
    number('H',len(materials))
    for value in materials.values():
        string(value['key']);string(value['name']);number('H',len(value['floats']))
        for f in value['floats']:string(f['name']);number('f',f['value'])
        number('H',len(value['colors']))
        for c in value['colors']:string(c['name']);vector(c['value'],'rgba')
    number('H',len(nodes))
    for value in nodes:
        string(value['name']);number('h?',value['parent'],value['active'])
        for name,keys in [('position','xyz'),('rotation','xyzw'),('scale','xyz'),('anchorMin','xy'),('anchorMax','xy'),('anchored','xy'),('size','xy'),('pivot','xy')]:vector(value[name],keys)
        number('B',len(value['groups']))
        for g in value['groups']:number('f?',g['alpha'],g['ignoreParents'])
        number('B',len(value['graphics']))
        for g in value['graphics']:
            number('?',g['enabled']);vector(g['color'],'rgba')
            for name in ['material','kind','image','text']:string(g[name])
            number('ffii',g['fontSize'],g['spacing'],g['alignment'],g['fontStyle']);vector(g['margin'],'xyzw')

# Campaign and Guildmaster both dispatch Merchant to the same UIGuildmasterHUD
# shopWindow. Follow its actual serialized inventory/prefab rather than inferring
# another row variant from the party inventory's unrelated slotPrefab.
hud=obj('level4',10746);hud_tree=hud.read_typetree()
shop=resolve(hud,hud_tree['shopWindow']);shop_tree=shop.read_typetree()
inventory=resolve(shop,shop_tree['itemInventory']);inventory_tree=inventory.read_typetree()
slot=resolve(inventory,inventory_tree['slotPrefab']);slot_tree=slot.read_typetree()
assert slot.assets_file.name=='sharedassets4.assets' and slot.path_id==1484
assert slot_tree['m_GameObject']['m_PathID']==330
field_paths={}
for field in ['itemName','itemNameWarning','itemIcon','itemAmount','itemPrice','goldIcon','reputation','itemPriceWarning','itemPricePanel']:
    target=resolve(slot,slot_tree[field]);target_tree=target.read_typetree()
    go=target_tree['m_GameObject']['m_PathID'] if 'm_GameObject' in target_tree else target.path_id
    index=next(i for i,n in enumerate(nodes) if n['pathID']==go);parts=[]
    while nodes[index]['parent']>=0:
        parts.insert(0,nodes[index]['name']);index=nodes[index]['parent']
    field_paths[field]='/'.join(parts)
(out/'provenance.json').write_text(json.dumps({'unitypy':UnityPy.__version__,'prefab':['sharedassets4.assets',330],
 'shared_mode_route':{'hud':['level4',hud.path_id],'shop':['level4',shop.path_id],
  'inventory':['level4',inventory.path_id],'slot':['sharedassets4.assets',slot.path_id]},'native_field_paths':field_paths,
 'nodes':len(nodes),'source_sha256':{name:hashlib.sha256((base/name).read_bytes()).hexdigest()for name in environments},
 'limitation':'Serialized original hierarchy, images, colours, layouts and shared material properties; equivalent editor TMP font atlas.'},indent=2)+'\n')
print('Exported original merchant row: '+str(len(nodes))+' nodes; '+str(len(materials))+' actual materials')
