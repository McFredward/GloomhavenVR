"""Export original results/ready render authoring without executing game controllers."""
import ast,hashlib,importlib.util,json,sys,xml.etree.ElementTree as ET
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.classes import PPtr
root,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('p',root/'scripts/menu726-native-profile.py');p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
base=root/'ressources/GH_Data';gen=TypeTreeGenerator('2021.3.5f1');gen.load_local_dll_folder(str(base/'Managed'))
env=UnityPy.load(str(base/'level6'));env.typetree_generator=gen;objects={o.path_id:o for o in env.objects}
globalobjects={o.path_id:o for o in UnityPy.load(str(base/'globalgamemanagers.assets')).objects}
def script_kind(obj):
 ref=obj.assets_file.script_types[obj.serialized_type.script_type_index]
 return PPtr(m_FileID=ref.local_serialized_file_index,m_PathID=ref.local_identifier_in_file,assetsfile=obj.assets_file).read().m_ClassName
# Reuse the qualified native typography/sprite/XML exporter definitions only;
# importing its top-level campaign/reward exports would do unrelated work.
source=(root/'scripts/levelup-native-runtime/export_inputs.py').read_text();tree=ast.parse(source)
links={'m_Script','m_GameObject','material','spriteSheet','m_SourceFontFile','m_AtlasTextures','m_SourceFontFile_EditorRef','fallbackFontAssetTable','m_FallbackFontAssetTable'};fonts={}
for item in tree.body:
 if isinstance(item,ast.FunctionDef) and item.name in ('ptr','export_font','clean','confirmation_node','xml_node'):
  code=ast.get_source_segment(source,item)
  code=code.replace("component.parse_monobehaviour_head().m_Script.read().m_ClassName","script_kind(component)")
  exec(compile(code,'qualified-exporter:'+item.name,'exec'),globals())
manager=objects[5291].read_typetree();roles={'window':957};roleReaders={}
for key in ('header','resultsPanel','statsPanel','buttonsContainer','battleGoalResultSlot','partyAchievementPrefab'):
 raw=manager[key];obj=ptr(raw,objects[5291]).deref();data=obj.read_typetree()
 go=obj if obj.type.name=='GameObject' else ptr(data['m_GameObject'],obj).deref()
 roles[key]=go.path_id;roleReaders[key]=go
roles['ready']=807
for name,gid in [('results',957),('ready',807),('battle',roles['battleGoalResultSlot']),('achievement',roles['partyAchievementPrefab'])]:
 reader=roleReaders['battleGoalResultSlot'] if name=='battle' else roleReaders['partyAchievementPrefab'] if name=='achievement' else objects[gid]
 data=confirmation_node(reader,'narrative-results-'+name)
 ET.ElementTree(xml_node(data)).write(out/(name+'.xml'),encoding='unicode',xml_declaration=True)
 (out/(name+'.json')).write_text(json.dumps(data,indent=2))
 # Independent serialized IDs map the native manager's own fields to exact original paths.
 def paths(node,path=''):
  yield node['original']['path_id'],path
  occurrences={}
  for child in node.get('children',[]):
   i=occurrences.get(child['name'],0);occurrences[child['name']]=i+1
   yield from paths(child,path+'/'+child['name']+':'+str(i))
 roles[name+'Paths']=dict(paths(data))
(out/'roles.json').write_text(json.dumps(roles,indent=2))
(out/'provenance.json').write_text(json.dumps({'scene_sha256':hashlib.sha256((base/'level6').read_bytes()).hexdigest(),'limits':'Original static hierarchy, native sprite/font authoring and manager field identities. Original gameplay controllers, scenario-populated values, native animation clocks and networking are explicit fixture ports.'},indent=2))
