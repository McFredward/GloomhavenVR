"""Bind real first-pose, corrected-arrival, native callbacks and board reset to tested code."""
from pathlib import Path
import re
import sys
root = Path(sys.argv[1])
base = root / 'src/GloomhavenVR/Rig'
def source(name):
    return re.sub(r'/\*[\s\S]*?\*/|//[^\n]*', '', (base / name).read_text())
checks = 0
def require(condition, message):
    global checks
    checks += 1
    assert condition, message
rig = source('VRRigDriver.cs')
require('_scenarioStart.EnterScenario(Choreographer.s_Choreographer)' in rig, 'Native scenario identity must scope cached arrival')
require(re.search(r'float scale = _scenarioStart.RestorePending\s*\? _scenarioStart.Pose.Scale\s*: baseScale \* ComfortSettings.ClampedSavedMultiplier', rig), 'Retry camera must begin at original scale')
require('bool arrival = newScenario || _priorKind != RigKind.Scenario;' in rig, 'Direct scenario replacement must count as arrival')
require('if (arrival && !_scenarioStart.RestorePending)' in rig, 'Retry must preserve arrival azimuth')
require(re.search(r'if \(_kind == RigKind.Scenario && _scenarioStart.RestorePending\)\s*\{\s*RestoreScenarioStart\(\);\s*\}\s*else if \(_kind == RigKind.Scenario\)', rig), 'Retry must bypass a fresh peer seat solve')
require(re.search(r'if \(!_ringPlaced\)\s*\{\s*Recenter\(\);\s*if \(_kind == RigKind.Scenario && !_scenarioStart.HasPose\)\s*RememberScenarioStart\(\);', rig), 'Initial fallback must be captured without replacing original on rebuild')
recenter = source('VRRigDriver.Recenter.cs')
require(re.search(r'_ringSeatAngleDegrees = seat.AngleDegrees;\s*_ringSeatAngleValid = true;\s*RememberScenarioStart\(\);', recenter), 'Final ring correction must update captured seat')
manual = recenter[recenter.index('internal static void RequestRecenter()'):recenter.index('internal void Recenter()')]
require('_scenarioStart' not in manual and 'RestoreScenarioStart' not in manual, 'B+Y must keep its existing semantics')
module = source('RigModule.cs')
for name in ['ScenarioRetryStart_Patch', 'ScenarioRetryDestination_Patch']:
    require(f'VRSession.Harmony?.PatchAll(typeof({name}));' in module, f'{name} must be registered')
pose = source('VRRigDriver.RetryStart.cs')
require('HarmonyPatch(typeof(SceneController), "LoadSceneCoroutine")' in pose, 'Native non-scenario destinations must retire canceled retries')
require('IsHost' not in pose and 'FFSNetwork' not in pose, 'Retry pose must apply independently to non-host participants')
tray = re.sub(r'/\*[\s\S]*?\*/|//[^\n]*', '', (root/'src/GloomhavenVR/Cards/Tray/PlayTray.1.Core.cs').read_text())
watch = re.sub(r'/\*[\s\S]*?\*/|//[^\n]*', '', (root/'src/GloomhavenVR/Cards/Tray/PlayTray.2.Watchdog.cs').read_text())
require('_retryScenarioOwner = Choreographer.s_Choreographer;' in tray, 'Tray adoption must record its native scenario owner')
require(re.search(r'_placementDeferLogged = false;\s*if \(TryRestoreRetryStart\(\)\)\s*return;', tray), 'Deferred first placement must consume retry pose before mutable offsets')
require(re.search(r'RememberRetryStart\(\);\s*LogBoardFaceDiagnostics', tray), 'Initial board placement must capture before user interaction')
require(re.search(r'TickArrivalSeatGuard\(\);\s*TryRestoreRetryStart\(\);\s*RememberRetryStart\(\);', watch), 'Surviving tray must restore after current pin and arrival handling')
require('"RestartScenario"' in pose and '__originalMethod.Name == "RestartScenario"' in pose, 'Round restart must use preserve-only lifetime')
print(f'Retry start production bindings: {checks} assertions passed.')
