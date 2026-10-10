#!/usr/bin/env python3
"""Export the original SummonContainer layout without running native controllers."""
import hashlib, json, struct, sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
root, output = map(Path, sys.argv[1:]); output.mkdir(parents=True, exist_ok=True)
base = root / 'ressources/GH_Data'
source = base / 'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle'
generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base/'Managed'))
env = UnityPy.load(str(source)); env.typetree_generator = generator
objects = {o.path_id:o for o in env.objects}
def own(pointer):
    assert pointer['m_FileID'] == 0, 'Original summon hierarchy must remain in its own bundle'
    return objects[pointer['m_PathID']]
def script(component, tree):
    if component.type.name != 'MonoBehaviour': return component.type.name
    return own(tree['m_Script']).read_typetree()['m_ClassName']
nodes=[]
def visit(go,parent):
    data=go.read_typetree(); components=[own(c['component'])for c in data['m_Component']]
    rect=next(c for c in components if c.type.name=='RectTransform'); rt=rect.read_typetree()
    record={'name':data['m_Name'],'go':go.path_id,'parent':parent,'active':bool(data['m_IsActive']),
        'rect':rt,'components':[]}; index=len(nodes);nodes.append(record)
    for component in components:
        if component.type.name not in ('MonoBehaviour',):continue
        tree=component.read_typetree();name=script(component,tree)
        record['components'].append({'type':name,'id':component.path_id,'serialized':tree})
    for pointer in rt['m_Children']:
        child=own(pointer);visit(own(child.read_typetree()['m_GameObject']),index)
visit(objects[-6366866636496485946],-1)
(output/'summon-serialized.json').write_text(json.dumps({'source':str(source.relative_to(base)),
    'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'nodes':nodes},indent=2)+'\n')
with (output/'summon.bin').open('wb') as f:
    def pack(fmt,*values):f.write(struct.pack('<'+fmt,*values))
    def string(text):data=text.encode();pack('H',len(data));f.write(data)
    def vec(value,keys):pack('f'*len(keys),*(value[k] for k in keys))
    types={'HorizontalLayoutGroup':1,'VerticalLayoutGroup':2,'ContentSizeFitter':3,'LayoutElement':4,
           'TextMeshProUGUI':5,'Image':6,'SummonContainer':7}
    pack('H',len(nodes))
    for node in nodes:
        string(node['name']);pack('h?',node['parent'],node['active']);rt=node['rect']
        for field,keys in [('m_LocalPosition','xyz'),('m_LocalRotation','xyzw'),('m_LocalScale','xyz'),
            ('m_AnchorMin','xy'),('m_AnchorMax','xy'),('m_AnchoredPosition','xy'),('m_SizeDelta','xy'),('m_Pivot','xy')]:vec(rt[field],keys)
        components=[c for c in node['components']if c['type']in types];pack('B',len(components))
        for c in components:
            t=c['serialized'];kind=types[c['type']];pack('B?',kind,bool(t['m_Enabled']))
            if kind in (1,2):
                p=t['m_Padding'];pack('iiiiif',p['m_Left'],p['m_Right'],p['m_Top'],p['m_Bottom'],t['m_ChildAlignment'],t['m_Spacing'])
                pack('??????',*(bool(t[k])for k in ['m_ChildForceExpandWidth','m_ChildForceExpandHeight','m_ChildControlWidth','m_ChildControlHeight','m_ChildScaleWidth','m_ChildScaleHeight']))
            elif kind==3:pack('ii',t['m_HorizontalFit'],t['m_VerticalFit'])
            elif kind==4:
                pack('ffffffi?',*(t[k]for k in ['m_MinWidth','m_MinHeight','m_PreferredWidth','m_PreferredHeight','m_FlexibleWidth','m_FlexibleHeight','m_LayoutPriority','m_IgnoreLayout']))
            elif kind==5:
                string(t['m_text']);pack('fii',t['m_fontSize'],t['m_fontStyle'],t['m_HorizontalAlignment']|t['m_VerticalAlignment']);vec(t['m_Color'],'rgba');vec(t['m_margin'],'xyzw')
            elif kind==6:vec(t['m_Color'],'rgba')
            elif kind==7:
                for field in ['SummonNameText','SummonLT','SummonLB','SummonMT','SummonMB','SummonR']:
                    item=own(t[field]);target=item.path_id if item.type.name=='GameObject'else item.read_typetree()['m_GameObject']['m_PathID']
                    pack('h',next(i for i,n in enumerate(nodes)if n['go']==target))
(output/'provenance.json').write_text(json.dumps({'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
    'dll_sha256':hashlib.sha256((base/'Managed/GH.Runtime.dll').read_bytes()).hexdigest(),
    'root_pathID':-6366866636496485946,'unitypy':UnityPy.__version__,
    'boundary':'Original serialized RectTransforms and native Unity layout component properties. Editor TMP font atlas; native gameplay controllers not executed.'},indent=2)+'\n')
print('Exported original summon: '+str(len(nodes))+' nodes')
