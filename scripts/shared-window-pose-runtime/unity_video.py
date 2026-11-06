"""Real Unity companion for the video coordinator/initial-adoption/frame-writer seam.

The native movie decoder, roster and discovery are explicit ports. Its complete playback
coordinator, movie keys, video receiver, shared move/context/scale codecs, pose resolver,
NativeVideo first-reveal, GrabbableModal pose/host writers and camera pixels run as source.
"""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import signal


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log, timeout=240):
    with log.open('w') as stream:
        child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def run_unity_video(root, run, sources, fixtures, original, hashes):
    import hashlib
    native_path = root / 'src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.SharedPlacement.cs'
    native = native_path.read_text()
    geometry_path = root / 'scripts/shared-window-geometry-runtime/Boundary.cs'
    geometry = geometry_path.read_text()
    grab_path = root / 'src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs'
    grab = grab_path.read_text()
    size_path = root / 'src/GloomhavenVR/WorldUI/Modal/SharedWindowSize.cs'
    size = size_path.read_text()
    for path in (Path(__file__), geometry_path, grab_path, size_path, native_path,
                 root / 'scripts/shared-window-pose-runtime/Unity/VideoUnityCases.cs'):
        hashes[str(path.relative_to(root))] = hashlib.sha256(path.read_bytes()).hexdigest()
    # Only Unity primitives and native decoder are substituted in the transport boundary.
    boundary = 'using UnityEngine;\n' + fixtures['Boundary.cs']
    boundary = boundary.replace(block(boundary, 'namespace UnityEngine\n'), '')
    decoder = block(boundary, 'namespace UnityEngine.Video')
    boundary = boundary.replace(decoder, decoder.replace('namespace UnityEngine.Video', 'namespace GloomhavenVR.VideoPoseFixture')
                                .replace('VideoPlayer : Component', 'VideoPlayer : UnityEngine.MonoBehaviour'))
    boundary = boundary.replace('using UnityEngine.Video;', 'using GloomhavenVR.VideoPoseFixture;')
    boundary = boundary.replace('Array.Clear(ReflowWindows);Array.Clear(ReflowIncluded);',
        'Array.Clear(ReflowWindows,0,ReflowWindows.Length);Array.Clear(ReflowIncluded,0,ReflowIncluded.Length);')
    boundary = boundary.replace('internal readonly record struct PeerInfo(int Build);',
        'internal readonly struct PeerInfo { internal readonly int Build; internal PeerInfo(int value) { Build=value; } }')
    boundary = boundary.replace('internal readonly record struct RosterMember(int Id);',
        'internal readonly struct RosterMember { internal readonly int Id; internal RosterMember(int value) { Id=value; } }')
    boundary = boundary.replace('        internal static InitialPoseReader? TryInitialPose { get; set; }',
        '        internal static InitialPoseReader? TryInitialPose { get; set; }\n        internal static bool IsInitialAuthor => InitialAuthor?.Invoke() == true;')
    # Copy the existing Unity geometry boundary declarations rather than inventing another host.
    physical = block(geometry, '    internal sealed partial class GrabbableModal')
    physical = physical.replace('        internal bool IsGrabbed => _handle.IsGrabbed;', '''        internal bool IsGrabbed { get => _handle.IsGrabbed; set => _handle.IsGrabbed = value; }
        internal Transform Root => _frame!;
        internal bool Visible { get => !_panel.RenderHidden && !_panel.RevealPending; set { _panel.RenderHidden = !value; } }
        bool IPanelGrabOwner.GrabVisible => Visible;
        internal void YieldSharedControl() { _handle.IsGrabbed = false; }
        internal void PlaceRoomMakingSample(Vector3 position, Quaternion rotation) { PlaceFrameAt(position,rotation); }
        internal ConvertedPanel Panel => _panel;
        internal GrabbableModal()
        {
            var root = new GameObject("native movie canvas",typeof(RectTransform),typeof(UnityEngine.Canvas));
            var canvas = root.GetComponent<UnityEngine.Canvas>(); canvas.renderMode=UnityEngine.RenderMode.WorldSpace;
            var rect = root.GetComponent<RectTransform>(); rect.sizeDelta=new Vector2(400,240);
            var ink = new GameObject("decoded movie pixels",typeof(RectTransform),typeof(UnityEngine.UI.Image));
            var inkRect=ink.GetComponent<RectTransform>(); inkRect.SetParent(root.transform,false); inkRect.sizeDelta=rect.sizeDelta;
            ink.GetComponent<UnityEngine.UI.Image>().color=new Color(.12f,.85f,.25f,1);
            _panel=new ConvertedPanel { HostGo=root,HostRect=rect,Target=inkRect,SharedDesignKind=SharedWindowKind.Video,SharedDesignFrame=rect.sizeDelta };
            Build(_panel,1f,"video coordinator physical frame"); Participation(true);
        }''')
    boundary = boundary.replace(block(boundary, '    internal sealed class GrabbableModal'), physical)
    names = ['internal sealed class ConvertedPanel', 'internal sealed class Dial', 'internal static class WorldUIConfig',
             'internal static class Resources', 'internal static class PanelGrabHandle', 'internal sealed class FixtureHandle',
             'internal sealed class FixtureTween', 'internal static class PanelPoseWatch']
    declarations = '\n'.join(block(geometry, '    '+name) for name in names)
    boundary += '\nnamespace GloomhavenVR.WorldUI {\n'+declarations+'\n}\n'
    boundary += '\nnamespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static float BaseWorldScale=12f; } }\n'
    boundary += 'internal static class UnityGameEditorRuntime { internal static UnityEngine.Vector3 s_TileSize=new(1.8f,0,1f); }\n'
    # The actual current physical panel controls the guard and first-reveal; remote pose values
    # are only legacy cache storage, exactly as in NativeVideoWindow.SetRemotePose.
    boundary = boundary.replace('internal static class NativeVideoWindow', 'internal static partial class NativeVideoWindow')
    boundary = boundary.replace('        internal static bool SharedInitialPoseReady => ModalFallback.Ready;', '''        private static GameObject? _root;
        private static ConvertedPanel? _panel;
        private static GrabbableModal? _grab;
        internal static void BindPhysical(GrabbableModal grab)
        { _grab=grab;_panel=grab.Panel;_root=_panel.HostGo;PrepareSharedPlacement(); }
        internal static bool RevealPhysical() => TryRevealSharedPlacement();''')
    boundary += '''\nnamespace GloomhavenVR.Hands.Interact { internal static class FixtureNamespace { } }
namespace GloomhavenVR.WorldUI {
    internal static partial class CanvasConversion {
        internal static UnityEngine.Camera? WorldCamera;
        internal static void SetPanelRenderVisible(ConvertedPanel panel,bool visible) {
            panel.RenderHidden=!visible; foreach(var graphic in panel.HostGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.enabled=visible;
        }
    }
    internal static class PanelPlacement {
        internal static void Spawn(UnityEngine.Camera head,float scale,out UnityEngine.Vector3 position,out UnityEngine.Quaternion rotation) {
            position=head.transform.position+head.transform.forward*scale;rotation=head.transform.rotation;
        }
    }
}
internal static class FixtureClock { internal static float unscaledTime;internal static int frameCount; }
'''
    # Replace portable scale storage with the same actual geometry scale consumer used by modal Tick.
    shared_size = block(boundary, '    internal static class SharedWindowSize')
    shared_size = shared_size.replace('internal static class SharedWindowSize', 'internal static partial class SharedWindowSize')
    shared_size = shared_size.replace('        internal static float ReferenceWorldScale=12;', '''        private const float MinDesignPx=64f, StretchTolerancePx=64f;
        private static readonly System.Collections.Generic.HashSet<string> ArmLogged=new();
        internal static void ServiceResize(SharedWindowKind kind,string name,Vector2 rect,float factor,bool held) { }''')
    boundary=boundary.replace(block(boundary, '    internal static class SharedWindowSize'),shared_size)
    current=dict(sources)
    current['Boundary.cs']=boundary
    current['NativeVideoPlacement.cs']=native
    current['PhysicalGrab.cs']='''using System;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;
namespace GloomhavenVR.WorldUI { internal sealed partial class GrabbableModal {
'''+ '\n'.join(block(grab,sig) for sig in ('internal void Build(', 'internal void Tick()', 'internal void LateSyncHost()',
    'internal void ApplySharedReferenceWorldScale(', 'internal void PlaceFrameAt(', 'private void SyncHostToFrame(',
    'private float ResolveGrabFactor()', 'internal void SetExtraScale(', 'private void PinVisualToFrame(', 'private void WriteVisual()',
    'internal void SnapFrameTo('))+'\n'+re.search(r'internal float SharedReferenceWorldScale => [^;]+;',grab)[0]+'\n}}'
    rendered_size='\n'.join([block(size,'internal static float ReferenceWorldScale'),block(size,'private static float NativeHexReferenceWorldScale()')])
    for name in ('NativeHexScale','NativeHexReadAttempted'):
        rendered_size+='\n'+re.search(r'private static (?:float|bool) '+name+r' = [^;]+;',size)[0]
    for name in ('UsesSharedWorldFrame','RenderWorldScale'):
        rendered_size+='\n'+re.search(r'internal static (?:bool|float) '+name+r'\([\s\S]*?;',size)[0]
    current['PhysicalSize.cs']='using System;using UnityEngine;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI { internal static partial class SharedWindowSize {\n'+rendered_size+'\n}}'
    helpers=fixtures['VideoPoseCases.cs']
    current['VideoCaseHelpers.cs']='using System;using UnityEngine;using GloomhavenVR.Net;using GloomhavenVR.WorldUI;\ninternal static class VideoCaseHelpers {\n'+ '\n'.join(block(helpers,sig).replace('private static','internal static',1) for sig in ('private static PresenceState Packet(', 'private static void Receive(', 'private static void Tick('))+'\n}'
    current['VideoUnityCases.cs']=(root/'scripts/shared-window-pose-runtime/Unity/VideoUnityCases.cs').read_text()
    proof=block(fixtures['Program.cs'],'internal static class Proof')
    proof=proof.replace('Enum.GetValues<SharedWindowKind>()','(SharedWindowKind[])Enum.GetValues(typeof(SharedWindowKind))')
    current['Proof.cs']='using System;using UnityEngine;using GloomhavenVR.Net;using GloomhavenVR.WorldUI;using GloomhavenVR.WorldUI.MapRoom;\n'+proof
    for name,text in list(current.items()):
        current[name]='using Time = FixtureClock;\n'+text.replace('using Time = FixtureClock;', '').replace('using UnityEngine.Video;', 'using GloomhavenVR.VideoPoseFixture;')
    variants=[('production',''),('repeated-initial','revealed mirror moves actual Unity video pixels')]
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
    managed=unity.parent/'Data/Managed';game=root/'ressources/GH_Data/Managed'
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    manifest={'result':str(run/'unity-results.txt'),'cases':[]}
    for name,expected in variants:
        case=run/('unity-'+name);generated=case/'Sources';generated.mkdir(parents=True)
        for filename,text in current.items():
            if name=='repeated-initial' and filename=='VideoPlayback.cs':
                before='if (!NativeVideoWindow.SharedInitialPoseReady\n            && RemoteMapStory.TryVideoSharedInitialPose('
                assert text.count(before)==1
                text=text.replace(before,'if (RemoteMapStory.TryVideoSharedInitialPose(',1)
            (generated/filename).write_text(text)
        assembly='VideoPose_'+name.replace('-','_')
        project=case/'VideoPose.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><GenerateDocumentationFile>false</GenerateDocumentationFile><AllowUnsafeBlocks>true</AllowUnsafeBlocks><NoWarn>CS0649;CS0169</NoWarn><AssemblyName>'+assembly+'</AssemblyName></PropertyGroup><ItemGroup><Reference Include="'+str(managed)+'/UnityEngine/*.dll"/><Reference Include="UnityEngine.UI" HintPath="'+str(game)+'/UnityEngine.UI.dll"/></ItemGroup></Project>\n')
        if execute([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false'],case/'build.log',120):
            raise RuntimeError('Unity video companion compilation failed: '+str(case/'build.log'))
        manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/f'{assembly}.dll'),'expected':expected})
    editor=run/'unity'
    for path in ('Assets/Editor','Assets/Plugins','Packages','ProjectSettings'):(editor/path).mkdir(parents=True,exist_ok=True)
    for case in manifest['cases']:
        target=editor/'Assets/Plugins'/Path(case['dll']).name;shutil.copyfile(case['dll'],target);case['dll']=str(target)
    shutil.copyfile(root/'scripts/shared-ui-window-runtime/Editor/SharedUiRunner.cs',editor/'Assets/Editor/SharedUiRunner.cs')
    (editor/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.video":"1.0.0"}}\n')
    (editor/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path=run/'unity-manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    status=execute(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-adb2','-adb2-port','5083','-projectPath',str(editor),'-executeMethod','SharedUiRunner.Start','-interactionManifest',str(path),'-logFile',str(run/'unity.log')],run/'unity-launch.log')
    if (run/'unity-results.txt').exists(): print((run/'unity-results.txt').read_text(),flush=True)
    for name, expected in hashes.items():
        path = root / name
        if not path.exists(): path = root / 'src/GloomhavenVR' / name
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise RuntimeError('Unity video proof input changed during execution: ' + name)
    return status
