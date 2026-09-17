#!/usr/bin/env python3
"""Check runtime admission and pose bindings alongside the numerical production policy."""
from pathlib import Path
import re
import sys
root = Path(sys.argv[1])
def code(relative):
    value = (root/relative).read_text()
    return re.sub(r'/\*.*?\*/|//[^\n]*', '', value, flags=re.S)
base = 'src/GloomhavenVR/WorldUI/'
runtime = code(base+'Modal/ModalFallback.WindowRoomMaking.cs')
checks = {
    'conversion tick services finite room making': 'TickWindowRoomMaking();' in code(base+'Modal/ModalFallback.4.Tick.cs'),
    'fit timeout retains a visible-window fallback': '(!fitted && age < 0.65f)' in runtime,
    'opening admission expires instead of idle following': 'age > 3f' in runtime,
    'painted footprint participates in measurement': 'WindowReflowBounds.TryRead(wp.Panel, out Rect bounds, out bool pending)' in runtime and 'TryGetHitRect' not in runtime,
    'transient opening geometry waits without blocking reveal': 'WindowMaterialise.IsAnimating(wp.Panel)' in runtime and 'if (pending) { retry = true; return false; }' in runtime,
    'only older windows may move': 'ReflowGeometry[n].Movable = i < incomingOrder' in runtime,
    'anchor closure or manual grab cancels displacement': '!Converted.Contains(_reflowIncoming)' in runtime and '_reflowIncoming.Grab!.IsGrabbed' in runtime,
    'frame pivot follows the measured footprint centre': 'WindowReflowPose.FramePositionForCentre(ReflowFrom[i], ReflowFromRotation[i],' in runtime,
    'shared motion cancels on peer revision': 'SharedWindowReflowBridge.Revision?.Invoke(ReflowKinds[i])' in runtime,
    'manual grip cancels animation': 'wp.Grab!.IsGrabbed' in runtime,
    'closing windows cancel animation': '!Converted.Contains(wp) || !ReflowVisible(wp)' in runtime,
    'room-making writes the frame with explicit attribution': 'PanelPoseWatch.Writer.RoomMaking' in code(base+'Grab/GrabbableModal.cs') and 'SnapFrameTo(position, rotation);' in code(base+'Grab/GrabbableModal.cs'),
    'attributed room-making is accepted by pose lock': 'case Writer.RoomMaking:' in code(base+'Conversion/PanelPoseWatch.cs'),
    'measured HMD cone caps placement': 'Mathf.Min(35f, UsableHalfConeDeg())' in runtime,
    'pitched headset viewport gates admission and endpoint': runtime.count('ReadReflowViewport(camera,') == 2,
    'reading-distance limit uses author physical metres': '3.5f * physicalScale' in runtime and 'physicalScale = Mathf.Max(PanelLayout.WorldScale' in runtime,
    'offscreen incoming windows cannot trigger room making': 'if (!inView) return false;' in runtime,
    'parked quest corner remains protected': '!corner && !wp.Grab!.IsGrabbed' in runtime,
}
for name, passed in checks.items():
    if not passed: raise SystemExit('FAIL: '+name)
print(f'Window reflow: {len(checks)} source bindings passed.')
