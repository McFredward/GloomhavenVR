#!/usr/bin/env python3
"""Source-bound quiet effect lifecycle with the shipped native animation engine.

The actual GH.Runtime UIEnchantressEffect/LoopAnimator/UILevelUpCardHolder and
GH.Runtime.FirstPass LeanTween execute. Native shop/payment callbacks and elapsed
time are explicit boundaries; this is not a headset rendering/network acceptance.
"""
import argparse, hashlib, json, os, shutil, subprocess, tempfile, re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def method(source,signature):
    start=source.index('    '+signature);opening=source.index('{',start);depth=1;end=opening+1
    while depth:
        if source[end]=='{':depth+=1
        elif source[end]=='}':depth-=1
        end+=1
    return source[start:end]
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-root',type=Path,default=ROOT);parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/town-enhancement-lifecycle635');parser.add_argument('--no-negative-controls',action='store_true');args=parser.parse_args()
    root=args.source_root;managed=root/'ressources/GH_Data/Managed';unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet');fixture=ROOT/'scripts/town-enhancement-lifecycle635-runtime'
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()));production={}
    exporter=fixture/'export-native.py';python=os.environ.get('UNITYPY_PYTHON','/home/claw/unitypy-venv/bin/python')
    subprocess.run([python,str(exporter),str(root/'ressources/GH_Data'),str(run/'native-effect.json')],check=True)
    native=json.loads((run/'native-effect.json').read_text());pulse=native['effects'][0]
    assert (native['effectGameObject'],native['rotationTargetGameObject'],native['idleGameObject'],native['buyGameObject'],native['sellGameObject'])==(2098,2098,2793,415,2675),'serialized original effect frame changed'
    assert native['rotationTime']==20 and native['rotationSpeed']==1 and not native['autoStart'] and not native['ignoreTimeScale'],'serialized original rotation clock changed'
    assert pulse['Transform']['m_PathID']==7358 and pulse['Animation']==24 and pulse['Duration']==2 and pulse['AnimationType']==0 and pulse['UseSpecificFromValue']==1 and pulse['FromUniqueValue']==1 and abs(pulse['ToUniqueValue']-.6)<1e-6,'serialized original pulse changed'
    for name in ['TownServiceQuietController.cs','TownServiceWindowMask.cs']:
        production[name]=(root/'src/GloomhavenVR/WorldUI/TownServices'/name).read_text()
        if name=='TownServiceQuietController.cs':production[name]='using GameAction = ScenarioRuleLibrary.GameAction;\n'+production[name].replace('new ShopService(', 'new MapRuleLibrary.Adventure.ShopService(')
    handoff=(root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementHandoff.cs').read_text()
    clear=method(handoff,'private void ClearNativeSelection()')
    holder_fields=handoff[handoff.index('    private static readonly FieldInfo? OriginalCardHolderField'):handoff.index('    internal static bool HasCurrentOffering')]
    production['NativeHandoffFixture.cs']='using System;using System.Reflection;using UnityEngine;namespace GloomhavenVR.WorldUI { public sealed class NativeHandoffFixture {\n'+holder_fields+'private readonly UINewEnhancementWindow _shop;private readonly UIWindow _window;public NativeHandoffFixture(UINewEnhancementWindow shop,UIWindow window){_shop=shop;_window=window;}public void Clear()=>ClearNativeSelection();\n'+clear+'\n} }'
    boundaries=(root/'scripts/town-quiet-controller-runtime/Boundaries.cs').read_text()
    boundaries=re.sub(r'\bShopService\?', 'MapRuleLibrary.Adventure.ShopService?', boundaries).replace('Init(ShopService service,','Init(MapRuleLibrary.Adventure.ShopService service,')
    boundaries=boundaries.replace('public class UINewEnhancementWindow : MonoBehaviour\n{','public class UINewEnhancementWindow : MonoBehaviour\n{\n    private UIEnchantressEffect? enhanctressEffect;\n    public UIEnhancementCardHighlighter? cardHolder;\n    public int Deselections;public bool ThrowSelect;\n    public void DeselectCurrentCard(){Deselections++;}')
    boundaries=boundaries.replace('public void OnSelectedCardToEnhance(AbilityCardUI? card){Selections++;selectedCard=card;previousSelectedCard=card;}','public void OnSelectedCardToEnhance(AbilityCardUI? card){Selections++;if(ThrowSelect)throw new Exception("live native selection failure");if(card==null&&cardHolder!=null)cardHolder.Hide();selectedCard=card;previousSelectedCard=card;}')
    production['ExplicitBoundaries.cs']=boundaries
    ritual=(root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceRitual.cs').read_text()
    assert 'TownServiceQuietController.SetOriginalEnhancementEffect(_window.GetComponent<UINewEnhancementWindow>(), native != null);' in method(ritual,'private void MaskDuplicateNativeCard()'),'original native effect lifecycle is installed on offered-card visibility'
    variants=[('production',None,None,'')]
    if not args.no_negative_controls:
        variants += [('missing-play','        Invoke(effect, "Play");','        /* native EnterShop Play omitted */','original effect Play starts original LeanTween'),('missing-stop','        if (effect != null) Invoke(effect, "Stop");','        /* original Stop omitted */','removed offering cancels original rotation and pulse'),('destroyed-canvas-callback','            if (holder == null || OriginalHolderGroupField != null\n                && OriginalHolderGroupField.GetValue(holder) as CanvasGroup == null) return;','            /* destroyed native CanvasGroup ignored */','destroyed native CanvasGroup callback escaped')]
    hashes={name:hashlib.sha256(value.encode()).hexdigest() for name,value in production.items()};hashes['Ritual.cs']=hashlib.sha256(ritual.encode()).hexdigest();hashes['Handoff.cs']=hashlib.sha256(handoff.encode()).hexdigest()
    for name in ['Program.cs','Lifecycle.csproj','Editor/InteractionRunner.cs','export-native.py']:hashes['fixture:'+name]=hashlib.sha256((fixture/name).read_bytes()).hexdigest()
    for dll in ['GH.Runtime.dll','GH.Runtime.FirstPass.dll']:hashes[dll]=hashlib.sha256((managed/dll).read_bytes()).hexdigest()
    (run/'source-hashes.json').write_text(json.dumps({'root':str(root),'native_effect':native,'sha256':hashes,'boundaries':['Deterministic elapsed time via original LeanTween manual clock; native shop/payment callbacks stubbed; no image/raycast or multiplayer hardware acceptance.']},indent=2)+'\n')
    manifest={'result':str(run/'results.txt'),'managed':str(managed.resolve()),'dependencies':str(managed.resolve()),'cases':[]}
    for name,before,after,expected in variants:
        build=run/name;source=build/'production';source.mkdir(parents=True)
        for filename,value in production.items():
            if before and before in value:
                assert value.count(before)==1,'mutation drift: '+name;value=value.replace(before,after,1)
            (source/filename).write_text(value)
        project=build/'Lifecycle.csproj';shutil.copyfile(fixture/'Lifecycle.csproj',project);assembly='NativeEnhancementLifecycle635_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(source),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed.resolve())],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit((result.stdout+result.stderr)[-8000:]+'\nCompilation failure is not passing evidence; '+str(build/'build.log'))
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir();shutil.copyfile(fixture/'Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs');(project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n');(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result=subprocess.run([str(unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(path),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    if (run/'results.txt').exists():print((run/'results.txt').read_text(),end='')
    if result.returncode or not (run/'results.txt').exists():raise SystemExit('FAIL original native effect lifecycle: '+str(run))
    print('PASS original native effect lifecycle635: '+str(len(variants))+' variants; '+str(run))
if __name__=='__main__':main()
