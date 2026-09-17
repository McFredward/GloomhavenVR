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
