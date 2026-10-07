"""Restore original drawing state for every native nonpacked Sprite."""
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import sys

from storage import BuildError, write_json, build_progress


def restore_fields(text, fields, width, height, texture):
    from packed_sprites import field_edits, vector, rectangle, scalar, vertex_bytes
    render = fields['m_RD']
    vertex = render['m_VertexData']
    if vertex['m_VertexCount']:
        data, vertices, uv = vertex_bytes(vertex, render, width, height)
    else:
        data, vertices, uv = bytes(vertex['m_DataSize']), [], []
    changes = {(name,): vector(fields[name]) for name in ('m_Offset','m_Border','m_Pivot')}
    changes[('m_Rect',)] = rectangle(fields['m_Rect'],4)
    changes[('m_PixelsToUnits',)] = scalar(fields['m_PixelsToUnits'])
    changes[('m_RD','m_VertexData','_typelessdata')] = data.hex()
    for name in ('textureRect','textureRectOffset','atlasRectOffset','uvTransform'):
        changes[('m_RD',name)] = rectangle(render[name],6) if name == 'textureRect' else vector(render[name])
    for name in ('settingsRaw','downscaleMultiplier'):
        changes[('m_RD',name)] = str(render[name])
    if texture is not None:
        changes[('m_RD','texture')] = '{fileID: '+str(texture['fileId'])+', guid: '+texture['guid']+', type: 3}'
    return field_edits(text,changes), vertices, uv


def stage(project, game_data, *, cab_bundles):
    """Recover native CAB/pathID drawing geometry; preserve proven packed sprites."""
    recovery = Path(__file__).resolve().parents[1]/'quest-recovery'
    if str(recovery) not in sys.path:sys.path.append(str(recovery))
    from export_identity import object_index
    from pointer_recovery import load_native, native_target
    from recover import sha256
    import UnityPy
    project, game_data = Path(project), Path(game_data)
    objects = object_index(json.loads((project/'QuestRecovery/original-asset-identities.json').read_text())['identities'])
    owners = json.loads(Path(cab_bundles).read_text()) if isinstance(cab_bundles,(str,Path)) else cab_bundles
    owners = {key.casefold():value for key,value in owners.items()}
    packed = {}
    for path in (project/'QuestRecovery').glob('packed-sprites-*.json'):
        document=json.loads(path.read_text())
        for row in document['restoredMembers']:
            key=(row['collection'],row['pathId'])
            if key in packed:raise BuildError('Native packed Sprite appears in multiple atlas receipts.')
            if sha256(project/row['assetPath']) != row['sha256']:
                raise BuildError('Verified native packed Sprite drawing state changed before full Sprite recovery.')
            packed[key]=row
    groups=defaultdict(list)
    for key,target in objects.items():
        if target['classId']==213 and key not in packed:groups[owners.get(key[0],key[0])].append((key,target))
    counter = build_progress.Counter('prepare-items:native-sprites', sum(len(targets) for targets in groups.values()), "items")
    try:
        assets,containers=[],[]
        for container,targets in sorted(groups.items()):
            env=load_native(UnityPy,game_data/container)
            native_objects={(obj.assets_file.name.casefold(),int(obj.path_id)):obj for obj in env.objects}
            dependencies={}
            def resolve(key):
                if key in native_objects:return native_objects[key]
                owner=owners.get(key[0],key[0])
                if owner not in dependencies:
                    dependency=load_native(UnityPy,game_data/owner)
                    dependencies[owner]=(dependency,{(obj.assets_file.name.casefold(),int(obj.path_id)):obj for obj in dependency.objects})
                if key not in dependencies[owner][1]:raise BuildError('Original Sprite texture identity is unresolved.')
                return dependencies[owner][1][key]
            container_sha=sha256(game_data/container)
            containers.append({'path':container,'sha256':container_sha})
            for key,target in targets:
                if key not in native_objects:raise BuildError('Original nonpacked Sprite identity is unresolved.')
                original=native_objects[key];fields=original.read_typetree()
                if fields['m_SpriteAtlas']['m_PathID']:
                    raise BuildError('Original packed Sprite lacks witnessed native atlas recovery.')
                render=fields['m_RD'];pointer=render['texture'];texture=None;width=height=0
                if pointer['m_PathID']:
                    texture_key=native_target(original.assets_file,(pointer['m_FileID'],pointer['m_PathID']))
                    if texture_key not in objects or objects[texture_key]['classId'] != 28:
                        raise BuildError('Original Sprite source texture has no exact recovered native identity.')
                    texture=objects[texture_key];texture_fields=resolve(texture_key).read_typetree()
                    width,height=texture_fields['m_Width'],texture_fields['m_Height']
                if fields['m_RD']['m_VertexData']['m_VertexCount'] and not texture:
                    raise BuildError('Native Sprite has vertices without its original texture.')
                path=project/target['path'];before=path.read_text()
                after,vertices,uv=restore_fields(before,fields,width,height,texture)
                path.write_text(after)
                assets.append({'assetPath':target['path'],'guid':target['guid'],'fileId':target['fileId'],
                  'originalCollection':key[0],'originalPathId':key[1],
                  'originalObjectSha256':hashlib.sha256(original.get_raw_data()).hexdigest(),
                  'sourceContainer':container,'sourceContainerSha256':container_sha,
                  'beforeSha256':hashlib.sha256(before.encode()).hexdigest(),'sha256':sha256(path),
                  'rect':fields['m_Rect'],'pivot':fields['m_Pivot'],'border':fields['m_Border'],
                  'offset':fields['m_Offset'],'pixelsPerUnit':fields['m_PixelsToUnits'],
                  'textureGuid':texture['guid'] if texture else None,'textureWidth':width,'textureHeight':height,
                  'vertices':vertices,'uv':uv,'textureCrop':render['textureRect'],'trimOffset':render['textureRectOffset'],
                  'nativeDrawingStateRestored':True})
                counter.add(1, target['path'])
            # Keep only the current owning container/dependency closure alive. Loading
            # hundreds of immutable game bundles simultaneously would waste player RAM.
            dependencies.clear();native_objects.clear()
        receipt={'schema':1,'nativeSpriteCount':len(assets)+len(packed),'restoredNonPackedSpriteCount':len(assets),
                 'preservedPackedSpriteCount':len(packed),'assets':assets,'sourceContainers':containers,
                 'source':'original-native-CAB-pathID-rect-offset-border-pivot-and-drawing-streams',
                 'unityImportVerified':False,'headsetPictureVerified':False}
        write_json(project/'Assets/QuestOriginalCampaign/native-sprites.json',receipt)
        counter.finish()
        return receipt
    except BaseException as error:
        counter.fail(error)
        raise
