#!/usr/bin/env python3
"""Read-only audit of native results/ready geometry and rendering component types."""
import collections,hashlib,json,sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base,out=map(Path,sys.argv[1:3]);scene=base/'level6';scripts=base/'globalgamemanagers.assets'
e=UnityPy.load(str(scene));generator=TypeTreeGenerator('2021.3.5f1');generator.load_local_dll_folder(str(base/'Managed'));e.typetree_generator=generator
objects={o.path_id:o for o in e.objects};globalObjects={o.path_id:o for o in UnityPy.load(str(scripts)).objects};counts=collections.Counter()
def walk(gid):
 go=objects[gid].read_typetree();rect=objects[go['m_Component'][0]['component']['m_PathID']].read_typetree()
 for c in go['m_Component']:
  obj=objects[c['component']['m_PathID']]
  if obj.type.name=='MonoBehaviour':
   # The serialized script-type table is authoritative even in stripped players;
   # a generated MonoBehaviour base-header tree may not expose a valid m_Script.
   ref=obj.assets_file.script_types[obj.serialized_type.script_type_index];assert ref.local_serialized_file_index==1
   script=globalObjects[ref.local_identifier_in_file].read_typetree();counts[script.get('m_Namespace','')+'.'+script['m_ClassName']]+=1
  elif obj.type.name!='RectTransform':counts[obj.type.name]+=1
 for child in rect['m_Children']:walk(objects[child['m_PathID']].read_typetree()['m_GameObject']['m_PathID'])
 return rect
results,ready=walk(957),walk(807)
record={'source':str(scene),'source_sha256':hashlib.sha256(scene.read_bytes()).hexdigest(),'script_source_sha256':hashlib.sha256(scripts.read_bytes()).hexdigest(),'results_root':results,'ready_root':ready,'components':dict(sorted(counts.items())),'limits':'Serialized original component/geometry census; native live values and callbacks are separate runtime subjects.'}
out.write_text(json.dumps(record,indent=2)+'\n')
