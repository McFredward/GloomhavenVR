#!/usr/bin/env python3
"""Run production rotation/hint code and prove representative regressions fail."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
here = Path(__file__).resolve().parent
dotnet = shutil.which('dotnet') or str(Path(os.environ.get('DOTNET_ROOT', Path.home()/'.dotnet'))/'dotnet')
aoe = root/'src/GloomhavenVR/Board/AoeControl.cs'
hint = root/'src/GloomhavenVR/Compat/Tutorial/TutorialAoeHint.cs'
gesture = root/'src/GloomhavenVR/Board/AoeFaceButtonGesture.cs'
refresh = root/'src/GloomhavenVR/Compat/Tutorial/TutorialAoeHintRefresh.cs'
project = here/'GloomhavenVR.AoeControlTests.csproj'

def run(project_path, **sources):
    return subprocess.run([dotnet, 'run', '--project', str(project_path), '--configuration', 'Release',
                           *[f'--property:{k}={v}' for k, v in sources.items()]],
                          capture_output=True, text=True)

def code(path):
    return re.sub(r'/\*.*?\*/|//[^\n]*', '', path.read_text(), flags=re.S)

dispatch = code(root/'src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs')
assert 'TutorialAoeHint.TryOverride(key, controllerKey, out text)' in dispatch
assert 'TutorialAoeHint.TryOverride(message.TitleKey, message.TitleKeyController, out text)' in dispatch
assert 'TutorialVR.Enabled' in dispatch and 'TutorialVR.IsTutorialActive' in dispatch
assert 'TickGuard.Run("Board.Aoe", AoeControl.Tick)' in code(root/'src/GloomhavenVR/Board/BoardDriver.cs')
assert 'AoeRotationInput = AoeRotationInputMode.UpperFaceButtons;' in code(root/'src/GloomhavenVR/Defaults/Defaults.Board.cs')
options = code(root/'src/GloomhavenVR/WorldUI/Options/VROptionsTab.4.Curated.cs')
assert 'new("Board", "AoeRotationInput", "")' in options
assert 'Loc.Mod("aoe_input_buttons")' in options and 'Loc.Mod("aoe_input_stick")' in options
loc = (root/'src/GloomhavenVR/Core/Loc/Loc.cs').read_text()
for side, german in [('left', 'LINKEN'), ('right', 'RECHTEN')]:
    block = loc.split(f'["tut_vr_aoe_{side}"] = Pair(', 1)[1].split('),', 1)[0]
    assert side.upper() in block and german in block and '60°' in block and 'Kontrollboard' in block
print('AoE source bindings: tutorial bodies/titles, runtime tick and bilingual physical controls passed.')
result = run(project)
assert result.returncode == 0, result.stdout + result.stderr
print(result.stdout.strip())

mutations = [
    ('effect-mode', aoe, 'VRModeStateMachine.CurrentMode != VRMode.Menu2D',
     'VRModeStateMachine.CurrentMode == VRMode.BoardTargeting', 'Native effect targeting in Play'),
    ('authority', aoe, 'if (!LocalTurnControl.ThisSeatActs)',
     'if (!LocalTurnControl.ThisSeatActs && display.AbilityRange < 0)', "Other player's turn"),
    ('turn-hand', aoe, 'return turn == HandSide.Left ? HandSide.Right : HandSide.Left;',
     'return turn;', 'Binding must follow turn configuration'),
    ('object-redraw', aoe, 'display.DisplaySelectObjectPositionAOEStars();', 'display.DisplayAOEStars();',
     'Each effect type must redraw'),
    ('controller-key', hint, 'if (!Matches(key) && !Matches(controllerKey))', 'if (!Matches(key))',
     'Both native hint keys must resolve'),
    ('world-grab', aoe, ' || hand.ThumbstickClick)', ')', 'Clicked stick belongs to world grab must not rotate'),
    ('stick-free', aoe, 'if (!UsesStick) return false;', '', 'Default B/Y mode must leave every locomotion stick axis free'),
    ('native-latch', aoe, 'buttonDisplay.m_TurningRight = step > 0;', '', 'Rapid B/Y alternation must override native keyboard direction latch'),
    ('recenter', gesture, 'if (leftDown && rightDown) _chord = true;', '', 'Same-frame chord must consume both releases'),
    ('target-identity', gesture, ' || !ReferenceEquals(press.Target, target)', '', 'Target change cancels a pending tap'),
    ('live-hint', refresh, 'BoardConfig.AoeRotationInput.SettingChanged += OnChanged;', '', 'Already-open tutorial body and title must refresh immediately'),
]
with tempfile.TemporaryDirectory(prefix='gvr-aoe-test-') as temp:
    dest = Path(temp)
    for source in here.glob('*.cs*'):
        shutil.copy2(source, dest/source.name)
    for name, source, old, new, expected in mutations:
        text = source.read_text()
        assert text.count(old) == 1, name
        changed = dest/(name+'.fixture')
        changed.write_text(text.replace(old, new))
        result = run(dest/project.name,
                     AoeSource=changed if source == aoe else aoe,
                     HintSource=changed if source == hint else hint,
                     BoardSources=aoe.parent,
                     GestureSource=changed if source == gesture else gesture,
                     RefreshSource=changed if source == refresh else refresh)
        assert result.returncode != 0 and expected in result.stdout + result.stderr, (
            name + ' did not fail for the expected reason:\n' + result.stdout + result.stderr)
        print(f'AoE negative control: {name} rejected.')
