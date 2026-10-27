#!/usr/bin/env python3
"""Read exact native character-window tooltip identities; never export assets."""
from pathlib import Path
import argparse,hashlib,importlib.util,json,re,struct,sys
sys.dont_write_bytecode=True
import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
ROOT=Path(__file__).resolve().parents[2]
OWNERS={'UIAdventurePartyAssemblyCharacterDisplay','UICharacterSkinSelector','UICampaignAssemblyCharacterInformation','UICharacterCreatorWindow','UICharacterCreatorClassRoster','UICharacterCreatorClassStep','UICharacterCreatorNameStep','UICharacterCreatorPersonalQuestStep','UICharacterCreatorConfirmationBox','UIPersonalQuest','UIPersonalQuestProgress','UICharacterPersonalQuest'}
FIELDS={'UICharacterSkinSelector':['skinTooltip'],'UIAdventurePartyAssemblyCharacterDisplay':['skinSelector','questTooltip','charTooltip','information','window'],'UICampaignAssemblyCharacterInformation':['descriptionTooltip','personalQuestTooltip'],'UICharacterCreatorWindow':['classStep','nameStep','personalQuestStep'],'UICharacterCreatorClassRoster':['perksTooltip','information'],'UICharacterPersonalQuest':['personalQuest','progressiveObjective']}
def sha(p):
 with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--game-data',type=Path,default=ROOT/'ressources/GH_Data');p.add_argument('--output',type=Path,required=True);a=p.parse_args();data=a.game_data
 profile_path=ROOT/'scripts/menu726-native-profile.py';spec=importlib.util.spec_from_file_location('profile',profile_path);profile=importlib.util.module_from_spec(spec);spec.loader.exec_module(profile)
 generator=TypeTreeGenerator('2021.3.5f1');generator.load_local_dll_folder(str(data/'Managed'))
 bases={}
 for file in (ROOT/'decompiled/GH.Runtime').rglob('*.cs'):
  declaration=re.search(r'\bclass\s+(\w+)[^{]*?:\s*([\w.]+)',file.read_text(errors='replace')[:5000])
  if declaration:bases[declaration[1]]={'base':declaration[2].split('.')[-1],'source':str(file),'sha256':sha(file)}
 def tooltip_kind(name):
  seen=set()
  while name not in seen:
   if name in ['UITooltipTarget','UILocalTooltip','TooltipUI']:return True
   seen.add(name)
   if name not in bases:return False
   name=bases[name]['base']
  return False
 result={'native_tooltip_base_contract':{n:v for n,v in bases.items()if tooltip_kind(n)},'unity':'2021.3.5f1','native_runtime_sha256':sha(data/'Managed/GH.Runtime.dll'),'sources':{},'owners':[],'tooltips':[]}
 for source in [data/'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle',*[data/('level'+str(i))for i in [2,3,4,5,9,10]]]:
  result['sources'][str(source.relative_to(data))]=sha(source);environment=UnityPy.load(str(source));environment.typetree_generator=generator;trees={};classes={}
  def tree(r):
   key=(r.assets_file.name,r.path_id)
   if key not in trees:trees[key]=profile.authored_tree(r)if r.type.name=='MonoBehaviour'else r.read_typetree()
   return trees[key]
  def pointer(v,owner):
   if not isinstance(v,dict)or not v.get('m_PathID'):return None
   return PPtr(m_FileID=v['m_FileID'],m_PathID=v['m_PathID'],assetsfile=owner.assets_file).deref()
  def kind(r):
   key=(r.assets_file.name,r.path_id)
   if key not in classes:
    if r.type.name!='MonoBehaviour':classes[key]=r.type.name
    else:
     f,i=struct.unpack_from('<iq',r.get_raw_data(),16);classes[key]=PPtr(m_FileID=f,m_PathID=i,assetsfile=r.assets_file).read().m_ClassName
   return classes[key]
  def components(go):return [pointer(v['component'],go)for v in tree(go)['m_Component']]
  def identity(r):
   if r is None:return None
   d=tree(r);go=r if r.type.name=='GameObject'else pointer(d.get('m_GameObject'),r);parts=components(go)if go else[]
   transform=next((c for c in parts if c.type.name in ['RectTransform','Transform']),None)
   return {'file':r.assets_file.name,'path_id':r.path_id,'class':kind(r),'game_object_path_id':go.path_id if go else None,'name':tree(go).get('m_Name')if go else d.get('m_Name'),'components':[kind(c)for c in parts if c],'rect':{k:v for k,v in tree(transform).items()if k in ['m_LocalPosition','m_LocalScale','m_SizeDelta','m_AnchoredPosition','m_Pivot','m_AnchorMin','m_AnchorMax']}if transform else None}
  def route(r):
   go=pointer(tree(r).get('m_GameObject'),r);rows=[]
   for i in range(40):
    if go is None:break
    rows.append(identity(go));transform=next((c for c in components(go)if c.type.name in ['RectTransform','Transform']),None)
    if transform is None:break
    parent=pointer(tree(transform).get('m_Father'),transform);go=pointer(tree(parent).get('m_GameObject'),parent)if parent else None
   return rows
  for r in environment.objects:
   if r.type.name!='MonoBehaviour':continue
   name=kind(r)
   if name in OWNERS:
    d=tree(r);fields={}
    for key in FIELDS.get(name,[]):
     value=d.get(key);fields[key]=[identity(pointer(v,r))for v in value]if isinstance(value,list)else identity(pointer(value,r))
    result['owners'].append({'source':str(source.relative_to(data)),'owner':identity(r),'fields':fields,'route':route(r),'object_sha256':hashlib.sha256(r.get_raw_data()).hexdigest()})
   if tooltip_kind(name):
    path=route(r)
    if any(any(c in OWNERS for c in row['components'])for row in path):
     d=tree(r);result['tooltips'].append({'source':str(source.relative_to(data)),'tooltip':identity(r),'route':path,'serialized':{k:v for k,v in d.items()if k not in ['m_GameObject','m_Script','m_EditorClassIdentifier','m_Name']},'object_sha256':hashlib.sha256(r.get_raw_data()).hexdigest(),'references':{k:identity(pointer(v,r))for k,v in d.items()if isinstance(v,dict)and 'm_PathID'in v}})
 skin=[r for r in result['owners']if r['owner']['class']=='UICharacterSkinSelector']
 assert len(skin)==6 and all(r['fields']['skinTooltip']['class']=='UITextTooltipTarget'for r in skin)
 creator=[r for r in result['tooltips']if any('UICharacterCreatorWindow'in n['components']for n in r['route'])]
 assert len(creator)==2 and all(r['tooltip']['class']=='UITextTooltipTarget'and r['serialized']['tooltipText']=='GUI_STARTING_PERK_POINTS_TOOLTIP'for r in creator)
 quests=[r for r in result['tooltips']if r['tooltip']['class']=='UILocalTooltip']
 assert len(quests)==2 and all({'UIPersonalQuestState','UIPersonalQuest'}<=set(r['references']['target']['components'])for r in quests)
 result['verified_roles']={'native_skin_hints':len(skin),'all_native_creation_emitters':len(creator),'native_personal_quest_explanations':len(quests)}
 a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({'output':str(a.output),'owners':len(result['owners']),'tooltips':len(result['tooltips'])}))
if __name__=='__main__':main()
