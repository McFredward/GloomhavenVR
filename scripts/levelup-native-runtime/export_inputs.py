"""Read-only native widget export; complete authored IDs, sprite and font provenance."""
import sys
from pathlib import Path
import UnityPy,importlib.util,json,hashlib
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.classes import PPtr
root,out=map(Path,sys.argv[1:]);out.mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('p',root/'scripts/menu726-native-profile.py');p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
gen=TypeTreeGenerator('2021.3.5f1');gen.load_local_dll_folder(str(root/'ressources/GH_Data/Managed'))
env=UnityPy.load(str(root/'ressources/GH_Data/level9'));env.typetree_generator=gen
byid={x.path_id:x for x in env.objects};button=byid[18225];d=p.authored_tree(button)
def ptr(raw,obj):return PPtr(m_FileID=raw['m_FileID'],m_PathID=raw['m_PathID'],assetsfile=obj.assets_file)
def node(reader):
 go=reader.read();components=[];transform=None
 for slot in go.m_Component:
  target=slot.component.deref();kind=target.type.name
  if kind=='MonoBehaviour':kind=target.parse_monobehaviour_head().m_Script.read().m_ClassName
  components.append({'kind':kind,'original':p.object_id(target)})
  if kind=='RectTransform':transform=target.read()
 result={'name':go.m_Name,'active':bool(go.m_IsActive),'original':p.object_id(reader),'components':components}
 if transform:
  result['rect']={key:p.vector(getattr(transform,'m_'+key),'xy' if key in ('AnchorMin','AnchorMax','Pivot','SizeDelta','AnchoredPosition') else 'xyzw' if key=='LocalRotation' else 'xyz') for key in ('AnchorMin','AnchorMax','Pivot','SizeDelta','AnchoredPosition','LocalPosition','LocalRotation','LocalScale')}
  result['children']=[node(child.read().m_GameObject.deref()) for child in transform.m_Children]
 return result
widget=node(ptr(d['m_GameObject'],button).deref())
assert widget['rect']['SizeDelta']==[307.,65.], 'Native Continue authored frame changed'
assert widget['children'][0]['rect']['SizeDelta']==[-15.999998092651367,-14.], 'Native label layout changed'
assert widget['children'][1]['active'] is False, 'Native controller hint now needs a visible input fixture'
assert p.authored_tree(byid[16283])['continueButton']=={'m_FileID':0,'m_PathID':18225}, 'Native reward template owner changed'
assert d['m_Transition']==2, 'Native Continue no longer uses SpriteSwap'
(out/'native-widget-tree.json').write_text(json.dumps(widget,indent=2)+'\n')
image=byid[17670];image_data=p.authored_tree(image)
records=[]
for name,raw,obj in [('normal',image_data['m_Sprite'],image)]+[(key,value,button) for key,value in d['m_SpriteState'].items()]:
 sprite=ptr(raw,obj).read();sprite.image.save(out/(name+'.png'))
 records.append({'name':name,'sprite':p.object_id(sprite.object_reader),'border':p.vector(sprite.m_Border,'xyzw'),'pivot':p.vector(sprite.m_Pivot,'xy'),'ppu':sprite.m_PixelsToUnits,'size':[sprite.m_Rect.width,sprite.m_Rect.height]})
text=ptr(d['buttonText'],button).deref();td=p.authored_tree(text)

font=ptr(td['m_fontAsset'],text).deref(); fd=p.authored_tree(font)
assert td['m_fontSize']==30 and td['m_enableAutoSizing']==1 and td['m_fontSizeMin']==18 and td['m_fontSizeMax']==30, 'Native label typography changed'
material=ptr(td['m_sharedMaterial'],text).deref();md=p.authored_tree(material);saved=md['m_SavedProperties']
shader=ptr(md['m_Shader'],material).deref().read_typetree()['m_ParsedForm']['m_Name']
assert shader=='TextMeshPro/Distance Field', 'Native Continue font shader family changed'
material_record={'original':p.object_id(material),'shader':shader,'keywords':md.get('m_ValidKeywords',[]),'renderQueue':md['m_CustomRenderQueue'],
 'floats':[{'name':key,'value':value} for key,value in saved['m_Floats']],
 'colors':[{'name':key,'value':value} for key,value in saved['m_Colors']]}
