from pathlib import Path
import sys
root, out = map(Path, sys.argv[1:])
src=(root/'src/GloomhavenVR/Net/Remote/RemoteMapStory.cs').read_text()
def block(marker):
    start=src.index(marker); brace=src.index('{',start); depth=1; end=brace+1
    while depth:
        depth+=(src[end]=='{')-(src[end]=='}');end+=1
    return src[start:end]
local=block('private sealed class Local')
finished=block('if (box == null || clickedThrough)')
(out/'Extracted.cs').write_text('using UnityEngine; using UnityEngine.UI; using GloomhavenVR.WorldUI;\nnamespace GloomhavenVR.Net;\ninternal static partial class RemoteMapStory {\n'+local+'\nprivate static int SampleFinished(bool clickedThrough, object? box, float now) { int n=0;'+finished+'return n;}\n}')
(out/'Reflow.cs').write_text((root/'src/GloomhavenVR/Net/Remote/RemoteMapStory.Reflow.cs').read_text())
assert 'ObserveReflow(senderId, in p);' in src
assert src.count('ObserveReflowPose(senderId, SharedWindowKind.') == 3
assert 'if (!local.Moving || local.Reflow || grab.IsGrabbed' in src
assert 'unchecked { local.ReflowRevision++; }\n        grab.PlaceFrameAt' in src
avatar=(root/'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs').read_text()
assert 'WorldUI.SharedWindows.AnyGrabbedHere() || RemoteMapStory.SharedReflowMoving' in avatar
assert '!RemoteMapStory.SharedReflowSendDue' in avatar
print('Shared reflow production bindings: 6 passed.')

for filename in ['MapStoryOpeningLedger.cs', 'MapStoryLifecycleState.cs']:
    (out/filename).write_text((root/'src/GloomhavenVR/Net'/filename).read_text())
pose_guard = 'if (MapStoryLifecycle.MatchesPose(true, senderId, openingEpoch, openingToken))\n                            ObserveReflowPose(senderId, SharedWindowKind.MapStory, in e, StoryStamp);'
assert pose_guard in src
(out/'PoseGate.cs').write_text('using System.Collections.Generic; using GloomhavenVR.WorldUI; namespace GloomhavenVR.Net; internal static partial class RemoteMapStory { private static void ReceiveStoryPose(int senderId, in PresenceState p, in SharedWindowEntry e, Dictionary<int, byte> StoryStamp) { MapStoryLifecycle.PoseOpening(in p, e.ContentKey, out uint openingEpoch, out uint openingToken); '+pose_guard+' } }')
src=(root/'src/GloomhavenVR/Net/Remote/MapStoryLifecycle.cs').read_text()
(out/'PoseOpening.cs').write_text('namespace GloomhavenVR.Net; internal static partial class MapStoryLifecycle {'+block('internal static void PoseOpening(')+'}')
