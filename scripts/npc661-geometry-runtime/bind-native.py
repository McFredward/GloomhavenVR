"""Bind the actual VRCard return sampler/update; only initialization/time are ports."""
import hashlib

def bind(root, bound, hashes, method):
    card = (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text()
    sampler = method(card, 'internal bool TryTownReturnMotion(')
    begin = card.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);')
    end = card.index('            if (ft >= 1f)', begin)
    step = card[begin:end].replace('Time.unscaledDeltaTime', 'global::GeometryClock661.Delta')
    native = '''using System;
using UnityEngine;
using GloomhavenVR.Hands;
namespace GloomhavenVR.Cards;
internal sealed partial class VRCard {
    private bool _flying, _flyIntro;
    private float _flyElapsed, _flyDuration, _flyArcHeight;
    private uint _townReturnRevision;
    private Vector3 _flyFromPos, _flyToPos, _flyFromScale, _flyToScale, _flyArcUp;
    private Quaternion _flyRot;
    internal bool NativeFlying661 => _flying;
    internal void BeginNative661(Vector3 target, float seconds) {
        _flying=true; _flyIntro=true; _flyElapsed=0; _flyDuration=seconds; _townReturnRevision=661;
        _flyFromPos=transform.position; _flyToPos=target; _flyRot=transform.rotation;
        _flyFromScale=transform.localScale; _flyToScale=transform.localScale*.7f;
        _flyArcUp=Vector3.up; _flyArcHeight=.08f;
    }
    internal void ReclaimNative661(VRHand hand) { Holder=hand; _flying=false; }
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNativeTownReturn661(', 1) + '''
    internal void StepNative661() {
''' + step + '''
        if (ft >= 1f) _flying=false; // Gameplay completion is the explicit boundary.
    }
}
'''
    bound['NativeVRCardReturn661.cs'] = native
    hashes['VRCard.cs (complete native sampler/update source)'] = hashlib.sha256(card.encode()).hexdigest()
    hashes['NativeVRCardReturn661.cs'] = hashlib.sha256(native.encode()).hexdigest()
    for name in ['TownServiceMirror.cs', 'TownServiceMirror.Motion.cs', 'TownServiceMirror.CardReturnCohorts.cs', 'TownServiceMotion.cs']:
        original = bound[name]
        bound[name] = original.replace('Time.unscaledTime', 'global::GeometryClock661.Read')
        hashes[name + ' (native time binding)'] = hashlib.sha256(bound[name].encode()).hexdigest()
    mirror = bound['TownServiceMirror.cs']
    call = 'RestoreOfferedPhysicalMounts();'
    if call not in mirror:
        opening = '    internal static void TickRemote(Func<int, Transform?> sharedFrame)\n    {'
        if mirror.count(opening) != 1: raise RuntimeError('Approved opening restore seam drift')
        mirror = mirror.replace(opening, opening + '\n        ' + call)
        bound['TownServiceMirror.cs'] = mirror
    hashes['TownServiceMirror.cs (approved opening seam binding)'] = hashlib.sha256(mirror.encode()).hexdigest()
    return bound, hashes
