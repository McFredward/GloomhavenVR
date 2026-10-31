#!/usr/bin/env python3
"""Execute production level-up composition with the real game widget in Unity.

Game session/owner fields and the existing conversion service are explicit ports.
Actual shipped ExtendedButton/UI/TMP, native sprite/font data, event dispatch,
Unity scene lifetime, owner matrices and GPU pixels execute unchanged. This is
not a native scenario bootstrap, HMD, multiplayer or hardware performance test.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    'LevelUpComposite.cs': 'Composites/LevelUpComposite.cs',
    'CardListEntry.cs': 'Composites/CardListEntry.cs',
    'MapDialogSeat.cs': 'Composites/MapDialogSeat.cs',
    'MovedSubtreeLayers.cs': 'Composites/MovedSubtreeLayers.cs',
    'ConfirmationOnOwner.cs': 'Composites/ConfirmationOnOwner.cs',
    'ConfirmationOnOwner.Origins.cs': 'Composites/ConfirmationOnOwner.Origins.cs',
    'ConfirmationOnOwner.Navigation.cs': 'Composites/ConfirmationOnOwner.Navigation.cs',
    'ConfirmationOnOwner.Identity.cs': 'Composites/ConfirmationOnOwner.Identity.cs',
    'ConfirmationOnOwner.NavigationState.cs': 'Composites/ConfirmationOnOwner.NavigationState.cs',
    'ConfirmationOnOwner.Deferred.cs': 'Composites/ConfirmationOnOwner.Deferred.cs',
    'AnnouncementContinue.cs': 'Modal/AnnouncementContinue.cs',
    'AnnouncementContinueView.cs': 'Modal/AnnouncementContinueView.cs',
    'DialogSurface.cs': 'Surfaces/DialogSurface.cs',
    'PanelGraphicMaterial.cs': 'Sharpness/PanelGraphicMaterial.cs',
    'NarrativeBackdrop.cs': 'Modal/NarrativeBackdrop.cs',
}


def method(source, signature):
    if source.count(signature) != 1: raise RuntimeError('Source binding drift: ' + signature)
    start = source.index(signature); end = source.index('{', start) + 1; depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]


def main():
    def terminated(signum, frame): raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, terminated)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/levelup-native-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case', action='append')
    parser.add_argument('--card-list-only', action='store_true', help='Run only the native closed-card-list proof; requires explicit --case selection.')
    parser.add_argument('--narrative-only', action='store_true', help='Run the native narrative background subset; not a complete level-up suite.')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use the private task ADB_SERVER_SOCKET; a default shared ADB server is not permitted.')
    root = args.source_root.resolve(); fixture = ROOT / 'scripts/levelup-native-runtime'
    originals = {name: (root / 'src/GloomhavenVR/WorldUI' / relative).read_text() for name, relative in FILES.items()}
    originals['Loc.CardList.cs'] = (root / 'src/GloomhavenVR/Core/Loc/Loc.CardList.cs').read_text()
    quest_copy = root / 'src/GloomhavenVR/Core/QuestTextureCopy.cs'
    quest_sources = [quest_copy] if quest_copy.is_file() else []
    for file in quest_sources:
        originals[file.name] = file.read_text()
    modal = root / 'src/GloomhavenVR/WorldUI/Modal'
    screen = (modal / 'ModalFallback.12.ScreenBind.cs').read_text()
    conversion = (modal / 'ModalFallback.8.Convert.cs').read_text()
    admit = conversion[conversion.index('private static bool TryConvertWindow('):conversion.index('        string name = window.name;', conversion.index('private static bool TryConvertWindow('))]
    originals['NativeAdmission.cs'] = 'using UnityEngine.UI; namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n' + method(screen, 'private static bool FloatWantedFor(') + '\n' + admit + 'AdmissionAttempts++; return false; } }}\n'
    transfer = (modal / 'ModalFallback.CompositeTransfer.cs').read_text()
    originals['NativeTransfer742.cs'] = ('using System;using UnityEngine;using UnityEngine.UI;namespace GloomhavenVR.WorldUI {internal static partial class ModalFallback {'
        + method(transfer, 'internal static bool ReleaseForComposite(') + '}}')
    supersample_core = (root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs').read_text()
    supersample_capture = (root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs').read_text()
    kernels = [method(supersample_core, 'private static bool Eligible(')]
    for signature in ('private static bool BuildCamera(', 'private static void ApplyCaptureClear(',
                      'private static bool BuildDisplay(', 'private static void SyncProjection(',
                      'private static void SyncDisplayPose(', 'private static void ResolveAndMip(',
                      'private static void NeutraliseGrabPassBlur(', 'private static float AskedMipLodOffset(',
                      'private static float ApplyMipLodOffset(', 'private static void BindDisplayTarget('):
        kernels.append(method(supersample_capture, signature))
    for name in ('MinAuthoredPx', 'MipLodOffsetMin', 'MipLodOffsetMax', 'MipLodOffsetEpsilon'):
        declaration = re.findall(r'private const float ' + name + r'\s*=\s*[^;]+;', supersample_core + supersample_capture)
        if len(declaration) != 1: raise RuntimeError('Supersample constant binding drift: ' + name)
        kernels.append(declaration[0])
    for value_type in ('Vector3', 'Quaternion'):
        start = supersample_capture.index('private static bool SameComponents(' + value_type)
        kernels.append(supersample_capture[start:supersample_capture.index(';', start) + 1])
    originals['SupersampleKernel.cs'] = ('using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI {'
        'internal static partial class PanelSupersample {\n' + '\n'.join(kernels) + '\n}}\n')
    converter_core=(root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs').read_text()
    centering=converter_core[converter_core.index('        target.SetParent(hostRect, worldPositionStays: false);'):converter_core.index('        AdoptNestedCanvases(panel);')]
    originals['NativeTargetFrame739.cs']='using UnityEngine;namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {internal static void NormalizeTarget739(RectTransform target,RectTransform hostRect,Vector2 size){'+centering+'}}}'
    adopt = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs').read_text()
    lifecycle = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs').read_text()
    background = [method(adopt, 'private static void HideFullScreenBackground(')]
    for name in ('BackgroundCoverFraction', 'BackgroundOpaqueAlpha', 'CanvasSweepIntervalFrames'):
        declaration = re.findall(r'private const (?:float|int) ' + name + r'\s*=\s*[^;]+;', adopt)
        if len(declaration) != 1: raise RuntimeError('Native backdrop constant binding drift: ' + name)
        background.append(declaration[0])
    restore = method(lifecycle, 'for (int i = 0; i < panel.HiddenBackgrounds.Count; i++)')
    originals['NativeBackgroundKernel739.cs'] = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;'
        'namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
        'private static readonly List<Graphic> BgGraphicScratch=new(64);private static readonly Vector3[] BgCornerScratch=new Vector3[4];'
        'internal static void SweepBackgrounds739(ConvertedPanel panel){if(panel.HideBackground)HideFullScreenBackground(panel,false);}'
        'internal static void RestoreBackgrounds739(ConvertedPanel panel){' + restore + 'panel.HiddenBackgrounds.Clear();}'
        + 'internal static void InstallBackground746(ConvertedPanel panel,bool transparentBackground,bool keepBackgroundHidden,System.Func<Graphic,bool>? backgroundFilter){'
        + method(converter_core, 'if (transparentBackground)') + '}'
        + '\n'.join(background) + '}}')
    story_source=(root / 'src/GloomhavenVR/WorldUI/Composites/StoryComposite.cs').read_text()
    spec=importlib.util.spec_from_file_location('first_fit_bindings748',fixture/'first_fit_bindings748.py')
    fit_bindings=importlib.util.module_from_spec(spec);spec.loader.exec_module(fit_bindings)
    originals['NativeFirstFit748.cs']=fit_bindings.bind(root,method)
    story_commitment=re.findall(r'internal static bool PointOfNoReturn\s*=>[^;]+;',story_source)
    if len(story_commitment)!=1:raise RuntimeError('Native story commitment binding drift')
    originals['NativeStoryCommitment748.cs']='namespace GloomhavenVR.WorldUI {internal static partial class StoryComposite {'+story_commitment[0]+'}}'
    story_methods=[method(story_source,signature) for signature in ('private static bool Park(',
        'private static void WriteLayers(', 'private static void ApplyPose(',
        'private static bool TryPaintedBounds(', 'private static void ReturnNarrativeBackdrop(', 'private static void Unpark(')]
    painted=story_source[story_source.index('private static bool CountsAsPaintedHere('):story_source.index('    private static void ReturnNarrativeBackdrop(')]
    for name in ('DockName','MinParkSizePx','ImageGapPx','OffsetEpsilonPx'):
        declaration=re.findall(r'private const (?:string|float) '+name+r'\s*=\s*[^;]+;',story_source)
        if len(declaration)!=1:raise RuntimeError('Native story constant binding drift: '+name)
        story_methods.append(declaration[0])
    originals['NativeStoryComposite746.cs']=('using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;'
        'namespace GloomhavenVR.WorldUI {internal static class NativeStoryComposite746 {'
        'private const string Scope="fixture story";private static RectTransform? _dock,_picture;'
        'private static UIWindow? _parkHost,_parkSource;private static NarrativeBackdrop.Lease? _narrativeBackdrop;'
        'private static NarrativeBackdrop.LoadoutLease? _loadoutBackdrop;'
        'private static Vector2 _dockSize,_slotHostSize,_slotPictureSize;private static Rect _slot;'
        'private static bool _slotValid,_composeLogged;private static int _slotCount;private static string _parkedSprite="",_poseBasis="";'
        'private static readonly List<Transform> Moved=new(8);private static readonly List<int> MovedIndex=new(8);'
        'private static readonly MovedSubtreeLayers Layers=new(64);private static readonly List<Graphic> PaintScratch=new(48);'
        'private static readonly Vector3[] Corners=new Vector3[4];'
        'private static void LogComposed(UIWindow loadout,UIWindow story,RectTransform host){System.GC.KeepAlive((_composeLogged,_dockSize,_slotCount,_parkedSprite,_poseBasis));}'
        'internal static bool ParkOriginal(UIWindow loadout,RectTransform host,UIWindow story,RectTransform image)=>Park(loadout,host,story,(RectTransform)story.transform,image);'
        'internal static void TickOriginal(RectTransform host)=>ApplyPose(host);internal static void UnparkOriginal()=>Unpark("native test hand-back");'
        + '\n'.join(story_methods)+painted+'}}')
    order_source=(root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs').read_text()
    order_methods=[method(order_source,signature) for signature in ('internal static void TickPanelOrder(', 'private static void StampShownPose(', 'private static float PanelEyeDistance(', 'private static void ApplyPanelOrder(', 'internal static void RegisterOrderFollower(ConvertedPanel panel, Canvas canvas, int offset)')]
    for name in ('PanelOrderBase','PanelOrderStep','PanelOrderMaxRank','OrderSwapMarginMeters','OrderSwapStableFrames'):
        declaration=re.findall(r'(?:private|internal) const (?:int|float) '+name+r'\s*=\s*[^;]+;',order_source)
        if len(declaration)!=1:raise RuntimeError('Ordering constant binding drift: '+name)
        order_methods.append(declaration[0])
    originals['NativeOrderKernel739.cs']=('using System;using System.Collections.Generic;using UnityEngine;namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
        'private static readonly List<ConvertedPanel> OrderedPanels=new(32),StandaloneOrderPanels=new(1);'
        'private static List<ConvertedPanel> Active=>ActivePanels;private static float s_orderSwapMarginScale=1,s_orderSwapMarginWorld=.02f;private static float OrderSwapMargin=>s_orderSwapMarginWorld;'
        'private static int s_orderDistanceMeasures=0,s_orderWrites=0,s_orderResorts=0,s_orderHashNodes=0;internal static int ShownStampFrame {get;private set;}'
        'private static void TickFurnitureOrder(Vector3 eye){}private static void BuildSeenThroughLadder(Vector3 eye){}private static void LogPanelOrder(){}'
        'private static void CheckFollowerOffset(int offset,string name,bool allowBehind){}'
        'internal static void ResetOrder739(){foreach(var p in ActivePanels){p.OrderListed=false;p.OrderSwapPeer=null;p.OrderSwapStreak=0;}OrderedPanels.Clear();}'
        + '\n'.join(order_methods)+'}}')
    cases = [('production', {}, '')]
    cases.append(('narrative-production', {}, ''))
    cases.append(('narrative-early-production', {}, ''))
    narrative_controls = [
        ('narrative-dimmer-retained', 'NarrativeBackdrop.cs',
         'if (isStory) return Filter(window.transform, Kind.Story);',
         'if (isStory) return null;',
         'native story direct pixels contain no desktop dimmer outside original foreground'),
        ('narrative-filter-install-lost', 'NativeBackgroundKernel739.cs',
         'panel.BackgroundFilter = backgroundFilter;', 'panel.BackgroundFilter = null;',
         'native full-frame illustrations remain enabled beside hidden desktop dimmers'),
        ('narrative-art-role-lost', 'NarrativeBackdrop.cs',
         '            || image.GetComponent<Mask>() != null) return false;',
         '            ) return false;',
         'original native narrative stencil remains enabled'),
        ('narrative-root-scope-lost', 'NarrativeBackdrop.cs',
         '|| !graphic.transform.IsChildOf(root)', '|| false',
         'foreign native story graphics cannot join another producer background filter'),
        ('narrative-lease-restore-lost', 'NarrativeBackdrop.cs',
         'else graphic.enabled = true;',
         'else { /* lost native hand-back */ }',
         'composite unpark restores every original narrative dimmer flag'),
        ('narrative-input-role-lost', 'NarrativeBackdrop.cs',
         'if (image.raycastTarget || image.sprite == null', 'if (false || image.sprite == null',
         'native transparent input roots retain their original raycast component'),
        ('narrative-composite-lease-lost', 'NativeStoryComposite746.cs',
         '_narrativeBackdrop = new NarrativeBackdrop.Lease(dock, ModalFallback.PanelFor(story), story.transform);', '_narrativeBackdrop = null;',
         'actual story Park and ApplyPose hide only the native dimmer Image'),
        ('narrative-composite-sync-lost', 'NativeStoryComposite746.cs',
         '_narrativeBackdrop?.Sync();', '/* composite pose omitted backdrop sync */',
         'actual story Park and ApplyPose hide only the native dimmer Image'),
        ('narrative-composite-unpark-lost', 'NativeStoryComposite746.cs',
         '_narrativeBackdrop?.Dispose();', '/* composite unpark omitted native hand-back */',
         'composite unpark restores every original narrative dimmer flag'),
        ('narrative-loadout-lease-lost', 'NativeStoryComposite746.cs',
         '_loadoutBackdrop = new NarrativeBackdrop.LoadoutLease(loadout, image);', '_loadoutBackdrop = null;',
         'actual composed loadout final head pixels contain no native host dimmer in SP and MP'),
        ('narrative-loadout-sync-lost', 'NativeStoryComposite746.cs',
         '_loadoutBackdrop?.Sync();', '/* native host dimmer sync omitted */',
         'actual composed loadout final head pixels contain no native host dimmer in SP and MP'),
        ('narrative-loadout-restore-lost', 'NativeStoryComposite746.cs',
         '_loadoutBackdrop?.Dispose();', '/* native host hand-back omitted */',
         'actual native host dimmer and both blur flags restore before the next native loadout phase'),
        ('narrative-loadout-owner-lost', 'NarrativeBackdrop.cs',
         '&& _info.transform.parent == _owner.transform', '&& true',
         'foreign native loadout viewer is never leased through a stale manager reference'),
        ('narrative-loadout-art-source-lost', 'NarrativeBackdrop.cs',
         '|| _dimmer.sprite.name != "Black_Backdrop"', '|| false',
         'native viewer illustration content is never mistaken for its desktop dimmer'),
        ('narrative-loadout-input-role-lost', 'NarrativeBackdrop.cs',
         '!OwnsViewer() || _dimmer == null || _dimmer.raycastTarget', '!OwnsViewer() || _dimmer == null',
         'native loadout input graphics cannot join the desktop dimmer lease'),
        ('narrative-loadout-pooled-source-lost', 'NarrativeBackdrop.cs',
         '&& _dimmer.sprite == _source', '&& true',
         'pooled native loadout content is not re-enabled by an expired dimmer lease'),
    ]
    for name, key, before, after, expected in narrative_controls:
        if originals[key].count(before) != 1: raise RuntimeError('Narrative mutation binding drift: ' + name)
        cases.append((name, {key: originals[key].replace(before, after)}, expected))
    early_controls = [
        ('narrative-early-filter-lost', 'NarrativeBackdrop.cs',
         '? new LoadoutLease(window, viewer.imageHolder.rectTransform).Contains : null;', '? null : null;',
         'first revealing native loadout eye pixels have no desktop dimmer before story Park'),
        ('narrative-early-waits-for-active-art', 'NarrativeBackdrop.cs',
         'return viewer != null && viewer.imageHolder != null', 'return viewer != null && viewer.imageHolder != null && viewer.imageHolder.gameObject.activeInHierarchy',
         'first revealing native loadout eye pixels have no desktop dimmer before story Park'),
        ('narrative-early-waits-for-loaded-art', 'NarrativeBackdrop.cs',
         'return viewer != null && viewer.imageHolder != null', 'return viewer != null && viewer.imageHolder != null && viewer.imageHolder.sprite != null',
         'first revealing native loadout eye pixels have no desktop dimmer before story Park'),
        ('narrative-early-initial-treatment-lost', 'NativeBackgroundKernel739.cs',
         'HideFullScreenBackground(panel, initial: true);', '/* initial producer treatment omitted */',
         'first revealing native loadout eye pixels have no desktop dimmer before story Park'),
        ('narrative-early-inactive-receipt-lost', 'NativeBackgroundKernel739.cs',
         'includeInactive: panel.BackgroundFilter != null', 'includeInactive: false',
         'native inactive Blur retains its original enabled receipt before capture neutralization'),
        ('narrative-early-fit-counts-disabled', 'NativeFirstFit748.cs',
         'if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)',
         'if (g == null || g.canvasRenderer == null || g.canvasRenderer.cull)',
         'actual first native fit excludes the full desktop backing even before illustration readiness'),
        ('narrative-ownership-culled-role-lost', 'NativeBackgroundKernel739.cs',
         '(g.canvasRenderer.cull && panel.BackgroundFilter == null)', 'g.canvasRenderer.cull',
         'exact native producer owns culled backings before the first reveal'),
        ('narrative-ownership-handoff-receipt-lost', 'NarrativeBackdrop.cs',
         'if (!panel.HiddenBackgrounds.Contains(image)) panel.HiddenBackgrounds.Add(image);', '/* composite owner receipt omitted */',
         'converter receives every exact original receipt from the earlier composite lease'),
        ('narrative-ownership-handoff-reenables', 'NarrativeBackdrop.cs',
         'if (panel != null && panel.HideBackground && panel.BackgroundFilter != null\n                    && panel.BackgroundFilter(image))',
         'if (panel != null && !panel.IsAlive && panel.HideBackground && panel.BackgroundFilter != null\n                    && panel.BackgroundFilter(image))',
         'composite handoff never re-enables a backing inside its live converted owner'),
        ('narrative-ownership-pooled-restore-lost', 'NativeBackgroundKernel739.cs',
         'if (g != null && (panel.BackgroundFilter == null || panel.BackgroundFilter(g)))', 'if (g != null)',
         'converted native background receipts never re-enable pooled source or foreign-parent content'),
        ('narrative-ownership-blur-origin-lost', 'NarrativeBackdrop.cs',
         'blurs.Add(image, (parent, parent.sprite));', '/* original Blur receipt omitted */',
         'native owner restores original flags after a rejected composite role adoption'),
        ('narrative-ownership-original-disabled-lost', 'NativeBackgroundKernel739.cs',
         'if (g == null || !g.enabled || !(g is Image || g is RawImage))', 'if (g == null || !(g is Image || g is RawImage))',
         'live native source converter regains exactly its original enabled receipts after child return'),
        ('narrative-ownership-generic-restore-lost', 'NativeBackgroundKernel739.cs',
         'if (g != null && (panel.BackgroundFilter == null || panel.BackgroundFilter(g)))', 'if (g != null && panel.BackgroundFilter != null && panel.BackgroundFilter(g))',
         'ordinary no-filter restoration remains independent of narrative source roles'),
        ('narrative-ownership-story-receipt-lost', 'NarrativeBackdrop.cs',
         'if (_borrowed.ContainsKey(previous.HiddenBackgrounds[i])) previous.HiddenBackgrounds.RemoveAt(i);',
         'if (_borrowed.ContainsKey(previous.HiddenBackgrounds[i])) { /* owner receipt transfer omitted */ }',
         'story dock takes every converted source backdrop receipt before moving native children'),
        ('narrative-ownership-destroyed-parent-lost', 'NarrativeBackdrop.cs',
         '&& original.Parent != null', '&& true',
         'destroyed native backdrop Image safely retires the cached Blur role'),
        ('narrative-ownership-destroyed-blur-lost', 'NarrativeBackdrop.cs',
         'graphic is Image image && image != null && blurs.TryGetValue', 'graphic is Image image && blurs.TryGetValue',
         'destroyed native Blur Image safely retires its cached original role'),
        ('narrative-ownership-adoption-atomicity-lost', 'NarrativeBackdrop.cs',
         '_borrowed.Add(graphic, (parent, dimmer, dimmer.sprite));\n                    _hidden.Add(graphic);',
         '_borrowed.Add(graphic, (parent, dimmer, dimmer.sprite));\n                    _hidden.Add(graphic); previous.HiddenBackgrounds.RemoveAt(i);',
         'failed native role adoption preserves every previous converter receipt and original hidden flag'),
        ('narrative-ownership-live-source-return-lost', 'NarrativeBackdrop.cs',
         'if (panel != null && panel.HideBackground && panel.BackgroundFilter != null && panel.BackgroundFilter(graphic))',
         'if (panel != null && !panel.IsAlive && panel.HideBackground && panel.BackgroundFilter != null && panel.BackgroundFilter(graphic))',
         'return to a live native story converter keeps its backdrop continuously disabled'),
    ]
    for name,key,before,after,expected in early_controls:
        if originals[key].count(before)!=1:raise RuntimeError('Early narrative mutation binding drift: '+name)
        cases.append((name,{key:originals[key].replace(before,after)},expected))
    changes = [
        ('card-list-opener-waits-for-open', 'CardListEntry.cs', '&& button.gameObject.activeSelf && reset != null', '&& button.gameObject.activeSelf && reset != null && reset.inventory.GetComponent<UIWindow>().IsOpen', 'native closed-list opener is reachable inside converted character Cards tab before list opens'),
        ('card-list-caption-not-clickable', 'CardListEntry.cs', '_caption.raycastTarget = true;', '_caption.raycastTarget = false;', 'native graphic raycast hits More Cards caption'),
        ('card-list-caption-language-stale', 'CardListEntry.cs', 'Loc.OnChanged += RefreshCaption;', '/* language change subscription omitted */', 'native card-list caption follows German language switch'),
        ('confirmation-aa-binding-lost', 'ConfirmationOnOwner.cs', 'PanelSupersample.BindConfirmationMember(panel, seat.PersistentMember);', '/* native producer AA association omitted */', 'native confirmation producer registers original member with owner AA capture'),
        ('confirmation-aa-before-placement', 'ConfirmationOnOwner.cs', 'Apply(seat);\n            PanelSupersample.BindConfirmationMember(panel, seat.PersistentMember);', 'PanelSupersample.BindConfirmationMember(panel, seat.PersistentMember);\n            Apply(seat);', 'owner AA binding follows final native confirmation placement'),
        ('unsafe-active-clone', 'AnnouncementContinueView.cs', 'staging.SetActive(false);', '/* active staging regression */', 'native input controller clones never enable'),
        ('native-graphics-lost', 'AnnouncementContinueView.cs', '|| component is Graphic', '/* native graphics dropped */', 'native Continue retains complete original hierarchy geometry and font'),
        ('source-callback-retained', 'AnnouncementContinueView.cs', '_button.onClick = new Button.ButtonClickedEvent();', '_button.onClick.RemoveAllListeners();', 'native Continue clone never retains serialized reward callbacks'),
        ('tracker-relay-lost', 'AnnouncementContinueView.cs', 'copy.AddComponent<AnnouncementNativeContinuePointer>().Press = Continue;', 'copy.AddComponent<AnnouncementNativeContinuePointer>().Press = null;', 'native button gate false cannot veto the original ready tracker pointer permission'),
        ('localization-per-frame', 'AnnouncementContinueView.cs', '_continue = onContinue;', '_continue = onContinue; _button!.TextLanguageKey = "GUI_CONTINUE";', 'steady native Continue does not rewrite localization every frame'),
        ('pooled-text-birth-ignored', 'LevelUpComposite.cs', 'TMPro_EventManager.TEXT_CHANGED_EVENT.Add(PreviewTextChanged);', '/* native text birth observation omitted */', 'same pooled preview root accepts later native text births'),
        ('text-ink-ignored', 'LevelUpComposite.cs', 'Bounds ink = text.textBounds;', 'Bounds ink = default;', 'complete original card text remains readable'),
        ('glyphless-ink-admitted', 'LevelUpComposite.cs', 'if (!HasRenderedInk(ink)) continue;', '/* original TMP sentinel edges admitted */', 'glyphless TMP cannot eject either original card list after the reveal'),
        ('cross-scene-rejected', 'ConfirmationOnOwner.cs', '|| mount.IsChildOf(rect))', '|| mount.IsChildOf(rect) || mount.gameObject.scene != rect.gameObject.scene)', 'same native confirmation joins level-up before independent floating placement'),
        ('persistent-scene-stolen', 'ConfirmationOnOwner.cs', 'seat.PersistentMember = CanvasConversion.Convert', 'rect.SetParent(mount,false); seat.PersistentMember = CanvasConversion.Convert', 'persistent native dialog never inherits the scene-owned level-up lifetime'),
        ('owner-loss-closes-native', 'ConfirmationOnOwner.cs', 'Seat seat = Seats[index];', 'Seat seat = Seats[index]; if (!SourceAlive(seat.Request)) { if (seat.Box is ConfirmationBox original) original.IsOpen = false; seat.Box.GetComponent<UIWindow>()!.IsVisible = false; }', 'owner scene unload restores the original persistent dialog'),
        ('generic-float-readmission', 'NativeAdmission.cs', 'window == null || ConfirmationOnOwner.IsOwned(window)', 'window == null', 'owned confirmation cannot be readmitted as a generic floating window'),
        ('direct-convert-readmission', 'NativeAdmission.cs', 'if (ConfirmationOnOwner.IsOwned(window)) return true;', '/* semantic admission guard omitted */', 'direct generic conversion acknowledges existing embedded presentation'),
        ('owner-resize-ignored', 'ConfirmationOnOwner.cs', 'host.localScale = seat.Mount.lossyScale;', '/* accepted owner scale omitted */', 'persistent overlay copies only accepted owner world matrix'),
        ('persistent-capture-bypassed', 'ConfirmationOnOwner.cs', 'useModLayer: true', 'useModLayer: false', 'owned confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('standalone-capture-bypassed', 'DialogSurface.cs', 'useModLayer: true', 'useModLayer: false', 'standalone confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('confirmation-blur-allowed', 'SupersampleKernel.cs', 'if (PanelGraphicMaterial.NeutraliseGrabPass(t.GetComponent<Graphic>()))', 'if (PanelGraphicMaterial.NeutraliseGrabPass(null))', 'owned confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('persistent-backdrop-retained', 'ConfirmationOnOwner.cs', 'transparentBackground: true', 'transparentBackground: false', 'owned direct confirmation has no full-screen black or GrabPass surface outside the native small box'),
        ('standalone-backdrop-retained', 'DialogSurface.cs', 'transparentBackground: true', 'transparentBackground: false', 'standalone direct confirmation has no full-screen black or GrabPass surface outside the native small box'),
        ('persistent-owner-tier-lost', 'ConfirmationOnOwner.cs', 'sortingOrder: panel.BaseSortingOrder + 1', 'sortingOrder: 0', 'owned direct confirmation retains every front and rear native foreground pixel during motion'),
        ('persistent-lease-not-retired', 'ConfirmationOnOwner.cs', 'if (PanelAlive(seat.PersistentMember)) return false;', 'if (seat.PersistentMember.IsAlive) return false;', 'native close restores exact persistent dialog home while owner remains open'),
        ('item-origin-lost', 'ConfirmationOnOwner.Origins.cs', 'private static void Prefix(UIItemConfirmationBox __instance)\n    {\n        ConfirmationOnOwner.NativeShowDepth++;\n        ConfirmationOnOwner.Record(__instance);', 'private static void Prefix(UIItemConfirmationBox __instance)\n    {\n        ConfirmationOnOwner.NativeShowDepth++;\n        _ = __instance;', 'native item Show records its exact shop producer and keeps the pooled original persistent'),
        ('deferred-source-lost', 'ConfirmationOnOwner.Origins.cs', '? field.GetValue(__instance) as Component : null;', '? null : null;', 'native deferred callback uses its captured original producer and restores ambient scope'),
        ('deferred-scope-not-restored', 'ConfirmationOnOwner.Origins.cs', '__state = Tuple.Create(ConfirmationOnOwner.CurrentScope, ConfirmationOnOwner.CurrentDeferredOrigin);', '__state = Tuple.Create<Component?, ConfirmationOnOwner.NavigationOrigin?>(null, ConfirmationOnOwner.CurrentDeferredOrigin);', 'native deferred callback uses its captured original producer and restores ambient scope'),
        ('actor-only-source-lost', 'ConfirmationOnOwner.Origins.cs', 'Component? previous = ConfirmationOnOwner.Begin(__instance);', 'Component? previous = ConfirmationOnOwner.CurrentScope;', 'native actor-only tooltip action retains its producing UI and original action'),
        ('menu-owner-undiscovered', 'ConfirmationOnOwner.cs', 'return FlatScreen.TryGetMenuOwner(source, out ConvertedPanel? menuPanel) ? menuPanel : null;', 'return null;', 'ready menu confirmation uses its proved capture wrapper without a fake table or converted ancestor'),
        ('menu-owner-not-live', 'ConfirmationOnOwner.cs', 'return FlatScreen.IsMenuOwner(panel);', 'return false;', 'settled menu ownership retains its live wrapper without per-frame release and recapture'),
        ('item-backdrop-retained', 'ConfirmationOnOwner.cs', 'transparentBackground: true', 'transparentBackground: box is not UIItemConfirmationBox', 'item direct confirmation has no full-frame black or GrabPass surface outside the native content'),
        ('legacy-member-yield-lost', 'MapDialogSeat.cs', 'window != null && ConfirmationOnOwner.IsOwned(window)', 'window != null && bool.Parse("false")', 'both actual coordinators retain one settled item owner without transform or capture churn'),
        ('legacy-refused-home-ignored', 'MapDialogSeat.cs', 'if (!ReferenceEquals(rect.parent, seat.Home)) return false;', '/* rejected native parent ignored */', 'rejected legacy native home retains the old lease and prevents a competing producer snapshot'),
        ('native-transfer-lease-retained', 'NativeTransfer742.cs', 'if (!ConfirmationOnOwner.ReleaseForExternalOwner(window))\n            return false;', '/* prior producer lease incorrectly retained */', 'actual common transfer retires the complete member lease before the palm records native home'),
        ('immersive-owner-priority-lost', 'ConfirmationOnOwner.cs', 'return window != null && TownServicePresentation.OwnsWindow(window);', 'return false;', 'immersive native requests bypass generic transfer machinery before either coordinator can write'),
        ('legacy-immersive-handoff-allowed', 'MapDialogSeat.cs', 'if (TownServicePresentation.OwnsWindow(window)) return false;', '/* immersive owner guard omitted */', 'legacy handoff refuses an original currently owned by the immersive native presentation'),
        ('external-source-retained', 'ConfirmationOnOwner.cs', 'Requests.Remove(seat.Box);', '/* stale native producer retained */', 'external transfer clears old exact producer provenance until another native request'),
        ('destroyed-foreign-guard-lost', 'ConfirmationOnOwner.cs', 'private static bool ForeignOwner(Component box)\n    {\n        if (box == null) return false;', 'private static bool ForeignOwner(Component box)\n    {', 'destroyed native originals retire without consulting a foreign owner through a missing Unity object'),
        ('destroyed-original-transfer-accepted', 'ConfirmationOnOwner.cs', 'if (window == null) return false;', '/* destroyed original accepted */', 'a destroyed native original cannot be borrowed by an external presentation owner'),
        ('perks-owner-rejected', 'ConfirmationOnOwner.cs', '|| mount.IsChildOf(rect))', '|| mount.IsChildOf(rect) || request.Source is UIPerksInventory)', 'every perks confirmation is a foreground member of its actual producer window'),
        ('same-scene-member-rejected', 'ConfirmationOnOwner.cs', '|| mount.IsChildOf(rect))', '|| mount.IsChildOf(rect) || mount.gameObject.scene == rect.gameObject.scene)', 'every perks confirmation is a foreground member of its actual producer window'),
    ]
    # Cards is a separate production lane; narrative-only selection does not
    # bind its unrelated mutations. The integrated/full candidate must carry
    # these exact owned-inventory APIs and reject every causal omission.
    if not args.narrative_only:
        changes.extend([
            ('card-list-native-browse-close-lost', 'LevelUpComposite.cs',
             '            reset.Hide();', '            /* native browse Hide omitted */',
             'commitment closes proved browsing through original Hide while preserving the native toggle preference'),
            ('card-list-native-browse-refloat', 'NativeAdmission.cs',
             'if (LevelUpComposite.IsOwnedInventory(window)) return false;', '/* ordinary native inventory ownership admission omitted */',
             'retired native-home browser cannot refloat through ordinary or direct admission during its fade'),
            ('card-list-native-browse-direct-readmission', 'NativeAdmission.cs',
             'if (LevelUpComposite.IsOwnedInventory(window)) return true;', '/* direct native inventory ownership admission omitted */',
             'retired native-home browser cannot refloat through ordinary or direct admission during its fade'),
            ('card-list-native-mandatory-recovery-lost', 'LevelUpComposite.cs',
             '        if (MandatoryLevelUp(inventory)) return;', '        /* original mandatory level-up priority omitted */',
             'fresh mandatory native level-up overrides the old browse receipt during commitment'),
            ('card-list-native-owner-recovery-lost', 'LevelUpComposite.cs',
             '        if (!StoryComposite.PointOfNoReturn && BrowseOwnerAvailable(reset))',
             '        if (!StoryComposite.PointOfNoReturn && BrowseOwnerAvailable(reset) && !ReferenceEquals(_retiredBrowse, inventory))',
             'fresh native Cards owner recovers browsing after commitment ends'),
        ])
    for name, file, before, after, expected in changes:
        if originals[file].count(before) != 1:
            raise SystemExit('Mutation binding drift: ' + name)
        cases.append((name, {file: originals[file].replace(before, after)}, expected))
    if args.card_list_only and (not args.case or any(name != 'production' and not name.startswith('card-list-') for name in args.case)):
        parser.error('--card-list-only requires explicit production/card-list case selection')
    if args.narrative_only:
        if args.card_list_only: parser.error('Narrative and card-list subsets are separate scopes')
        cases = [case for case in cases if case[0].startswith('narrative-')]
    if args.case:
        unknown = set(args.case) - {case[0] for case in cases}
        if unknown: parser.error('Unknown cases: ' + str(unknown))
        cases = [case for case in cases if case[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('Evidence: ' + str(run), flush=True)
    log = run / 'completion.log'; log.write_text('Evidence: ' + str(run) + '\n')
    code = 1; reaped = True; project = run / 'unity'; process = None
    compiler_temp = Path(tempfile.mkdtemp(prefix='ghvr-levelup-', dir='/tmp'))
    (run / 'compiler-temp.json').write_text(json.dumps({'TMPDIR': str(compiler_temp)}) + '\n')
    spec = importlib.util.spec_from_file_location('project_compilers', ROOT / 'scripts/menu-depth-character/project_compilers.py')
    compilers = importlib.util.module_from_spec(spec); spec.loader.exec_module(compilers)
    spec = importlib.util.spec_from_file_location('run_compilers', ROOT / 'scripts/check-environment-menu-scope.py')
    run_compilers = importlib.util.module_from_spec(spec); spec.loader.exec_module(run_compilers)
    try:
        managed = root / 'ressources/GH_Data/Managed'; dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        inputs = run / 'native-inputs'
        result = subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(fixture / 'export_inputs.py'), str(root), str(inputs)], capture_output=True, text=True)
        (run / 'native-export.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise RuntimeError('Native export failed: ' + str(run / 'native-export.log'))
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'inputs': str(inputs), 'managed': str(managed.resolve()), 'entry': 'RunCardListEntry745' if args.card_list_only else 'Run', 'cases': []}
        hashes = {}
        for file in [*(root / 'src/GloomhavenVR/WorldUI' / v for v in FILES.values()), *quest_sources, root / 'src/GloomhavenVR/WorldUI/Composites/StoryComposite.cs', root / 'src/GloomhavenVR/Core/Loc/Loc.CardList.cs', *(root / 'src/GloomhavenVR/WorldUI/Conversion' / name for name in ('CanvasConversion.1.Core.cs','CanvasConversion.2.Adopt.cs','CanvasConversion.3.Fit.cs','CanvasConversion.4.Lifecycle.cs','CanvasConversion.8.Order.cs')), root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs', root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs', modal / 'ModalFallback.12.ScreenBind.cs', modal / 'ModalFallback.8.Convert.cs', modal / 'ModalFallback.CompositeTransfer.cs', Path(__file__), ROOT / 'scripts/check-environment-menu-scope.py', ROOT / 'scripts/menu-depth-character/project_compilers.py', ROOT / 'scripts/town_run_retention.py', *fixture.rglob('*.cs'), *fixture.glob('*.py'), *fixture.glob('*.shader'), fixture / 'LevelUpNative.csproj', *(managed / v for v in ('GH.Runtime.dll', 'UnityEngine.UI.dll', 'Unity.TextMeshPro.dll'))]:
            hashes[str(file)] = hashlib.sha256(file.read_bytes()).hexdigest()
        (run / 'source-pins.json').write_text(json.dumps({'sha256': hashes, 'coverage': 'partial' if args.case or args.narrative_only or args.card_list_only else 'production-and-controls', 'limits': __doc__}, indent=2) + '\n')
        env = os.environ.copy(); env.update(UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', TMPDIR=str(compiler_temp))
        assemblies = run / 'executed-assemblies'; assemblies.mkdir()
        for name, replacements, expected in cases:
            build = run / name; production = build / 'production'; production.mkdir(parents=True)
            for file, source in originals.items(): (production / file).write_text(replacements.get(file, source))
            # Harmony/MonoMod keys generated detours by method/type identity. The
            # real game is common to all variants, but surrounding fake native
            # controllers must have distinct CLR namespaces in the shared editor.
            # Production bodies remain unchanged and bind these explicit ports
            # through global aliases; actual native metadata is audited separately.
            case_fixture = build / 'fixture'; case_fixture.mkdir()
            for original in fixture.glob('*.cs'): shutil.copyfile(original, case_fixture / original.name)
            boundary = (case_fixture / 'Boundary.cs').read_text()
            namespace = 'NativeBoundary742_' + name.replace('-', '_')
            start = boundary.index('internal static class FFSNetwork')
            end = boundary.index('\nnamespace UnityEngine.UI', start)
            block = boundary[start:end]
            sprite = 'namespace SpriteMemoryManagement { internal sealed class ReferenceToSprite { } }\n'
            if block.count(sprite) != 1: raise RuntimeError('Native namespace port binding drift')
            block = block.replace(sprite, '')
            aliases = re.findall(r'^internal (?:static |sealed )?class (\w+)', block, re.M)
            boundary = boundary[:start] + sprite + 'namespace ' + namespace + ' {\n' + block + '\n}\n' + boundary[end:]
            boundary = boundary.replace('namespace UnityEngine.UI\n{', 'namespace ' + namespace + '\n{', 1)
            aliases += ['UIWindow', 'UIWindowID']
            boundary = boundary.replace('extern alias native;\n', 'extern alias native;\n' + '\n'.join('global using ' + alias + ' = ' + namespace + '.' + alias + ';' for alias in aliases) + '\n', 1)
            (case_fixture / 'Boundary.cs').write_text(boundary)
            (build / 'fixture-port-binding.json').write_text(json.dumps({'namespace': namespace, 'aliases': aliases,
                'boundary_sha256': hashlib.sha256(boundary.encode()).hexdigest(),
                'production': 'Unchanged production bodies; only explicit surrounding fixture native input types have variant identities.'}, indent=2) + '\n')
            csproj = build / 'LevelUpNative.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'LevelUpNative_' + name.replace('-', '_')
            result = subprocess.run([dotnet, 'build', str(csproj), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(case_fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(managed.resolve())], capture_output=True, text=True, env=env)
            (build / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode: raise RuntimeError('Compilation failure is not a successful defect control: ' + str(build / 'build.log'))
            output = build / 'bin/Release/net472'; dll = assemblies / (assembly + '.dll'); shutil.copyfile(output / dll.name, dll)
            if 'harmony' not in manifest:
                runtime = run / 'harmony-runtime'; runtime.mkdir()
                for item in output.glob('*.dll'):
                    if item.name != dll.name: shutil.copyfile(item, runtime / item.name)
                manifest['harmony'] = str(runtime / '0Harmony.dll')
            manifest['cases'].append({'name': name, 'dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'expected': expected,
                'entry': 'RunBackdropOwnership748' if name.startswith('narrative-ownership-') else 'RunEarlyLoadout748' if name.startswith('narrative-early-') else 'RunNarrative746' if name.startswith('narrative-') else ''})
        for path in ('Assets/Editor', 'Assets/Plugins', 'Assets/Resources/OriginalShaders', 'Packages', 'ProjectSettings'): (project / path).mkdir(parents=True)
        shutil.copyfile(fixture / 'Editor/LevelUpRunner.cs', project / 'Assets/Editor/LevelUpRunner.cs')
        for shader in fixture.glob('*.shader'): shutil.copyfile(shader, project / 'Assets/Resources' / shader.name)
        for name in ('Unity.TextMeshPro.dll', 'UnityEngine.UI.dll'): shutil.copyfile(managed / name, project / 'Assets/Plugins' / name)
        shaders = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
        source = (shaders / 'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"', 'Shader "TextMeshPro/Distance Field"', 1)
        source = source.replace('\n\t\t\t#include "ActorBarForeground.cginc"', '', 1)
        source = re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);', '', source)
        source = re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;', '', source)
        source = re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);', '', source)
        source = source.replace('\n\t\t\t\tComputeScreenPos(vPosition),', '')
        source = source.replace('Fallback "GloomhavenVR/ActorBarMobileSDF"', 'Fallback "TextMeshPro/Mobile/Distance Field"')
        provenance = json.loads((shaders / 'source-provenance.json').read_text())
        record = next(item for item in provenance['sources'] if item['file'] == 'ActorBarSDF.shader')
        original = source
        for pragma in record['retained_feature_pragmas']:
            before = pragma.strip().replace('shader_feature', 'multi_compile')
            if original.count(before) != 1: raise RuntimeError('Canonical shader pragma binding drift')
            original = original.replace(before, pragma.strip())
        if hashlib.sha256(original.encode()).hexdigest() != record['normalized_original_sha256']:
            raise RuntimeError('Canonical original TMP shader differs from its verified upstream source')
        (run / 'canonical-source-provenance.json').write_text(json.dumps({'normalized_original_sha256': record['normalized_original_sha256'], 'runtime': 'Original source with the same retained keyword branches; native Windows shader bytecode is not executed.'}, indent=2) + '\n')
        (project / 'Assets/Resources/OriginalShaders/NativeSDF.shader').write_text(source)
        for name in ('TMPro.cginc', 'TMPro_Properties.cginc'): shutil.copyfile(shaders / name, project / 'Assets/Resources/OriginalShaders' / name)
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {name: '1.0.0' for name in ('com.unity.modules.ui', 'com.unity.modules.imgui', 'com.unity.modules.jsonserialize', 'com.unity.modules.physics', 'com.unity.modules.imageconversion')}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'LevelUpRunner.Start', '-levelUpManifest', str(path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a', *command]
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True, env=env); reaped = False
        # Preserve the original14-case budget of30 seconds per variant as the
        # ownership/foreground control inventory grows. This is only the editor
        # completion limit; every pixel/callback/control oracle remains intact.
        editor_timeout = max(420, 30 * len(cases))
        (run / 'editor-budget.json').write_text(json.dumps({
            'cases': len(cases), 'seconds': editor_timeout,
            'formula': 'max(420,30*case_count)',
            'reason': 'The old420s limit cut off an active32/39-case pass before the remaining controls.'}, indent=2) + '\n')
        editor_timed_out=False
        try: result = process.wait(timeout=editor_timeout)
        except subprocess.TimeoutExpired:
            editor_timed_out=True
            raise
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait(); reaped = True
            (run/'editor-exit.json').write_text(json.dumps({'returncode':process.returncode,'timed_out':editor_timed_out,'budget_seconds':editor_timeout},indent=2)+'\n')
            editor_dll = project / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
            if editor_dll.exists(): shutil.copyfile(editor_dll, assemblies / 'Assembly-CSharp-Editor.dll')
            (run / 'executed-pins.json').write_text(json.dumps({str(p.relative_to(run)): hashlib.sha256(p.read_bytes()).hexdigest() for folder in (assemblies, run / 'harmony-runtime') for p in folder.glob('*.dll')}, indent=2) + '\n')
            editor = compilers.cleanup_project_editors(project, args.unity, launcher_reaped=True)
            (run / 'editor-cleanup.json').write_text(json.dumps(editor, indent=2) + '\n')
            if editor['remaining']: raise RuntimeError('Exact private editor failed to stop')
            compiler = run_compilers.cleanup_run_compilers(run, editor_reaped=True)
            (run / 'compiler-cleanup.json').write_text(json.dumps(compiler, indent=2) + '\n')
            if compiler['remaining'] or compiler['refused']: raise RuntimeError('Exact private compiler failed to stop safely')
        report = Path(manifest['result'])
        if report.is_file(): print(report.read_text(), end='')
        if result or not report.is_file(): raise RuntimeError('Unity failed: ' + str(run / 'unity.log'))
        code = 0
        print(('PARTIAL PASS' if args.case or args.narrative_only or args.card_list_only else 'PASS') + ': ' + str(len(cases)) + ' actual native/Unity variants; ' + str(run))
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL); process.wait(); reaped = True
        compiler = run_compilers.cleanup_run_compilers(run, editor_reaped=reaped)
        (run / 'completion-compiler-cleanup.json').write_text(json.dumps(compiler, indent=2) + '\n')
        if compiler['remaining'] or compiler['refused']:
            (run / '.keep').write_text('Compiler ownership cleanup requires review.\n')
            raise RuntimeError('Private compiler cleanup incomplete; preserving run')
        shutil.rmtree(compiler_temp)
        receipt = cleanup_completed_run('levelup-native-runtime', code, log, ROOT, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__': main()
