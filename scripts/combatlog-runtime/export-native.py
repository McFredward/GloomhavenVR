"""Read-only original compact combat-log hierarchy and typography export."""
import ast,hashlib,importlib.util,json,sys,xml.etree.ElementTree as ET
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.classes import PPtr
root,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('profile',root/'scripts/menu726-native-profile.py');p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
base=root/'ressources/GH_Data';gen=TypeTreeGenerator('2021.3.5f1');gen.load_local_dll_folder(str(base/'Managed'))
env=UnityPy.load(str(base/'level6'));env.typetree_generator=gen;objects={o.path_id:o for o in env.objects}
def script_kind(obj):
 ref=obj.assets_file.script_types[obj.serialized_type.script_type_index]
 return PPtr(m_FileID=ref.local_serialized_file_index,m_PathID=ref.local_identifier_in_file,assetsfile=obj.assets_file).read().m_ClassName
source=(root/'scripts/levelup-native-runtime/export_inputs.py').read_text();tree=ast.parse(source)
links={'m_Script','m_GameObject','material','spriteSheet','m_SourceFontFile','m_AtlasTextures','m_SourceFontFile_EditorRef','fallbackFontAssetTable','m_FallbackFontAssetTable'};fonts={}
for item in tree.body:
 if isinstance(item,ast.FunctionDef) and item.name in ('ptr','export_font','clean','confirmation_node','xml_node'):
  code=ast.get_source_segment(source,item).replace('component.parse_monobehaviour_head().m_Script.read().m_ClassName','script_kind(component)')
  if item.name=='confirmation_node':code=code.replace("'AspectRatioFitter','RectMask2D'","'AspectRatioFitter','RectMask2D','Mask'")
  exec(compile(code,'qualified-exporter:'+item.name,'exec'),globals())
handler=objects[5264];d=handler.read_typetree();target=ptr(d['m_GameObject'],handler).deref();assert target.path_id==1486
r=objects[4182].read_typetree();parent=objects[3193].read_typetree()
assert r['m_AnchorMin']=={'x':0.0,'y':0.0} and r['m_AnchorMax']=={'x':1.0,'y':1.0} and r['m_SizeDelta']=={'x':0.0,'y':0.0}
assert abs(parent['m_SizeDelta']['x']-569.4)<.01 and abs(parent['m_SizeDelta']['y']-291.1)<.01
layout={'target':r,'parent':parent,'native_id':p.object_id(handler),'desktop_reference':[1920,1080]}
for name in ['combatLogText','combatLogTextHighlighted']:
 reader=ptr(d[name],handler).deref();data=reader.read_typetree();go=ptr(data['m_GameObject'],reader).deref()
 node=confirmation_node(go,'combatlog-'+name);ET.ElementTree(xml_node(node)).write(out/(name+'.xml'),encoding='unicode',xml_declaration=True)
 text=ptr(data['textLog'],reader).deref();layout[name+'Typography']=text.read_typetree()
node=confirmation_node(target,'combatlog-window');ET.ElementTree(xml_node(node)).write(out/'window.xml',encoding='unicode',xml_declaration=True)
(out/'layout.json').write_text(json.dumps(layout,indent=2)+'\n')
(out/'native-provenance.json').write_text(json.dumps({'scene':p.object_id(target),'scene_sha256':hashlib.sha256((base/'level6').read_bytes()).hexdigest(),
 'fixed_parent':p.object_id(objects[3193]),'target_transform':p.object_id(objects[4182]),'font_records':list(fonts.values()),
 'limits':'Original compact parent and stretched child, full static viewport hierarchy, original row template, static TMP font/atlas/material authoring. Gameplay controller initialization, log population and native lean-tween clocks are declared ports. Native shaders render through their qualified GL font source; native Windows D3D11 programs are not executed.'},indent=2)+'\n')
(out/'pins.json').write_text(json.dumps({x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in out.iterdir() if x.is_file()},indent=2)+'\n')
print('Original compact log:',parent['m_SizeDelta'],'fonts',len(fonts))
