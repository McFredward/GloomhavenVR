#!/usr/bin/env python3
"""Prove native summon selection survives highlight creation before Unity layout."""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
BASE='ce637a1dc'
INVARIANT='native selectable summon rect follows its post-highlight layout target'
def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc666-overlay')
    p.add_argument('--controls',action='store_true')
    a=p.parse_args();root=a.source_root.resolve();a.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()))
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    (run/'town-purse-runtime').symlink_to(root/'scripts/town-purse-runtime',target_is_directory=True)
    shutil.copyfile(HERE/'Overlay666.cs',fixture/'Overlay666.cs')
    native=run/'native'
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(HERE/'export-native.py'),str(root),str(native)],check=True)
    spec=importlib.util.spec_from_file_location('binding666',root/'scripts/check-town-service-mirror.py')
    checker=importlib.util.module_from_spec(spec);spec.loader.exec_module(checker)
    env=dict(os.environ,DOTNET_ROOT=str(Path.home()/'.dotnet'));dll=root/'ressources/GH_Data/Managed/GH.Runtime.dll'
    exports={}
    for name in ['CreateLayout','EnhancementButtonBase','EnhancementButton','CardEnhancementElements','UINewEnhancementWindow','UIEnhancementButtonHighlight','EnhancementLine','EnhancementLineFilter','EnhancementUtils']:
        source=subprocess.check_output([str(Path.home()/'.dotnet/tools/ilspycmd'),'--disable-updatecheck','-t',name,str(dll)],env=env,text=True)
        (native/(name+'.cs')).write_text(source);exports[name]=source.expandtabs(4)
    ports=(HERE/'NativePorts.cs').read_text()
    ports=ports.replace('NATIVE_ENHANCEMENT_METHODS','\n'.join(checker.method(exports['EnhancementButton'],s)for s in ['public override void Init(','public void UpdateEnhancement(','public Sprite GetEnhancementSprite(']))
    ports=ports.replace('NATIVE_WINDOW_METHODS','\n'.join(checker.method(exports['UINewEnhancementWindow'],s)for s in ['private void HighlightButtons(','private void ClearHighlights(','private void SetEnhanceFilters(']))
    pointer=(root/'src/GloomhavenVR/Hands/Interact/UguiPointer.cs').read_text()
    ports=ports.replace('NATIVE_RAYCAST_METHOD',checker.method(pointer,'private bool TryRaycastTop('))
    handoff=(root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementHandoff.cs').read_text()
    ports=ports.replace('NATIVE_AREA_METHOD',checker.method(handoff,'internal static bool TryNativeArea(').replace('VRCard?','GloomhavenVR.Cards.VRCard?'))
    (fixture/'NativePorts666.cs').write_text(ports)
    prefix='#nullable disable\n#pragma warning disable CS0169\nusing System;using System.Linq;using System.Collections.Generic;using ScenarioRuleLibrary;using ScenarioRuleLibrary.YML;using UnityEngine;using UnityEngine.UI;using TMPro;\nnamespace GloomhavenVR.WorldUI {\n'
    classes=''
    for name in ['EnhancementButtonBase','CardEnhancementElements','UIEnhancementButtonHighlight','EnhancementLine','EnhancementLineFilter']:
        text=exports[name];start=text.index('[RequireComponent' if name=='UIEnhancementButtonHighlight' else ('[Serializable]' if name=='CardEnhancementElements' else 'public class'))
        body=text[start:]
        classes+=body+'\n'
    classes+='internal static class EnhancementUtils {\n'+ '\n'.join(checker.method(exports['EnhancementUtils'],s)for s in ['public static List<EnhancementLine> GetEnhancementLines(','private static IEnumerable<EnhancementButtonBase> GetEnhancementButtons(','public static bool CanBeEnhanced(this EnhancementButtonBase'])+'\n}\n'
    layout='''internal sealed class CreateLayout {
internal GameObject FullLayout;
internal CreateLayout(object group,Rect bounds,int id,bool isLongRest,CardEnhancementElements elements) { FullLayout=new GameObject("Native special-text layout boundary",typeof(RectTransform));((RectTransform)FullLayout.transform).sizeDelta=bounds.size; }
internal float GenerateFullLayout()=>0;
internal static string LocaliseText(string text)=>text.Replace("$Summon$","Summon").Replace("$SlimeSpirit$","Slime spirit").Replace("$BurningAvatar$","Burning avatar");
'''+ '\n'.join(checker.method(exports['CreateLayout'],s)for s in ['public static void CreateSummon(','public static void CreateSummonStatText(','private static void CreateEnhancement('])+'\n}\n'
    bound_native=prefix+classes+layout+'}\n'
    pub=fixture/'Publisher.cs';text=pub.read_text().replace('    internal sealed class UIEnhancementButtonHighlight : MonoBehaviour { }\n','')
    text=text.replace('internal Transform fullAbilityCard = null!;','internal Transform fullAbilityCard = null!; internal CardEnhancementElements EnhancementElements=Native666.NewElements();')
    text=text.replace('internal sealed class TownServiceEnhancementHandoff','internal sealed partial class TownServiceEnhancementHandoff');pub.write_text(text)
    # Real game model DLLs, copied once as dependency evidence, never edited.
    deps=['ScenarioRuleLibrary.dll','SharedLibrary.dll','ThirdParty.dll']
    project=fixture/'Mirror.csproj';text=project.read_text().replace('</ItemGroup>',''.join(f'<Reference Include="{n}"><HintPath>{root / "ressources/GH_Data/Managed" / n}</HintPath><Private>false</Private></Reference>'for n in deps)+'</ItemGroup>');project.write_text(text)
    editor=fixture/'Editor/MirrorRunner.cs';text=editor.read_text();anchor='        VerifyCoroutineScheduling();'
    text=text.replace(anchor,'        AppDomain.CurrentDomain.AssemblyResolve += (sender,eventArgs) => { string path=Path.Combine('+json.dumps(str(root/'ressources/GH_Data/Managed'))+',new AssemblyName(eventArgs.Name).Name+".dll"); return File.Exists(path)?Assembly.LoadFrom(path):null; };\n'+anchor);editor.write_text(text)
    program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();';assert text.count(anchor)==1
    text=text.replace(anchor,'            if (suite == "lifecycle") { var overlay=Overlay666();while(overlay.MoveNext())yield return overlay.Current;File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break; }\n'+anchor);program.write_text(text)
    source=(root/'scripts/check-town-service-mirror.py').read_text();anchor='    bound, hashes = sources(args.source_root)';assert source.count(anchor)==1
    source=source.replace(anchor,anchor+'\n    bound["NativeControllers666.cs"] = '+repr(bound_native)+'\n    hashes["NativeControllers666.cs"] = hashlib.sha256(bound["NativeControllers666.cs"].encode()).hexdigest()')
    name='TownServiceNativeEnhancementCardMask.cs';path='src/GloomhavenVR/WorldUI/TownServices/'+name
    current=(root/path).read_text();old=subprocess.check_output(['git','-C',str(root),'show',BASE+':'+path],text=True)
    variants=[('production',None,None,None,'')]
    if a.controls:
        variants.append(('old665-stale-summon-rect',name,current,old,INVARIANT))
        variants.append(('summon-format-left-stale',name,'                frame.material.SetVector(property, format);','                frame.material.SetVector(property, frame.material.GetVector(property));','native frame shader format follows the actual laid-out summon cell'))
        variants.append(('native-selection-callback-lost','NativeControllers666.cs','        onSelectedAbility = onSelect;','        onSelectedAbility = null;','actual native click selects the genuine ability and enhancement line once'))
    anchor='    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)';assert source.count(anchor)==1
    source=source.replace(anchor,'    variants = '+repr(variants)+'\n'+anchor)
    anchor="    command += ['-nativePurseData', str(native_purse)]";assert source.count(anchor)==1
    source=source.replace(anchor,anchor+"\n    command += ['-native666', "+repr(str(native))+", '-rules666', "+repr(str(root/'ressources/GH_Data/StreamingAssets/Rulebase/Global.ruleset'))+"]")
    # Native shader property probe: original material identity is a stated boundary.
    anchor='    manifest_path = run / "manifest.json";'
    source=source.replace(anchor,'    shutil.copyfile('+repr(str(HERE/'RectFormat666.shader'))+', project / "Assets/RectFormat666.shader")\n'+anchor)
    generated=run/'bound-checker.py';generated.write_text(source)
    (run/'provenance.json').write_text(json.dumps({'baseline':BASE,'native_source_sha256':{k:hashlib.sha256(v.encode()).hexdigest()for k,v in exports.items()},'game_dependencies':{n:hashlib.sha256((root/'ressources/GH_Data/Managed'/n).read_bytes()).hexdigest()for n in deps},'bound_controller_sha256':hashlib.sha256(bound_native.encode()).hexdigest(),'old_mask_sha256':hashlib.sha256(old.encode()).hexdigest(),'current_mask_sha256':hashlib.sha256(current.encode()).hexdigest(),'input_sources_sha256':{str(p):hashlib.sha256(p.read_bytes()).hexdigest()for p in HERE.iterdir()if p.is_file()},'boundary':'Actual game model DLL/YML parsers, native summon construction and selection/highlight methods; actual mask, Sync capture/codec/replay, GraphicRaycaster and production VR native-area validation. Asset loader, shop/navigation shell, font/settings, special-text row, ExtendedButton audio/lock shell and frame GPU shader are explicit ports. No headset proof.'},indent=2)+'\n')
    print('Evidence: '+str(run),flush=True)
    command=['python3',str(generated),'--source-root',str(root),'--fixture-dir',str(fixture),'--suite','lifecycle','--no-negative-controls','--output-dir',str(run/'proof')]
    result=subprocess.run(command,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (run/'command.log').write_text(result.stdout);print(result.stdout,end='');raise SystemExit(result.returncode)
if __name__=='__main__':main()
