from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
base = root / 'src/GloomhavenVR/Compat/Tutorial/Controls'
for name in ('ControllerVisual', 'ControlsLesson'):
    (dest / (name + '.cs')).write_text((base / (name + '.cs')).read_text())
source = (base / 'ControlsTutorial.cs').read_text()
def method(name):
    start = source.index('    private static void ' + name + '(')
    brace = source.index('{', start)
    level, pos = 1, brace + 1
    # Braces inside interpolation are balanced in these methods.
    while level:
        level += (source[pos] == '{') - (source[pos] == '}')
        pos += 1
    return source[start:pos]
# Execute production orchestration; only its outer lesson state machine is a test host.
(dest / 'ControlsTutorial.cs').write_text('''using UnityEngine;
using GloomhavenVR.Hands;
using GloomhavenVR.Core;
namespace GloomhavenVR.Compat;
internal static class ControlsTutorial {
private static ControllerVisual? _left, _right;
private static bool _controllersUp;
private static int _index = -1;
private static float _nextControllerRetryAt;
internal static void Apply(int index) { _index = index; ApplyStep(index); }
internal static void Tick() => TickControllerVisuals();
internal static void Teardown() { _left?.Hide(); _right?.Hide(); _left = _right = null; _controllersUp = false; }
''' + method('ApplyStep') + method('TickControllerVisuals') + method('SetControllersVisible') + '\n}')
progress = (base / 'ControlsProgress.cs').read_text()
start = progress.index('internal enum ControlAction')
end = progress.index('\n}', start) + 2
(dest / 'ControlAction.cs').write_text('namespace GloomhavenVR.Compat;\n' + progress[start:end])
# No ownership or scope change may bypass restoring both ordinary hands.
for boundary in ('internal static void Stop(string reason)', 'internal static void Reset()'):
    block = source[source.index(boundary):]
    block = block[:block.index('\n    }')]
    assert '_left?.Hide();' in block and '_right?.Hide();' in block, 'Both models must retire on completion and scene reset'
assert 'Stop("the session is shutting down");' in source, 'Shutdown must restore both hands'
assert '        TickControllerVisuals();' in source, 'Running lessons must advance the original model recovery and transitions'
print('Tutorial controller source bindings: 4 passed.')
