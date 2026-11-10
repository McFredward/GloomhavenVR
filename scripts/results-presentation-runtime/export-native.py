"""Export original results/ready render authoring without executing game controllers."""
import ast,hashlib,importlib.util,json,re,sys,xml.etree.ElementTree as ET
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
material_records={};material_envs={};shader_records={};texture_records={};root_texture_shape=None
def original_reader(identity):
 name=identity['serialized_file']
 if name not in material_envs:
  loaded=UnityPy.load(str(base/name));material_envs[name]=(loaded,{o.path_id:o for o in loaded.objects})
 return material_envs[name][1][identity['path_id']]
def export_texture(reader):
 key='native-texture-'+reader.assets_file.name.replace('.','_')+'-'+str(reader.path_id)
 if key in texture_records:return key
 tree=reader.read_typetree();texture=reader.read()
 deferred=tree['m_Width']<=0 or tree['m_Height']<=0
 raw=b'' if deferred else texture.get_image_data();raw_hash=hashlib.sha256(raw).hexdigest()
 # Preserve every native material dependency's real format and complete mip
 # stream. Decoding selected formats to RGBA32 hid actual registry identities.
 if not deferred:(out/(key+'.raw')).write_bytes(raw)
 texture_records[key]={'name':tree['m_Name'],'width':tree['m_Width'],'height':tree['m_Height'],
  'format':tree['m_TextureFormat'],'mips':tree['m_MipCount'],'original':p.object_id(reader),
  'native_pixel_stream_sha256':raw_hash,'native_pixel_stream_bytes':len(raw),
  'deferred':deferred}
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
 global root_texture_shape
 for component in data['components']:
  if component.get('kind')=='Image' and component.get('material'):
   component['nativeMaterial']=export_material(component['material'])
  if component.get('kind')=='Image' and component.get('spriteOriginal') and data['original']['path_id']==roles['window']:
   # The actual Results root's sprite has a conflicting same-descriptor sibling.
   # Preserve its true full native texture; a cropped RGBA upload under an export
   # filename hid this provenance edge in the earlier fixture.
   reader=original_reader(component['spriteOriginal']);sprite=reader.read_typetree()
   texture=ptr(sprite['m_RD']['texture'],reader).deref();td=texture.read_typetree();rect=sprite['m_Rect']
   assert rect['x']==rect['y']==0 and rect['width']==td['m_Width'] and rect['height']==td['m_Height']
   component['nativeSpriteTexture']=export_texture(texture)
   root_texture_shape=tuple(td[k] for k in ('m_Name','m_Width','m_Height','m_TextureFormat','m_MipCount'))
 for child in data.get('children',[]):native_materials(child)
def material_xml(node):
 result=xml_node(node)
 for original,component in zip(node['components'],result.findall('component')):
  if original.get('nativeMaterial'):component.set('nativeMaterial',original['nativeMaterial'])
  if original.get('nativeSpriteTexture'):component.set('nativeSpriteTexture',original['nativeSpriteTexture'])
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

# The four serialized roots are not the runtime material closure. Native button
# state/effect controllers install other original materials after initialization.
# Enumerate every original material's dependency table independently of what the
# previous failing screenshot happened to use. The direct inventory and source
# file hashes make omissions auditable instead of blessing a hand-selected count.
inventory=[];files=[]
for file in sorted(path for path in base.iterdir() if path.suffix=='.assets' or re.fullmatch(r'level\d+',path.name)):
 loaded=UnityPy.load(str(file));loaded.typetree_generator=gen
 readers={o.path_id:o for o in loaded.objects};material_envs[file.name]=(loaded,readers)
 files.append({'file':file.name,'bytes':file.stat().st_size,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
 for reader in readers.values():
  if reader.type.name=='Texture2D' and root_texture_shape is not None:
   td=reader.read_typetree()
   if tuple(td[k] for k in ('m_Name','m_Width','m_Height','m_TextureFormat','m_MipCount'))==root_texture_shape:
    export_texture(reader)
  if reader.type.name!='Material':continue
  identity=p.object_id(reader);data=reader.read_typetree();saved=data['m_SavedProperties'];dependencies=[]
  for name,tex in saved.get('m_TexEnvs',[]):
   ref=tex['m_Texture']
   if ref['m_PathID']:
    original=ptr(ref,reader).deref()
    assert original.type.name=='Texture2D',(identity,name,original.type.name)
    dependencies.append({'property':name,'texture':export_texture(original)})
  shader=ptr(data['m_Shader'],reader).deref() if data['m_Shader']['m_PathID'] else None
  shader_name=shader.read_typetree()['m_ParsedForm']['m_Name'] if shader else ''
  # The whole original material/texture closure is inventoried, but only the
  # reported runtime GUI button family joins actual UI material/render tests.
  # Unrelated world shaders are not rendered through invented UI substitutes.
  reported=(data['m_Name'].startswith('GUI_') or shader_name.startswith('GUI/')) and any(
   texture_records[d['texture']]['name']=='noise_01' for d in dependencies)
  if reported:export_material(identity)
  inventory.append({'original':identity,'name':data['m_Name'],'shader':shader_name,
   'texture_dependencies':dependencies,'saved_properties':saved,'reported_runtime_gui_family':reported})
(out/'native-material-inventory.json').write_text(json.dumps({'files':files,'materials':inventory,
 'root_texture_variants':[key for key,td in texture_records.items() if tuple(td[k] for k in ('name','width','height','format','mips'))==root_texture_shape]},indent=2)+'\n')
(out/'native-materials.json').write_text(json.dumps({'materials':material_records,'shaders':shader_records,'textures':texture_records,'boundary':'Every original serialized material and its native texture/property dependencies, including runtime GUI button-state candidates; exact native formats/mips. GL fragment programs are declared fixture ports, not original Windows shader acceptance.'},indent=2)+'\n')
