from pathlib import Path
import sys, re, hashlib, json
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
assert 'WorldUI.SharedWindows.AnyGrabbedHere()' in avatar
assert '|| WorldUI.SharedWindows.AnyReleaseTurningHere() || RemoteMapStory.SharedReflowMoving' in avatar
assert '!RemoteMapStory.SharedReflowSendDue' in avatar
print('Shared reflow production bindings: 6 passed.')

for filename in ['MapStoryOpeningLedger.cs', 'MapStoryLifecycleState.cs']:
    (out/filename).write_text((root/'src/GloomhavenVR/Net'/filename).read_text())
pose_guard = 'if (MapStoryLifecycle.MatchesPose(true, senderId, openingEpoch, openingToken))\n                            ObserveReflowPose(senderId, SharedWindowKind.MapStory, in e, StoryStamp);'
assert pose_guard in src
(out/'PoseGate.cs').write_text('using System.Collections.Generic; using GloomhavenVR.WorldUI; namespace GloomhavenVR.Net; internal static partial class RemoteMapStory { private static void ReceiveStoryPose(int senderId, in PresenceState p, in SharedWindowEntry e, Dictionary<int, byte> StoryStamp) { MapStoryLifecycle.PoseOpening(in p, e.ContentKey, out uint openingEpoch, out uint openingToken); '+pose_guard+' } }')
src=(root/'src/GloomhavenVR/Net/Remote/MapStoryLifecycle.cs').read_text()
(out/'PoseOpening.cs').write_text('namespace GloomhavenVR.Net; internal static partial class MapStoryLifecycle {'+block('internal static void PoseOpening(')+'}')

# Execute the exact sender pre-emption expression with scalar input ports. Do not
# reimplement its OR/rate-limit policy in a test-only helper.
condition=re.search(r"bool sharedWindowDue = ([^;]+);", avatar).group(1)
assert "|| WorldUI.ModalFallback.StoryRecoverySendDue" in condition
for old,new in [
    ("WorldUI.SharedWindows.AnyGrabbedHere()","grabbed"),
    ("WorldUI.SharedWindows.AnyReleaseTurningHere()","turning"),
    ("RemoteMapStory.SharedReflowMoving","reflow"),
    ("WorldUI.ModalFallback.StoryRecoverySendDue","recovery"),
    ("_extrasAccumulator","accumulator")]:
    assert condition.count(old)==1, old
    condition=condition.replace(old,new)
(out/'SenderCadence.cs').write_text('using System; namespace GloomhavenVR.Net; internal static partial class RemoteMapStory {\ninternal static readonly int StorySenderCadenceAssertions = VerifyStorySenderCadence();\nprivate static bool SenderDue(bool grabbed,bool turning,bool reflow,bool recovery,float accumulator,float fastInterval) => '+condition+';\nprivate static int VerifyStorySenderCadence() {\nfloat rate=1f/15f;\nif(SenderDue(false,false,false,false,.2f,rate))throw new Exception("idle sender must retain the original five-Hz cadence");\nif(SenderDue(false,false,false,true,0,rate)||SenderDue(false,false,false,true,.01f,rate))throw new Exception("story flight must never exceed the rig rate");\nif(!SenderDue(false,false,false,true,rate,rate))throw new Exception("actual sender expression must publish active story recovery at the rig rate");\nif(!SenderDue(true,false,false,false,rate,rate)||!SenderDue(false,true,false,false,rate,rate)||!SenderDue(false,false,true,false,rate,rate))throw new Exception("existing manual and room-making sender gates remain available");\nConsole.WriteLine("Story sender cadence: 7 actual-expression assertions passed.");return 7;\n}\n}\n')
pins=[root/'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs',root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.StoryRecovery.cs',root/'src/GloomhavenVR/WorldUI/Modal/StoryRecoveryFlight.cs',root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.3.WindowPanel.cs']
(out/'sender-source-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in pins},indent=2)+'\n')
