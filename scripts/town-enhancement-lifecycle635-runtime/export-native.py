#!/usr/bin/env python3
"""Read the original enchantress effect/loop references and timing from level4."""
import hashlib, json, sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base,out=map(Path,sys.argv[1:]);out.parent.mkdir(parents=True,exist_ok=True)
generator=TypeTreeGenerator('2021.3.5f1');generator.load_local_dll_folder(str(base/'Managed'))
environment=UnityPy.load(str(base/'level4'));environment.typetree_generator=generator
objects={obj.path_id:obj for obj in environment.objects}
effect=objects[15436].read_typetree();loop=objects[11055].read_typetree()
def local_go(ptr):
    obj=objects[ptr['m_PathID']];tree=obj.read_typetree()
    return tree['m_GameObject']['m_PathID'] if 'm_GameObject' in tree else obj.path_id
record={'source':'level4','sha256':hashlib.sha256((base/'level4').read_bytes()).hexdigest(),
    'effectMono':15436,'effectGameObject':local_go({'m_PathID':15436}),
    'rotationTargetGameObject':local_go(effect['enchantressEffect']),
    'rotationTime':effect['rotationTime'],'rotationSpeed':effect['rotationSpeed'],
    'idleAnimatorMono':effect['idleAnimator']['m_PathID'],'idleGameObject':local_go(effect['idleAnimator']),
    'buyGameObject':local_go(effect['buyEffect']),'sellGameObject':local_go(effect['sellEffect']),
    'autoStart':loop['autoStart'],'ignoreTimeScale':loop['ignoreTimeScale'],
    'effects':loop['effects']}
out.write_text(json.dumps(record,indent=2)+'\n');print('Exported original native effect and loop: '+str(out))
