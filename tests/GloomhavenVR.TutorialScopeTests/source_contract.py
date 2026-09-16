"""Bind the tested scope/hold policies to production lesson entry and cleanup paths."""
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
base = root / 'src/GloomhavenVR/Compat/Tutorial'
checks = 0


def source(relative):
    # A comment containing the old gate must not satisfy a production binding.
    return re.sub(r'/\*[\s\S]*?\*/|//[^\n]*', '', (base / relative).read_text())


def require(condition, message):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(message)


controls = source('Controls/ControlsTutorial.cs')
for method in ['RequestForTutorial', 'NoteMessageDismissed']:
    require(re.search(r'void ' + method + r'\([^)]*\)\s*\{\s*if\s*\([^;{}]*!TutorialLessonScope.IsActive[^;{}]*\)\s*return;', controls),
            method + ' must reject non-first tutorials before changing lesson state')
for method in ['TickCore', 'OnAction']:
    require(re.search(r'void ' + method + r'\(\)\s*\{\s*if\s*\(!TutorialLessonScope.IsActive\)\s*\{\s*Stop\([^;]+;\s*return;', controls),
            method + ' must retire an old lesson immediately on scope loss')
require('TutorialChainHold.Release(HoldOwner, reason);' in controls,
        'Stopping the lesson must release its native message hold')
grab = source('TutorialGrabStep.cs')
require(re.search(r'void NoteDismissed\([^)]*\)\s*\{\s*if\s*\([^;{}]*!TutorialLessonScope.IsActive[^;{}]*\)\s*return;', grab),
        'Figure-grab admission must be first-tutorial-only')
require(re.search(r'bool inTutorial =[^;]+TutorialLessonScope.IsActive[^;]+;\s*if \(!inTutorial\)\s*\{\s*Finish\(', grab),
        'Figure-grab scope loss must close its pending step')
require('TutorialChainHold.Release(HoldOwner, reason);' in grab,
        'Finishing the extra figure step must release its hold')
flow = source('TutorialFlowPatches.cs')
for reset in ['ControlsTutorial.Reset();', 'TutorialCameraSkip.Reset();', 'TutorialGrabStep.Reset();', 'TutorialCardNames.Reset();']:
    require(flow.index(reset) < flow.index('if (!TutorialVR.Enabled'),
            'Every scripted-level boundary must reset old tutorial state before admission: ' + reset)
require(re.search(r'if \(TutorialLessonScope.IsActive\)\s*ControlsTutorial.RequestForTutorial', flow),
        'Scenario startup must only queue lessons for the first tutorial')
skip = source('TutorialCameraSkip.cs')
require(re.search(r'if \(!TutorialLessonScope.IsActive\)\s*\{\s*Clear\(\);\s*return;', skip),
        'Camera skip retries must retire on scope loss')
require(re.search(r'bool ShouldSkip\([^)]*\)\s*\{\s*why = string.Empty;\s*if \(!TutorialLessonScope.IsActive\)\s*return false;', skip),
        'Later tutorial camera pages must retain their native flow')
for file in ['TutorialVR.cs', 'TutorialHintPatches.cs']:
    require('TutorialLessonScope' not in source(file),
            'Generic VR input and text adapters must still work in later tutorials: ' + file)
print(f'Tutorial scope production bindings: {checks} assertions passed.')
