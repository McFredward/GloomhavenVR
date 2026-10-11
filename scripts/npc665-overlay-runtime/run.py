#!/usr/bin/env python3
"""Verify native summon overlay layout and renderer suppression through real town capture/playback."""
import argparse, hashlib, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
BASE='c9462f78b'
INVARIANT='native summon stat target retains its original label layout while printed artwork is masked'
def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--source-root',type=Path,default=ROOT)
 p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc665-overlay')
 p.add_argument('--controls',action='store_true')
 a=p.parse_args();root=a.source_root.resolve();a.output_dir.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()))
 fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
 (run/'town-purse-runtime').symlink_to(root/'scripts/town-purse-runtime',target_is_directory=True)
 shutil.copyfile(Path(__file__).with_name('Overlay665.cs'),fixture/'Overlay665.cs')
 native=run/'native';subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(Path(__file__).with_name('export-native.py')),str(root),str(native)],check=True)
 # Use the actual native Highlight body; interaction setup is an explicit state
 # boundary because the geometry proof never purchases or invokes a callback.
 env=dict(os.environ,DOTNET_ROOT=str(Path.home()/'.dotnet'))
 dll=root/'ressources/GH_Data/Managed/GH.Runtime.dll'
 decompiled=subprocess.run([str(Path.home()/'.dotnet/tools/ilspycmd'),'--disable-updatecheck','-t','UIEnhancementButtonHighlight',str(dll)],env=env,capture_output=True,text=True,check=True).stdout
 start=decompiled.index('\tprivate void Highlight(RectTransform target,');end=decompiled.index('\n\tpublic void OnHovered',start)
 methods=decompiled[start:end]
 # Keep the real Highlight and SetInteractable code. Its external navigation
 # query and original ExtendedButton state port use their actual member shape.
 (native/'UIEnhancementButtonHighlight.cs').write_text(decompiled)
 native_source='''using System; using UnityEngine; using UnityEngine.UI;
namespace GloomhavenVR.WorldUI {
internal sealed class EnhancementButtonBase : MonoBehaviour {}
internal static class InputManager { internal static bool GamePadInUse => false; }
internal sealed class ExtendedButton : Button { internal bool IsNavigationEnabled; }
internal sealed partial class UIEnhancementButtonHighlight {
private enum HighlightState { PREVIEW, SELECTED, SELECTABLE, INVALID }
private Image frame=null!, fillImageIcon=null!; private CanvasGroup fillImage=null!;
private string shaderProperty="_RectFormat";private float opacitySelected=.7f;
private Color invalidFrameColor=Color.red,validFrameColor=Color.cyan;
private RectTransform rectTransform=null!;private EnhancementButtonBase ability=null!;
private ExtendedButton button=null!;private Action<EnhancementButtonBase> onSelectedAbility=null!;
private HighlightState state=HighlightState.SELECTED;private bool isNavigationEnabled,isInteractable;
internal void Native665(RectTransform target, Image ink, CanvasGroup fill) {
rectTransform=(RectTransform)transform;frame=fillImageIcon=ink;fillImage=fill;
if(button==null)button=gameObject.AddComponent<ExtendedButton>();Highlight(target,null!); }
'''+methods+'\n}\n}\n'
 pub=fixture/'Publisher.cs';text=pub.read_text();text=text.replace('internal sealed class UIEnhancementButtonHighlight : MonoBehaviour { }','internal sealed partial class UIEnhancementButtonHighlight : MonoBehaviour { }');pub.write_text(text)
 program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
 assert text.count(anchor)==1
 text=text.replace(anchor,'            if (suite == "lifecycle") { var overlay = Overlay665(); while(overlay.MoveNext()) yield return overlay.Current; File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break; }\n'+anchor);program.write_text(text)
 checker=root/'scripts/check-town-service-mirror.py';source=checker.read_text();opening='    bound, hashes = sources(args.source_root)';assert source.count(opening)==1
 source=source.replace(opening,opening+'\n    bound["NativeHighlight665.cs"] = '+repr(native_source)+'\n    hashes["NativeHighlight665.cs"] = hashlib.sha256(bound["NativeHighlight665.cs"].encode()).hexdigest()')
 name='TownServiceNativeEnhancementCardMask.cs';path='src/GloomhavenVR/WorldUI/TownServices/'+name
 current=(root/path).read_text();old=subprocess.check_output(['git','-C',str(root),'show',BASE+':'+path],text=True)
 variants=[('production',None,None,None,'')]
 if a.controls:
  variants.append(('old664-disabled-layout-label',name,current,old,INVARIANT))
  variants.append(('inactive-highlight-masked',name,current,current.replace('graphic.GetComponentInParent<UIEnhancementButtonHighlight>(true)','graphic.GetComponentInParent<UIEnhancementButtonHighlight>()'),'remote native small rectangle ink remains enabled'))
  # This exact statement is the one per-render guard; initial Mask remains.
  variants.append(('renderer-overwrite-leaks',name,'        if (_masked) HidePrintedArt();','        /* native renderer overwrite left visible */','masked native summon label preserves layout activity without drawing pooled artwork'))
  variants.append(('renderer-restore-lost',name,'                if (_layoutArt[i]) _art[i].canvasRenderer.SetColor(_rendererColors[i]);','                /* original renderer colour not restored */','mask restore returns original native summon enabled and renderer colour exactly'))
  variants.append(('receiver-renderer-alpha-lost','TownServiceBinding.cs','case TownServiceProperty.Renderer: Require<CanvasRenderer>(node).SetColor(ColorAt(n, 0)); break;','case TownServiceProperty.Renderer: Require<CanvasRenderer>(node).SetColor(Color.white); break;','remote original renderer stream suppresses pooled summon text while retaining enabled layout provider'))
 anchor='    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)';assert source.count(anchor)==1
 source=source.replace(anchor,'    variants = '+repr(variants)+'\n'+anchor)
 anchor="    command += ['-nativePurseData', str(native_purse)]";assert source.count(anchor)==1
 source=source.replace(anchor,anchor+"\n    command += ['-nativeSummon665', "+repr(str(native/'summon.bin'))+"]")
 generated=run/'bound-checker.py';generated.write_text(source)
 (run/'provenance.json').write_text(json.dumps({'base':BASE,'old_mask_sha256':hashlib.sha256(old.encode()).hexdigest(),
 'native_highlight_method_sha256':hashlib.sha256(methods.encode()).hexdigest(),
 'native_full_controller_sha256':hashlib.sha256(decompiled.encode()).hexdigest(),
 'source_sha256':{str(f):hashlib.sha256(f.read_bytes()).hexdigest()for f in [Path(__file__),Path(__file__).with_name('Overlay665.cs'),Path(__file__).with_name('export-native.py'),root/path,checker]},
 'boundary':'Original serialized SummonContainer and native Highlight method; actual mask/Sync Publish/capture/codec/Receive/apply; native template partition registry uses one original subtree per module. Printed FullAbilityCard root/row placement and editor TMP font atlas are fixture boundaries; no native payment/controller callbacks. Not headset pixel proof.'},indent=2)+'\n')
 command=['python3',str(generated),'--source-root',str(root),'--fixture-dir',str(fixture),'--suite','lifecycle','--no-negative-controls','--output-dir',str(run/'proof')]
 print('Evidence: '+str(run),flush=True);result=subprocess.run(command,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 (run/'command.log').write_text(result.stdout);print(result.stdout,end='');raise SystemExit(result.returncode)
if __name__=='__main__':main()