(out/'font-material.json').write_text(json.dumps(material_record,indent=2)+'\n')
atlas=ptr(fd['m_AtlasTextures'][0],font).read();atlas.image.save(out/'font-atlas.png')
links={'m_Script','m_GameObject','material','spriteSheet','m_SourceFontFile','m_AtlasTextures','m_SourceFontFile_EditorRef','fallbackFontAssetTable','m_FallbackFontAssetTable'}
(out/'font.json').write_text(json.dumps({k:v for k,v in fd.items() if k not in links},indent=2)+'\n')
(out/'button.json').write_text(json.dumps({'button':d,'image':image_data,'text':td,'sprites':records,'font':p.object_id(font),'atlas':p.object_id(atlas.object_reader)},indent=2)+'\n')
# The confirmation's full-screen backdrop is on its root (not only its Blur
# child). Export actual RectTransform/layout/graphic declarations and artwork.
confirmation_env=UnityPy.load(str(root/'ressources/GH_Data/level1'));confirmation_env.typetree_generator=gen
confirmation_ids={x.path_id:x for x in confirmation_env.objects}
fonts={}
def export_font(font_reader,material_reader):
 key=(font_reader.assets_file.name,font_reader.path_id,material_reader.path_id)
 if key in fonts:return fonts[key]
 prefix='confirmation-font-'+str(len(fonts));fonts[key]=prefix
 fd=p.authored_tree(font_reader);md=p.authored_tree(material_reader);saved=md['m_SavedProperties']
 atlas=ptr(fd['m_AtlasTextures'][0],font_reader).read();atlas.image.save(out/(prefix+'-atlas.png'))
 (out/(prefix+'.json')).write_text(json.dumps({k:v for k,v in fd.items() if k not in links},indent=2)+'\n')
 (out/(prefix+'-material.json')).write_text(json.dumps({'keywords':md.get('m_ValidKeywords',[]),'renderQueue':md['m_CustomRenderQueue'],
  'floats':[{'name':k,'value':v} for k,v in saved['m_Floats']], 'colors':[{'name':k,'value':v} for k,v in saved['m_Colors']]},indent=2)+'\n')
 return prefix
def clean(value):
 if isinstance(value,dict):return {k:clean(v) for k,v in value.items() if not (isinstance(v,dict) and 'm_PathID' in v)}
 if isinstance(value,list):return [clean(v) for v in value]
 return value
def confirmation_node(reader):
 go=reader.read();result={'name':go.m_Name,'active':bool(go.m_IsActive),'original':p.object_id(reader),'components':[]};transform=None
 for slot in go.m_Component:
  component=slot.component.deref();kind=component.type.name
  if kind=='MonoBehaviour':kind=component.parse_monobehaviour_head().m_Script.read().m_ClassName
  record={'kind':kind,'original':p.object_id(component)}
  if kind in ('RectTransform','Image','TextMeshProUGUI','VerticalLayoutGroup','HorizontalLayoutGroup','ContentSizeFitter','LayoutElement'):
   d=p.authored_tree(component);record['json']=json.dumps(clean(d))
   if kind=='RectTransform':transform=component.read()
   if kind=='Image':
    record['shader']='UI/Default'
    if d['m_Material']['m_PathID']:
     material=ptr(d['m_Material'],component).deref();md=p.authored_tree(material)
     record['shader']=ptr(md['m_Shader'],material).deref().read_typetree()['m_ParsedForm']['m_Name']
     record['material']=p.object_id(material)
    if d['m_Sprite']['m_PathID']:
     sprite_reader=ptr(d['m_Sprite'],component).deref();sprite=sprite_reader.read()
     record['sprite']='confirmation-sprite-'+str(component.path_id);sprite.image.save(out/(record['sprite']+'.png'))
     record.update(border=p.vector(sprite.m_Border,'xyzw'),pivot=p.vector(sprite.m_Pivot,'xy'),ppu=sprite.m_PixelsToUnits,spriteOriginal=p.object_id(sprite_reader))
   if kind=='TextMeshProUGUI':record['font']=export_font(ptr(d['m_fontAsset'],component).deref(),ptr(d['m_sharedMaterial'],component).deref())
  result['components'].append(record)
 if transform:result['children']=[confirmation_node(c.read().m_GameObject.deref()) for c in transform.m_Children]
 return result
confirmation=confirmation_node(confirmation_ids[1337]);assert confirmation['name']=='Confirmation Box_pc'
assert [n['name'] for n in confirmation['children']]==['Blur','Box']
root_image=next(c for c in confirmation['components'] if c['kind']=='Image');assert json.loads(root_image['json'])['m_Color']['a']>.79
assert next(c for c in confirmation['children'][0]['components'] if c['kind']=='Image')['shader']=='Custom/SimpleGrabPassBlur'
(out/'native-confirmation-tree.json').write_text(json.dumps(confirmation,indent=2)+'\n')
(out/'native-confirmation-provenance.json').write_text(json.dumps({'scene_sha256':hashlib.sha256((root/'ressources/GH_Data/level1').read_bytes()).hexdigest(),
 'presentation':'Original rect/layout/image/TMP declarations; original sprites and font data. Native game controllers are not installed. Localized question is an explicit input.',
 'nativeDefaultGraphicShader':'Original Box and full-screen root use built-in UI/Default; actual editor stock shader executes. Native Blur shader uses declared GL GrabPass stand-in.'},indent=2)+'\n')
(out/'pins.json').write_text(json.dumps({x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in out.iterdir() if x.is_file()},indent=2)+'\n')
print('exported original native widget sprites/font',out)
