"""Read-only native party/creation geometry, art and animation-endpoint export."""
import hashlib
import json
import sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

root, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
generator = TypeTreeGenerator('2021.3.5f1')
generator.load_local_dll_folder(str(root / 'ressources/GH_Data/Managed'))
source = root / 'ressources/GH_Data/level9'
environment = UnityPy.load(str(source))
environment.typetree_generator = generator
objects = {obj.path_id: obj for obj in environment.objects}
sprites = {}


def vec(value, axes='xy'):
    return {axis: float(getattr(value, axis)) for axis in axes}


def kind(obj):
    return obj.parse_monobehaviour_head().m_Script.read().m_ClassName if obj.type.name == 'MonoBehaviour' else obj.type.name


rows = []


def visit(transform, parent=0):
    go = transform.m_GameObject.read()
    record = dict(nodeId=transform.object_reader.path_id, parentId=parent, name=go.m_Name,
                  active=bool(go.m_IsActive), min=vec(transform.m_AnchorMin), max=vec(transform.m_AnchorMax),
                  size=vec(transform.m_SizeDelta), pivot=vec(transform.m_Pivot), position=vec(transform.m_AnchoredPosition),
                  scale=vec(transform.m_LocalScale, 'xyz'), rotation=vec(transform.m_LocalRotation, 'xyzw'))
    for entry in go.m_Component:
        obj = entry.component.deref()
        name = kind(obj)
        if name not in ('Image', 'RawImage', 'CanvasGroup', 'UIWindow', 'RectMask2D', 'Mask', 'TextMeshProUGUI',
                        'UIKeyboard', 'UICharacterCreatorClassStep', 'UICharacterCreatorPersonalQuestStep'):
            continue
        data = obj.read_typetree()
        if name == 'UIKeyboard':
            record.update(keyboard=True)
            continue
        if name in ('UICharacterCreatorClassStep', 'UICharacterCreatorPersonalQuestStep'):
            field = 'abilityCardsViewer' if name.endswith('ClassStep') else 'helpText'
            pointer = data[field]['m_PathID']
            linked = objects[pointer].read().m_GameObject.read().m_Component[0].component.path_id
            record.update(classStep=name.endswith('ClassStep'), questStep=name.endswith('QuestStep'), linked=linked)
            continue
        if name == 'CanvasGroup':
            record.update(group=True, alpha=float(data['m_Alpha']))
        elif name == 'UIWindow':
            record.update(window=True, shown=data['m_StartingState'] == 0)
        elif name in ('Mask', 'RectMask2D'):
            record.update(mask=name)
        elif name in ('Image', 'RawImage'):
            record.update(image=name == 'Image', raw=name == 'RawImage', color=data['m_Color'], enabled=bool(data['m_Enabled']))
            if name == 'RawImage':
                record.update(nativeTexture=data['m_Texture'], uv=data['m_UVRect'])
            if name == 'Image':
                record.update(imageType=data['m_Type'])
                sprite = obj.read().m_Sprite
                if sprite.path_id:
                    key = str(sprite.file_id) + '-' + str(sprite.path_id)
                    if key not in sprites:
                        value = sprite.read(); filename = 'sprite-' + key + '.png'
                        value.image.save(output / filename)
                        sprites[key] = dict(sprite=filename, border=vec(value.m_Border, 'xyzw'),
                                            spritePivot=vec(value.m_Pivot), ppu=float(value.m_PixelsToUnits))
                    record.update(sprites[key])
        else:
            record.update(text=True, value=data.get('m_text', ''), fontSize=float(data['m_fontSize']),
                          horizontal=data['m_HorizontalAlignment'], vertical=data['m_VerticalAlignment'])
    rows.append(record)
    for pointer in transform.m_Children:
        visit(pointer.read(), record['nodeId'])


target = objects[7924].read()
# Party rail and its actual campaign selector/creator only. Other native tabs
# are absent ports, never silently active gameplay components in this fixture.
go = target.m_GameObject.read()
record = dict(nodeId=7924, parentId=0, name=go.m_Name, active=True,
              min=vec(target.m_AnchorMin), max=vec(target.m_AnchorMax), size=vec(target.m_SizeDelta),
              pivot=vec(target.m_Pivot), position=vec(target.m_AnchoredPosition), scale=vec(target.m_LocalScale, 'xyz'),
              rotation=vec(target.m_LocalRotation, 'xyzw'))
rows.append(record)
for node in (11014, 8177):
    visit(objects[node].read(), 7924)
animations = []
for identity in (14088, 18969):
    data = objects[identity].read_typetree()
    for item in data['animations']['moveEffects']:
        animations.append(dict(owner=identity, target=item['Target']['m_PathID'], mode=item['Animation'],
                               original=item['OriginalValue'], to=item['ToValue'], duration=item['Duration']))
(output / 'creation764.json').write_text(json.dumps(dict(records=[json.dumps(row) for row in rows], animations=animations), indent=2) + '\n')
(output / 'provenance.json').write_text(json.dumps(dict(source=str(source), sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    rects=len(rows), sprites=len(sprites), note='Original geometry/art and serialized animation endpoints; gameplay/native population are explicit inputs.'), indent=2) + '\n')
