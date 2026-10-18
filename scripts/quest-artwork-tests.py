#!/usr/bin/env python3
"""Actual Unity texture-copy, region and changing portrait-cache qualification."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import signal
import struct
import time
import sys
import tempfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    end = source.index('\n    }\n', start) + len('\n    }\n')
    return source[start:end]


def reap_fixture_group(group):
    """Stop/reap this dedicated subreaper's fixture descendants before cleanup.

    Roslyn may create another session. A process-group-only wait would miss it;
    after Unity exits Linux reparents that orphan to this runner, not PID1.
    """
    reaped, adopted = [], set()
    for signum in (signal.SIGTERM, signal.SIGKILL):
        try:
            if group is not None: os.killpg(group, signum)
        except ProcessLookupError: pass
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            # This process launches only the one fixture; every adopted child
            # belongs to it. Recheck PPid immediately before signalling so no
            # shared compiler or other agent's process can become a target.
            for entry in Path("/proc").iterdir():
                if not entry.name.isdigit(): continue
                try:
                    stat = (entry / "stat").read_text().rsplit(")", 1)[1].split()
                    if int(stat[1]) != os.getpid(): continue
                    adopted.add(int(entry.name))
                    os.kill(int(entry.name), signum)
                except (FileNotFoundError, ProcessLookupError, PermissionError):
                    pass
            try:
                child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                return {"ownedProcessGroup": group, "adoptedChildren": sorted(adopted),
                        "reapedChildren": reaped, "remainingChildren": 0}
            if child:
                reaped.append({"pid": child, "status": status})
            else:
                time.sleep(.05)
    raise RuntimeError("Owned Unity fixture descendants could not be reaped before cleanup")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--graphics', choices=('vulkan', 'glcore'), default='vulkan')
    parser.add_argument('--case', action='append')
    parser.add_argument('--native-header-project', type=Path, help='Optional retained owned game project; copies only the native Brute header Sprite/texture and exact shader inputs.')
    parser.add_argument('--color-space', choices=('gamma', 'linear'), default='linear')
    parser.add_argument('--drop-guides-only', action='store_true', help='Run only the actual card drop-guide geometry, material and evidence graph.')
    parser.add_argument('--stat-portraits-only', action='store_true', help='Run only the native dual held-panel portrait snapshot graph.')
    parser.add_argument('--owned-card-pixels-only', action='store_true', help='Qualify exact owned GPU-only card RGBA, native mesh and minification; requires retained native inputs.')
    parser.add_argument('--native-board-probe', action='store_true', help='Measure both guide depth/geometry against the actual authored Steel board.')
    args = parser.parse_args()
    base = ROOT / '.planning/debug/quest-artwork'
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    log = run / 'runner.log'
    log.write_text('Quest artwork evidence: ' + str(run) + '\n')
    print(log.read_text(), end='', flush=True)
    sources = {}
    board_core = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.1.Core.cs').read_text()
    board_defaults = (ROOT / 'src/GloomhavenVR/Defaults/Defaults.Cards.cs').read_text()
    scale = re.search(r'internal const float SlotScale = ([^;]+);', board_core).group(1)
    wanted = {style: re.search(r'internal const float SlotOverlayScale_' + style + r' = ([^;]+);', board_defaults).group(1) for style in ('Oak', 'Steel', 'Bronze')}
    sources['NativeBoardContract.cs'] = 'internal static class NativeBoardContract { internal const float SlotScale = ' + scale + '; internal static float Wanted(string style) => style switch { ' + ', '.join('"' + style + '" => ' + value for style, value in wanted.items()) + ', _ => throw new System.ArgumentException(style) }; }\n'
    for relative in ('Cards/Art/CardFaceMipBake.cs', 'Cards/Art/QuestCardPixels.cs', 'Core/QuestTextureCopy.cs', 'Core/QuestPrimitiveMesh.cs', 'Cards/Art/CardGlow.cs', 'Cards/Art/QuestCardGuideWitness.cs', 'Cards/Art/QuestArtworkEvidence.cs', 'Cards/Art/QuestArtworkRegion.cs', 'Cards/Art/QuestCardAlpha.cs', 'Cards/Art/CardContour.cs'):
        path = ROOT / 'src/GloomhavenVR' / relative
        sources[path.name] = path.read_text()
    stat = (ROOT / 'src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs').read_text()
    sources['ActualStat.cs'] = '''using System.Collections.Generic; using UnityEngine; using UnityEngine.UI; using GloomhavenVR.Core;
namespace GloomhavenVR.WorldUI; internal static class ActualStat {
internal static readonly List<Texture2D> _copyOwnedTextures = new();
internal static readonly List<Sprite> _copyOwnedSprites = new();
internal static int Detach(ActorStatPanel original, GameObject copy) => DetachSharedImagery(original, copy);
''' + method(stat, '    private static int DetachSharedImagery(') + method(stat, '    private static string? PathUnder(') + method(stat, '    private static Vector2 NormalizedPivot(') + '\n}\n'
    panel = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelMipBake.cs').read_text()
    sources['RawPortrait.cs'] = '''using System.Collections.Generic; using GloomhavenVR.Cards; using GloomhavenVR.Core; using UnityEngine; using UnityEngine.UI;
namespace GloomhavenVR.WorldUI; internal static class PanelMipBake {
private static readonly Dictionary<int, Texture> s_rawOriginalByBaked = new(16);
private static readonly List<Image> ImageScratch = new();
private static readonly List<RawImage> RawScratch = new();
private static readonly HashSet<string> s_swapLogged = new();
private static bool s_errorLogged;
private static void EnsureArrivalWatch() {} // The frame pump is outside this focused static rescan graph.
internal static Texture OriginalFor(Texture texture) => s_rawOriginalByBaked.TryGetValue(texture.GetInstanceID(), out Texture original) && original != null ? original : texture;
''' + method(panel, '    internal static bool RefreshRawTexture(') + method(panel, '    internal static void Rescan(') + '\n}\n'
    capture = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs').read_text()
    sources['ActualPanelCopies.cs'] = '''using GloomhavenVR.Core; using UnityEngine;
namespace GloomhavenVR.WorldUI; internal static class ActualPanelCopies {
internal sealed class Entry { internal RenderTexture? Rt, MipRt; internal int ResolveSizeMismatch, MipCount, MotionResolves, StillResolves; internal bool Moving; }
private static RenderTexture DisplayTexture(Entry e) => e.MipRt != null ? e.MipRt : e.Rt!;
private static bool IsMoving(Entry e) => e.Moving;
private static float AskedMipLodOffset() => -.25f;
private static void ApplyMipLodOffset(RenderTexture target, float asked) => target.mipMapBias = asked;
internal static bool Carry(Entry e, RenderTexture? previous) => CarryOverInto(e, previous);
internal static void Resolve(Entry e, bool priming = false) => ResolveAndMip(e, priming);
''' + method(capture, '    private static bool CarryOverInto(') + method(capture, '    private static void ResolveAndMip(') + '\n}\n'
    face = (ROOT / 'src/GloomhavenVR/Cards/Art/CardFace.cs').read_text()
    slots = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.4.Slots.cs').read_text()
    nested = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.7.Nested.cs').read_text()
    sources['ActualWanted.cs'] = '''using GloomhavenVR.Core; using UnityEngine;
namespace GloomhavenVR.Cards; internal sealed class ActualWanted : MonoBehaviour {
private Transform? _visual = null;
internal readonly Transform?[] _slots = new Transform?[2];
internal readonly GameObject?[] _wantedHighlights = new GameObject?[2];
private const float WantedGlowBaseZ = -.004f;
private static float WantedGlowScale => 1.45f;
private static void AdoptFurniture(GameObject cue) {}
private static GameObject UnqualifiedQuad(string name, Transform parent, Vector3 scale, Vector3 position, Color color) {
var cue = GameObject.CreatePrimitive(PrimitiveType.Quad); cue.name = name;
Object.Destroy(cue.GetComponent<Collider>()); cue.transform.SetParent(parent, false);
cue.transform.localScale = scale; cue.transform.localPosition = position;
cue.GetComponent<MeshRenderer>().sharedMaterial = CardGlow.MakeGlowMaterial(color); cue.SetActive(false); return cue;
}
internal void Build() => BuildWantedHighlights();
''' + method(slots, '    private void BuildWantedHighlights(') + method(slots, '    internal string QuestGuideDrawState(') + method(nested, '    private sealed class SlotPulse') + '\n}\n'
    remote = (ROOT / 'src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs').read_text()
    board = (ROOT / 'src/GloomhavenVR/Net/Board/BoardVisual.cs').read_text()
    remote_pulse = remote[remote.index('internal sealed class RemoteGlowPulse : MonoBehaviour'):]
    assert remote_pulse.rstrip().endswith('}') and remote_pulse.count('\ninternal ') == 0
    sources['ActualRemoteGlow.cs'] = '''using UnityEngine;
namespace GloomhavenVR.Net;
internal sealed class ActualRemoteGlow : MonoBehaviour {
internal Transform _root = null!;
internal float _slotFrameW = .08255f, _slotFrameH = .1144f;
internal Material? Reported;
internal GameObject Build(string name, Vector3 local, float scale, float proud, Color color, bool pulse)
    => BuildSlotGlow(name, local, scale, proud, color, pulse);
private void ReportSlotGlowBlend(Material? material) => Reported = material;
private static GameObject UnqualifiedGlow(string name, Transform parent, Vector3 scale, Vector3 position, Color color, Material material) {
var mr = BoardVisual.Quad(parent, name, new Vector2(scale.x, scale.y), material);
mr.transform.localPosition = position; mr.gameObject.SetActive(false); return mr.gameObject;
}
''' + method(remote, '    private GameObject BuildSlotGlow(') + '\n}\n' + remote_pulse + '\ninternal static class BoardVisual {\n' + method(board, '    internal static MeshRenderer Quad(') + method(board, '    internal static Material Unlit(') + '\n}\n'
    constants = '\n'.join(re.findall(r'^    private const (?:float|int) (?:SilhouetteAttemptInterval|FootprintWidth|MinOutlineAreaFraction|BackdropAreaFraction|MinOutlineCoverage) = [^;]+;', face, re.M))
    sources['ActualSilhouette.cs'] = '''using System.Collections.Generic; using GloomhavenVR.Core; using UnityEngine; using UnityEngine.UI;
namespace GloomhavenVR.Cards; internal static class ActualSilhouette {
internal static object State() => new SilhouetteState { GaveUpLogged = false };
internal static void Capture(RectTransform root, object state) => TryCapture(root, CardBodyKind.Ability, (SilhouetteState)state);
internal static int Attempts(object state) => ((SilhouetteState)state).Attempts;
internal static string? Reason(object state) => ((SilhouetteState)state).LastReason;
''' + constants + method(face, '    private sealed class SilhouetteState') + method(face, '    private static void Reject(') + method(face, '    private static void TryCapture(') + method(face, '    private static bool DominatesUnknown(') + method(face, '    private static Rect DrawnLocalRect(') + method(face, '    private static bool IsFullyOpaque(') + method(face, '    private static Texture2D? ReadSpriteRegion(') + '\n}\n'
    variants = [
        ('production', '', '', '', ''),
        ('copy-source-missing', 'QuestTextureCopy.cs', 'Graphics.Blit(source, target, _material, 0);', 'Graphics.Blit(Texture2D.whiteTexture, target, _material, 0);', 'owned copy preserves repeated source/crop RGBA despite ambient camera and viewport'),
        ('copy-crop-missing', 'QuestTextureCopy.cs', 'new Vector4(scale.x, scale.y, offset.x, offset.y)', 'new Vector4(1, 1, 0, 0)', 'owned copy preserves repeated source/crop RGBA despite ambient camera and viewport'),
        ('readable-linear-encoding-lost', 'CardFaceMipBake.cs', 'linear: !GraphicsFormatUtility.IsSRGBFormat(src.graphicsFormat)', 'linear: false', 'readable whole-texture filtering retains original linear encoding'),
        ('readable-region-encoding-lost', 'CardFaceMipBake.cs', 'linear: QuestStandalonePlatform.Enabled && !GraphicsFormatUtility.IsSRGBFormat(atlas.graphicsFormat)', 'linear: false', 'readable region filtering retains original linear encoding'),
        ('readable-cpu-corrupted', 'CardFaceMipBake.cs', 'Color32[] pixels = src.GetPixels32(0);', 'Color32[] pixels = new Color32[src.width * src.height];', 'readable filtering preserves the exact current CPU source rather than stale GPU pixels'),
        ('gpu-only-filtered', 'CardFaceMipBake.cs', 'if (QuestStandalonePlatform.Enabled)\n        {\n            if (!src.isReadable) return QuestCardPixels.Read(src);', 'if (QuestStandalonePlatform.Enabled && src.isReadable)\n        {\n            if (!src.isReadable) return QuestCardPixels.Read(src);', 'Quest GPU-only original is never replaced by an unqualified readback'),
        ('label-is-content', 'CardFaceMipBake.cs', 'return QuestStandalonePlatform.Enabled ? $"{label}|instance={tex.GetInstanceID()}|update={tex.updateCount}" : label;', 'return label;', 'same-name atlas regions do not alias unrelated content'),
        ('raw-update-stale', 'RawPortrait.cs', 'if (!QuestStandalonePlatform.Enabled || original is not Texture2D live || live == null) return false;', 'if (QuestStandalonePlatform.Enabled || original is not Texture2D live || live == null) return false;', 'already baked raw portrait refreshes changed original contents'),
        ('raw-source-current-copy', 'RawPortrait.cs', 'source = live;', 'source = displayed;', 'already baked raw portrait refreshes changed original contents'),
        ('sprite-update-stale', 'CardFaceMipBake.cs', '&& update == source.texture.updateCount;', '&& true;', 'same-object source update invalidates preparation without GPU hash'),
        ('stale-camera-suppresses-evidence', 'QuestArtworkEvidence.cs', '&& _records < 8 &&', '&& Camera.current == null && _records < 8 &&', 'at most two actual artwork generations per role and eight total records'),
        ('inactive-source-silent', 'QuestArtworkEvidence.cs', 'if (image == null) { Missing(role, "image"); return; }\n            Sprite?', 'if (image == null || !image.isActiveAndEnabled) { Missing(role, "image"); return; }\n            Sprite?', 'Retained native header source and both pixel crops fit the protected Android record while parked' if args.native_header_project else 'inactive capture source and disabled image remain passively observable'),
        ('same-source-copy-corrupt', 'QuestArtworkEvidence.cs', 'string outputPixels = Sample(output, outputCrop, out string outputRead);', "string outputRead = \"gpu2d\"; string outputPixels = new string('A', sourcePixels.Length);", 'same source and crop retain repeat-copy consistency and actual texture format within the existing record'),
        ('overflow-drops-pixels', 'QuestArtworkEvidence.cs', 'message = "[Quest startup] artwork" + compact + " caller=" + _caller + samples;', 'message = "[Quest startup] artwork" + compact;', 'long native metadata retains paired pixel samples within the Android byte limit'),
        ('scenario-fallback-silent', 'QuestArtworkEvidence.cs', 'Image(chosen != null ? chosen.headerImage : null, "ability-header");', ';', 'scenario callback reports missing native sources without an adoption callback'),
        ('game-conditional-erases-pixels', 'QuestArtworkEvidence.cs', 'UnityEngine.Debug.Log(message);', 'Debug.Log(message);', 'Actual retained native header emits one bounded source record' if args.native_header_project else 'scenario census reaches actual native header without adoption callback'),
        ('game-conditional-erases-state', 'QuestArtworkEvidence.cs', '=> UnityEngine.Debug.Log("[Quest startup] artwork" + detail', '=> Debug.Log("[Quest startup] artwork" + detail', 'scenario callback reports missing native sources without an adoption callback'),
        ('parked-template-hides-live-source', 'QuestArtworkEvidence.cs', '(face.gameObject.activeInHierarchy ? 16 : 0)', '(face.gameObject.activeInHierarchy ? 0 : 16)', 'scenario census reaches actual native header without adoption callback'),
        ('silhouette-gpu-only-trusted', 'QuestArtworkRegion.cs', 'if (QuestStandalonePlatform.Enabled)\n        {\n            if (source is not Texture2D image || !image.isReadable)', 'if (QuestStandalonePlatform.Enabled && source is Texture2D readable && readable.isReadable)\n        {\n            if (source is not Texture2D image || !image.isReadable)', 'actual contour caller never writes a mask from GPU-only original alpha'),
        ('silhouette-partial-union-trusted', 'ActualSilhouette.cs', 'if (DominatesUnknown(alpha, fw, fh, img, norm, sprite, crop))', 'if (alpha.Length > 0)', 'actual contour caller never writes a mask from GPU-only original alpha'),
        ('silhouette-crop-lost', 'QuestArtworkRegion.cs', 'System.Array.Copy(original, (y + row) * image.width + x, slice, row * width, width);', 'System.Array.Copy(original, (y + row) * image.width, slice, row * width, width);', 'silhouette crop preserves exact native alpha'),
        ('owned-alpha-crop-lost', 'QuestCardAlpha.cs', 'alpha[(y + row) * entry.Width + x + column]', 'alpha[row * entry.Width + column]', 'owned alpha crops exact original mip-zero bytes without a GPU readback'),
        ('owned-alpha-dimensions-ignored', 'QuestCardAlpha.cs', '|| entry.Width != source.width || entry.Height != source.height)', ')', 'owned alpha refuses mismatched native dimensions instead of guessing a resized contour'),
        ('owned-alpha-unavailable', 'QuestArtworkRegion.cs', 'return QuestCardAlpha.Read(source, x, y, width, height);', 'return null;', 'owned CPU alpha is available for a GPU-only native texture'),
        ('owned-alpha-interior-unconditionally-refused', 'ActualSilhouette.cs', 'if (DominatesUnknown(alpha, fw, fh, img, norm, sprite, crop))', 'if (alpha.Length < 0)', 'known native alpha proves an unreadable interior cannot change the ordinary union'),
        ('owned-alpha-cache-admits-gpu', 'QuestArtworkRegion.cs', '".quest-owned-cpu-alpha-v1"', '"." + QuestTextureCopy.Recipe', 'Quest silhouette cache is isolated from old GPU-derived masks'),
        ('drop-glow-owned-upload-missing', 'CardGlow.cs', 'QuestPrimitiveMesh.Rebuild(filter.sharedMesh, "card-drop-glow")', 'null', 'actual wanted-slot consumer owns explicit white COLOR on Quest'),
        ('wanted-glow-reverts-native-quad', 'ActualWanted.cs', 'GameObject quad = CardGlow.CreateGlowQuad("WantedHighlight", slot!,', 'GameObject quad = UnqualifiedQuad("WantedHighlight", slot!,', 'actual wanted-slot consumer owns explicit white COLOR on Quest'),
        ('drop-glow-additive-missing', 'CardGlow.cs', 'm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);', 'm.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);', 'drop cues retain additive light over the existing board'),
        ('drop-glow-release-missing', 'CardGlow.cs', 'if (_mesh != null) Object.Destroy(_mesh);', ';', 'owned cue mesh is released with its visual'),
        ('alpha-provenance-missing', 'QuestArtworkEvidence.cs', '+ " contour=" + _abilityContour', '+ ""', 'existing bounded native artwork record names alpha delivery and actual contour outcome'),
        ('remote-drop-glow-owned-upload-missing', 'ActualRemoteGlow.cs', 'Cards.CardGlow.CreateGlowQuad(name, _root,', 'UnqualifiedGlow(name, _root,', 'actual mirrored slot guide owns the same explicit Quest vertex inputs'),
        ('guide-determinant-evidence-missing', 'ActualWanted.cs', '+ ",det=" + number(go.transform.localToWorldMatrix.determinant)', '+ ",missingDet=" + number(go.transform.localToWorldMatrix.determinant)', 'actual left/right guide facts retain transform determinant and independent material state'),
        ('stat-copy-cpu-stale', 'ActualStat.cs', 'if (QuestStandalonePlatform.Enabled && !tex.isReadable)', 'if (QuestStandalonePlatform.Enabled && tex.isReadable)', 'actual panel mip rescan preserves native frozen portrait GPU pixels'),
        ('stat-shared-texture', 'ActualStat.cs', 'img!.sprite = rebuilt;', 'img!.sprite = sprite;', 'frozen portrait owns its source texture independently of the native loader'),
        ('guide-sampler-unbound', 'QuestCardGuideWitness.cs', 'material.mainTexture = Texture2D.whiteTexture;', 'material.mainTexture = null;', 'actual Quest guide explicitly binds its authored white sampler independently of previous native draws'),
        ('guide-sampler-replaces-imagery', 'QuestCardGuideWitness.cs', '&& material.HasProperty("_MainTex") && material.mainTexture == null)', '&& material.HasProperty("_MainTex"))', 'Quest cue binding never replaces supplied imagery or writes a global texture'),
        ('guide-submission-silent', 'QuestCardGuideWitness.cs', 'Submitted = true;', 'Submitted = false;', 'actual head-camera submission updates guide evidence after culling'),
        ('owned-pixels-missing', 'CardFaceMipBake.cs', 'if (!src.isReadable) return QuestCardPixels.Read(src);', 'if (!src.isReadable) return null;', 'owned GPU-only card receives exact mip filtering without XR readback'),
        ('owned-pixels-rgb-swapped', 'QuestCardPixels.cs', 'new Color32(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3])', 'new Color32(rgba[at + 2], rgba[at + 1], rgba[at], rgba[at + 3])', 'owned GPU-only card receives exact original RGBA before mip generation'),
        ('owned-pixels-mips-missing', 'CardFaceMipBake.cs', 'if (!ApplyQuestLinearMips(copy, pixels, src))', 'if (true)', 'owned filtering reduces actual distance and oblique minification error'),
    ]
    if args.case:
        variants = [row for row in variants if row[0] in args.case]
        if len(variants) != len(set(args.case)): parser.error('Unknown/duplicate case')
    fixture = ROOT / 'tests/QuestArtwork.Tests'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    code = 1
    process = None
    # Adopt only this runner's orphaned compiler/editor descendants. A completed
    # Unity parent can otherwise leave Roslyn pinning already deleted cache files.
    if sys.platform.startswith('linux'):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), 'Could not own Unity fixture child cleanup')
    try:
        for name, filename, before, after, expected in variants:
            case = run / name
            production = case / 'production'; production.mkdir(parents=True)
            for path, source in sources.items():
                if path == filename:
                    assert source.count(before) == 1, name
                    source = source.replace(before, after, 1)
                (production / path).write_text(source)
            project = case / 'Runtime.csproj'; shutil.copyfile(fixture / 'Runtime.csproj', project)
            assembly = 'QuestArtwork_' + name.replace('-', '_')
            build = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '--disable-build-servers', '-p:UseSharedCompilation=false',
                '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ROOT / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')], capture_output=True, text=True)
            (case / 'build.log').write_text(build.stdout + build.stderr)
            if build.returncode: raise RuntimeError('Fixture compilation failed: ' + str(case / 'build.log'))
            manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        project = run / 'unity'
        for folder in ('Assets/Plugins', 'Assets/Editor', 'Assets/Resources', 'Packages', 'ProjectSettings'): (project / folder).mkdir(parents=True, exist_ok=True)
        for case in manifest['cases']:
            target = project / 'Assets/Plugins' / Path(case['dll']).name
            shutil.copyfile(case['dll'], target); case['dll'] = str(target)
        shutil.copyfile(ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs', project / 'Assets/Editor/DiagnosticRunner.cs')
        shutil.copyfile(ROOT / 'unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader', project / 'Assets/Resources/QuestTextureCopy.shader')
        shutil.copyfile(ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader', project / 'Assets/Resources/PrimitiveOverlay.shader')
        if args.native_board_probe:
            source_folder = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table'
            boards = [('PlayTray_9capjqp6', 'Steel'), ('PlayTray', 'Oak'), ('PlayTray_16vm268h', 'Bronze')]
            files = [(model + '.prefab', 'NativeBoard' + style + '.prefab') for model, style in boards]
            files += [(model + '.fbx', 'NativeBoardModel' + style + '.fbx') for model, style in [('PlayTray_9capjqp6', 'Steel'), ('PlayTray_prepped', 'Oak'), ('PlayTray_16vm268h', 'Bronze')]]
            board_inputs = []
            for source, destination in files:
                for suffix in ('', '.meta'):
                    shutil.copyfile(source_folder / (source + suffix), project / 'Assets/Resources' / (destination + suffix))
                    original = source_folder / (source + suffix)
                    board_inputs.append({'source': str(original), 'sha256': hashlib.sha256(original.read_bytes()).hexdigest(), 'fixturePath': 'Assets/Resources/' + destination + suffix})
            # Exercise the actual BoardLit/material path; the old witness's
            # uniform Overlay replacement could not qualify native vertex/depth state.
            dependency_guids = set()
            for model, style in boards:
                material = source_folder / (model + '.mat')
                for suffix in ('', '.meta'):
                    original = Path(str(material) + suffix)
                    destination = project / 'Assets/Resources' / ('NativeBoardMaterial' + style + '.mat' + suffix)
                    shutil.copyfile(original, destination)
                    board_inputs.append({'source': str(original), 'sha256': hashlib.sha256(original.read_bytes()).hexdigest(), 'fixturePath': str(destination.relative_to(project))})
                dependency_guids.update(re.findall(r'guid: ([0-9a-f]{32})', material.read_text()))
            for meta in source_folder.rglob('*.meta'):
                match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(), re.M)
                if not match or match.group(1) not in dependency_guids: continue
                original_asset = meta.with_suffix('')
                if original_asset.suffix not in ('.shader', '.png', '.jpg', '.tga'): continue
                for original in (original_asset, meta):
                    destination = project / 'Assets/Resources' / original.name
                    shutil.copyfile(original, destination)
                    board_inputs.append({'source': str(original), 'sha256': hashlib.sha256(original.read_bytes()).hexdigest(), 'fixturePath': str(destination.relative_to(project))})
                dependency_guids.remove(match.group(1))
            if dependency_guids: raise RuntimeError('Native board material dependencies unresolved: ' + str(dependency_guids))
            (run / 'board-inputs.json').write_text(json.dumps({'inputs': board_inputs, 'core': hashlib.sha256(board_core.encode()).hexdigest(), 'defaults': hashlib.sha256(board_defaults.encode()).hexdigest(), 'slotScale': scale, 'wantedScale': wanted}, indent=2) + '\n')
        if args.native_header_project:
            retained = args.native_header_project.resolve()
            paths = {
                'Assets/QuestRecoveredBundles/62ef3d424e9cc164580eaaf46c2f35bf/AC_Brute_Background.asset': 'Assets/Resources/NativeHeader.asset',
                'Assets/QuestRecoveredBundles/019ed361c43574148b95d6abd6457479/AC_Brute_Background.png': 'Assets/Resources/NativeHeaderTexture.png',
                'Assets/QuestRecoveredBundles/30a84ac1a80cfc34e841ad97d8f9b8fe/Boots_of_Striding.asset': 'Assets/Resources/NativeItem.asset',
                'Assets/QuestRecoveredBundles/034fdd42cbcbb5545b9013c6db0e38f2/Boots_of_Striding.png': 'Assets/Resources/NativeItemTexture.png',
                'Assets/Shader/GUI_AbilityCard_Shd.shader': 'Assets/Resources/NativeHeaderShader.shader',
                'Assets/Sprite/Portrait_Brute.asset': 'Assets/Resources/NativeStatPortrait.asset',
                'Assets/Texture2D/Portrait_Brute.png': 'Assets/Resources/NativeStatPortraitTexture.png',
                'Assets/_AssetBundles/characterportraits/brute.asset': 'Assets/Resources/NativeInitiativePortrait.asset',
                'Assets/_AssetBundles/characterportraits/brute.png': 'Assets/Resources/NativeInitiativePortraitTexture.png',
            }
            shader_source = (retained / 'Assets/Shader/GUI_AbilityCard_Shd.shader').read_text()
            for include in set(re.findall(r'#include "(Assets/QuestOriginalCampaign/ShaderPrograms/[^"\n]+)"', shader_source)):
                paths[include] = include
            owned_inputs = []
            for source, destination in paths.items():
                src, dst = retained / source, project / destination
                dst.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(src, dst)
                if src.with_name(src.name + '.meta').is_file(): shutil.copyfile(src.with_name(src.name + '.meta'), dst.with_name(dst.name + '.meta'))
                owned_inputs.append({'source': str(src), 'sha256': hashlib.sha256(src.read_bytes()).hexdigest(), 'fixturePath': destination})
            # The original PNG is a read-only local hardware input, never a
            # tracked/distributed test asset. Its exact bottom-up CPU alpha also
            # qualifies the actual native Sprite/crop through the new sidecar.
            from PIL import Image
            alpha_sources = [
                ('NativeHeaderTexture', 'Assets/QuestRecoveredBundles/019ed361c43574148b95d6abd6457479/AC_Brute_Background.png'),
                ('NativeItemTexture', 'Assets/QuestRecoveredBundles/034fdd42cbcbb5545b9013c6db0e38f2/Boots_of_Striding.png'),
            ]
            payload = b'GHVA1' + struct.pack('<i', len(alpha_sources))
            for label, relative in alpha_sources:
                with Image.open(retained / relative) as image:
                    alpha = image.convert('RGBA').getchannel('A').transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
                    width, height = image.size
                codec = zlib.compressobj(wbits=-15)
                compressed = codec.compress(alpha) + codec.flush()
                name = label.encode('utf-8')
                payload += struct.pack('<i', len(name)) + name + struct.pack('<iii', width, height, len(compressed)) + compressed
            (project / 'Assets/Resources/NativeCardAlpha.bytes').write_bytes(payload)
            (run / 'owned-inputs.json').write_text(json.dumps(owned_inputs, indent=2) + '\n')
            if args.owned_card_pixels_only:
                spec = importlib.util.spec_from_file_location('owned_pixels_fixture', ROOT / 'tools/quest-builder/quest_card_pixels.py')
                pixels = importlib.util.module_from_spec(spec); spec.loader.exec_module(pixels)
                def entries():
                    for label, relative in alpha_sources:
                        with Image.open(retained / relative) as image:
                            rgba = image.convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
                            yield label, image.width, image.height, 1, rgba
                    image = Image.new('RGBA', (512, 512))
                    image.putdata([(255 if (x + y) % 2 else 0, (x * 17 + y * 3) % 256, 41, 255) for y in range(512) for x in range(512)])
                    image.save(project / 'Assets/Resources/OwnedSamplingPattern.png')
                    (project / 'Assets/Resources/OwnedSamplingPattern.png.meta').write_text('fileFormatVersion: 2\nguid: 7282a34680364f069f84602675666755\nTextureImporter:\n  serializedVersion: 12\n  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n  isReadable: 0\n  textureFormat: 1\n  maxTextureSize: 4096\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n  textureType: 0\n  textureShape: 1\n  compressionQuality: 100\n  textureCompression: 0\n')
                    rgba = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
                    (project / 'Assets/Resources/OwnedPatternExpected.bytes').write_bytes(rgba)
                    yield 'OwnedSamplingPattern', 512, 512, 1, rgba
                pixels.write_entries(entries(), project / 'Assets/Resources/NativeCardPixels.bytes')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: ' + ('1' if args.color_space == 'linear' else '0') + '\n')
        manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'source-hashes.json').write_text(json.dumps({'sources': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()},
            'panelSource': hashlib.sha256(panel.encode()).hexdigest(), 'silhouetteSource': hashlib.sha256(face.encode()).hexdigest(), 'captureSource': hashlib.sha256(capture.encode()).hexdigest(), 'graphics': args.graphics, 'colorSpace': args.color_space,
            'hardwareVerified': False}, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-' + args.graphics, '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        environment = dict(os.environ)
        if args.drop_guides_only: environment['GHVRQ_TEST_DROP_GUIDES_ONLY'] = '1'
        if args.stat_portraits_only: environment['GHVRQ_TEST_STAT_PORTRAITS_ONLY'] = '1'
        if args.owned_card_pixels_only:
            if not args.native_header_project: parser.error('Owned card pixels proof requires retained native inputs')
            environment['GHVRQ_TEST_OWNED_CARD_PIXELS_ONLY'] = '1'
        if args.native_board_probe: environment['GHVRQ_TEST_NATIVE_BOARD_PROBE'] = '1'
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True, env=environment)
        code = process.wait(timeout=240)
        result = Path(manifest['result'])
        if result.is_file(): print(result.read_text(), end='', flush=True)
        if code or not result.is_file(): raise RuntimeError('Actual Unity artwork fixture failed: ' + str(run / 'unity.log'))
        if args.native_board_probe:
            for entry in board_inputs:
                if hashlib.sha256(Path(entry['source']).read_bytes()).hexdigest() != entry['sha256']:
                    raise RuntimeError('Read-only native board source changed: ' + entry['source'])
        print('PASS Quest artwork ' + args.graphics + ': ' + str(len(variants)) + ' production/causal variants', flush=True)
    finally:
        if process is not None and process.poll() is None: process.kill(); process.wait()
        children = reap_fixture_group(process.pid if process is not None else None)
        (run / 'process-cleanup.json').write_text(json.dumps(children, indent=2) + '\n')
        cleanup = cleanup_completed_run('quest-artwork', code, log, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': main()
