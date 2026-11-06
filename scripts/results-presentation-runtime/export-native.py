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
# Preserve the ORIGINAL custom image material contracts too. The earlier fixture
# exported only shader names and then assigned UI/Default at import, so it never
# asked the real resolver for dormant result/ready effect textures such as3DNoise.
material_records={};material_envs={};shader_records={};texture_records={}
def original_reader(identity):
 name=identity['serialized_file']
 if name not in material_envs:
  loaded=UnityPy.load(str(base/name));material_envs[name]=(loaded,{o.path_id:o for o in loaded.objects})
 return material_envs[name][1][identity['path_id']]
def export_texture(reader):
 key='native-texture-'+reader.assets_file.name.replace('.','_')+'-'+str(reader.path_id)
 if key in texture_records:return key
 tree=reader.read_typetree();texture=reader.read();image=texture.image
 raw=texture.get_image_data();raw_hash=hashlib.sha256(raw).hexdigest()
 if tree['m_TextureFormat'] in (3,4):(out/(key+'.raw')).write_bytes(raw)
 image.save(out/(key+'.png'))
 texture_records[key]={'name':tree['m_Name'],'width':tree['m_Width'],'height':tree['m_Height'],
  'format':tree['m_TextureFormat'],'mips':tree['m_MipCount'],'original':p.object_id(reader),
  'native_pixel_stream_sha256':raw_hash,'native_pixel_stream_bytes':len(raw),
  'pixels_sha256':hashlib.sha256(image.tobytes()).hexdigest()}
 return key
def export_material(identity):
 reader=original_reader(identity);key='native-material-'+reader.assets_file.name.replace('.','_')+'-'+str(reader.path_id)
 if key in material_records:return key
 data=reader.read_typetree();shader=ptr(data['m_Shader'],reader).deref();shader_tree=shader.read_typetree();parsed=shader_tree['m_ParsedForm'];shader_key='native-shader-'+shader.assets_file.name.replace('.','_')+'-'+str(shader.path_id)
 shader_records[shader_key]={'name':parsed['m_Name'],'original':p.object_id(shader),'properties':parsed['m_PropInfo']['m_Props'],
  'native_pass_state':parsed['m_SubShaders'][0]['m_Passes'][0]['m_State']}
 saved=data['m_SavedProperties'];textures=[]
 for name,env in saved.get('m_TexEnvs',[]):
  ref=env['m_Texture'];texture=export_texture(ptr(ref,reader).deref()) if ref['m_PathID'] else ''
  textures.append({'name':name,'texture':texture,'scale':[env['m_Scale']['x'],env['m_Scale']['y']],
   'offset':[env['m_Offset']['x'],env['m_Offset']['y']]})
 material_records[key]={'name':data['m_Name'],'shader':shader_key,'original':identity,
  'queue':data['m_CustomRenderQueue'],'keywords':data.get('m_ValidKeywords',[]),'textures':textures,
  'floats':[{'name':k,'value':v} for k,v in saved.get('m_Floats',[])],
  'colors':[{'name':k,'value':[v['r'],v['g'],v['b'],v['a']]} for k,v in saved.get('m_Colors',[])]}
 return key
def native_materials(data):
 for component in data['components']:
  if component.get('kind')=='Image' and component.get('material'):
   component['nativeMaterial']=export_material(component['material'])
 for child in data.get('children',[]):native_materials(child)
def material_xml(node):
 result=xml_node(node)
 for original,component in zip(node['components'],result.findall('component')):
  if original.get('nativeMaterial'):component.set('nativeMaterial',original['nativeMaterial'])
 for child,element in zip(node.get('children',[]),result.findall('node')):
  result.remove(element);result.append(material_xml(child))
 return result
for name,gid in [('results',957),('ready',807),('battle',roles['battleGoalResultSlot']),('achievement',roles['partyAchievementPrefab'])]:
 reader=roleReaders['battleGoalResultSlot'] if name=='battle' else roleReaders['partyAchievementPrefab'] if name=='achievement' else objects[gid]
 data=confirmation_node(reader,'narrative-results-'+name)
 native_materials(data)
 ET.ElementTree(material_xml(data)).write(out/(name+'.xml'),encoding='unicode',xml_declaration=True)
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

(out/'native-materials.json').write_text(json.dumps({'materials':material_records,'shaders':shader_records,'textures':texture_records,'boundary':'Exact original native material/property/texture authoring; GL fragment programs are declared fixture ports, not original Windows shader acceptance.'},indent=2)+'\n')
