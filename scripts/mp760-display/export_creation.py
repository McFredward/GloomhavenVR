"""Export native creation layout/art read-only; gameplay controllers are never copied."""
import hashlib,json,re,sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
root,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
gen=TypeTreeGenerator('2021.3.5f1');gen.load_local_dll_folder(str(root/'ressources/GH_Data/Managed'))
env=UnityPy.load(str(root/'ressources/GH_Data/level9'));env.typetree_generator=gen
ids={o.path_id:o for o in env.objects}
def vec(v,n='xy'):return {k:float(getattr(v,k)) for k in n}
def kind(o):return o.parse_monobehaviour_head().m_Script.read().m_ClassName if o.type.name=='MonoBehaviour' else o.type.name
def components(go):return [(kind(s.component.deref()),s.component.deref()) for s in go.m_Component]
def record(t,children):
 go=t.m_GameObject.read();r={'nodeId':t.object_reader.path_id,'name':go.m_Name,'active':bool(go.m_IsActive),'min':vec(t.m_AnchorMin),'max':vec(t.m_AnchorMax),'size':vec(t.m_SizeDelta),'pivot':vec(t.m_Pivot),'position':vec(t.m_AnchoredPosition),'scale':vec(t.m_LocalScale,'xyz'),'rotation':vec(t.m_LocalRotation,'xyzw'),'children':children}
 for k,o in components(go):
  if k=='Image':
   d=o.read_typetree();r['image']=True;r['color']=d['m_Color']
   sprite=o.read().m_Sprite
   if sprite.path_id:
    s=sprite.read();name='sprite-'+str(o.path_id)+'.png';s.image.save(out/name);r.update(sprite=name,border=vec(s.m_Border,'xyzw'),spritePivot=vec(s.m_Pivot),ppu=s.m_PixelsToUnits)
  if k=='TextMeshProUGUI':
   d=o.read_typetree();r.update(text=True,fontSize=d['m_fontSize'],value=re.sub('<[^>]*>','',d.get('m_text','')) or 'Choose a personal quest')
 return r
def subtree(t):return record(t,[subtree(p.read()) for p in t.m_Children])
# Preserve every native stretch ancestor. Retain only the two choice subtrees;
# page/controller lifecycle and dynamic quest identities are explicit inputs.
quest=ids[8523].read();node=record(quest,[subtree(ids[n].read()) for n in [9355,11201]])
while quest.m_Father.path_id:
 quest=quest.m_Father.read()
 if quest.m_GameObject.read().m_Name=='Campaign Canvas':break
 node=record(quest,[node])
assert node['name']=='New Party display'
rows=[]
def flatten(node,parent=0):
 children=node.pop('children');node['parentId']=parent;rows.append(node)
 for child in children:flatten(child,node['nodeId'])
flatten(node)
(out/'creation.json').write_text(json.dumps({'records':[json.dumps(row) for row in rows],'rootId':node['nodeId']},indent=2)+'\n')
(out/'provenance.json').write_text(json.dumps({'source':'level9','nativeQuestIds':[15364,18298],'nativeQuestRectIds':[9355,11201],'nativeDesign':[1920,1080],'artifacts':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()}},indent=2)+'\n')
