#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${GHVR_TEST_SOURCE_ROOT:-$repo_root}"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 - "$repo_root" "$source_root" <<'PY'
from pathlib import Path
import importlib.util,json,os,shutil,subprocess,sys,tempfile
root,source_root=map(Path,sys.argv[1:]);out=root/'.planning/debug/level-up-composite';out.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=out));project=run/'project';project.mkdir()
fixture=root/'tests/GloomhavenVR.LevelUpCompositeTests'
for source in fixture.glob('*'):
 if source.is_file() and source.suffix in ('.cs','.csproj'):shutil.copy2(source,project/source.name)
source=source_root/'src/GloomhavenVR/WorldUI/Composites/LevelUpComposite.cs'
layers=source_root/'src/GloomhavenVR/WorldUI/Composites/MovedSubtreeLayers.cs'
source_dir=run/'sources';source_dir.mkdir()
import hashlib
bindings=[]
for name,original in [('LevelUpComposite.cs',source),('MovedSubtreeLayers.cs',layers)]:
 snapshot=source_dir/name;shutil.copy2(original,snapshot)
 bindings.append({'original':str(original),'snapshot':str(snapshot),'sha256':hashlib.sha256(snapshot.read_bytes()).hexdigest()})
source=source_dir/'LevelUpComposite.cs';layers=source_dir/'MovedSubtreeLayers.cs'
(run/'source-bindings.json').write_text(json.dumps(bindings,indent=2))
log=run/'console.log';log.write_text('Evidence: '+str(run)+'\n')
print('Evidence: '+str(run),flush=True)
status=1
try:
 # A helper-only test could pass while production never invokes it. These narrow
 # source bindings prove enrollment/order, while the executable proves behavior.
 hook=(source_root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.10.CatchAll.cs').read_text()
 start=hook.index('private static void TickCatchAll(')
 if hook.index('LevelUpComposite.Tick();',start)>hook.index('if (UnknownShown.Count == 0)',start):raise RuntimeError('Composite must run before catch-all can return')
 release=(source_root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.4.Tick.cs').read_text()
 start=release.index('internal static void Detach(')
 if release.index('LevelUpComposite.Reset();',start)>release.index('ReleaseAllWindows(',start):raise RuntimeError('Composite must return original before float teardown')
 for relative,call in [('Conversion/CanvasConversion.3.Fit.cs','AddSubViewCandidate'),('Conversion/CanvasConversion.9c.SubViewBurst.cs','MixOpenSubView'),('Conversion/CanvasConversion.9g.SubViewSeatVeil.cs','AddSeatVeilMember')]:
  original=source_root/'src/GloomhavenVR/WorldUI'/relative
  content=original.read_text()
  for member in ('FittedView','InventoryView'):
   if call+'(panel, LevelUpComposite.'+member not in content:raise RuntimeError('Fixed-fit membership mismatch: '+relative+' '+member)
 print('Level-up composite enrollment: 8 source-order/membership checks passed.',flush=True)
 command=['dotnet','run','--project',str(project/'GloomhavenVR.LevelUpCompositeTests.csproj'),'--configuration','Release','--property:LevelUpSource='+str(source),'--property:LayerSource='+str(layers)]
 env=os.environ.copy();env['GHVR_LEVELUP_REPORT']=str(run/'geometry.json')
 result=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,env=env)
 with log.open('a') as stream:stream.write(result.stdout)
 print(result.stdout,end='',flush=True)
 if result.returncode:sys.exit(result.returncode)
 # Each control executes the same production-linked hierarchy/geometry fixture.
 mutations=[
  ('lifetime','level.myWindow.IsOpen','level.IsShowing','sibling inventory becomes part of actual native level-up owner'),
  ('handover','if (!ModalFallback.ReleaseForComposite(inventoryWindow)) return;','if (inventoryWindow == null) return;','standalone float retires before native home capture'),
  ('overlap','_previewRight + ChromeParkTuning.ChromeGapPx + _rect.rect.width * .5f','0f','full preview right edge remains readable beside inventory'),
  ('restore','_rect.SetParent(_home, false);','_rect.SetParent(_rect.parent, false);','original parent restored'),
  ('owner-change','if (!ReferenceEquals(inventory, _inventory) || !ReferenceEquals(owner, _owner)\n            || (_mount != null && !ReferenceEquals(_mount.parent, mountParent)))','if (!ReferenceEquals(inventory, _inventory))','reset browse rebinds same inventory to current party owner'),
  ('native-hide','Layers.Restore();','Layers.Restore(); _inventory!.GetComponent<UIWindow>()!.Hide();','native show/hide callbacks untouched'),
  ('comparison-seat','_comparison = new ComparisonSeat(comparison, ownerRect);','_comparison = null;','existing owned deck shares level root through independent mount'),
  ('refused-reset','if (_rect != null || _mount != null || _comparison != null) return;','if (inventoryWindow == null) return;','refused restoration must not capture an old mount as a new native home'),
 ]
 text=source.read_text()
 for name,old,new,expected in mutations:
  if text.count(old)!=1:raise RuntimeError('Mutation target must occur exactly once: '+name)
  mutant=run/(name+'.fixture');mutant.write_text(text.replace(old,new))
  mutant_command=[value if not value.startswith('--property:LevelUpSource=') else '--property:LevelUpSource='+str(mutant) for value in command]
  mutant_env=env.copy();mutant_env.pop('GHVR_LEVELUP_REPORT',None)
  result=subprocess.run(mutant_command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,env=mutant_env)
  (run/(name+'.log')).write_text(result.stdout)
  if result.returncode==0:raise RuntimeError('Negative control escaped fixture: '+name)
  if expected not in result.stdout:raise RuntimeError('Negative control failed at wrong boundary: '+name+'\n'+result.stdout)
  message='Level-up composite negative control: '+name+' rejected.\n'
  with log.open('a') as stream:stream.write(message)
  print(message,end='',flush=True)
 status=0
finally:
 spec=importlib.util.spec_from_file_location('retention',root/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);spec.loader.exec_module(retention)
 result=retention.cleanup_completed_run('level-up-composite',status,log,root,process_reaped=True)
 (run/'cleanup.json').write_text(json.dumps(result,indent=2))
PY
