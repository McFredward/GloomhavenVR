#!/usr/bin/env python3
"""Guard the scenario-figure compromise boundaries; Unity runtime proof is a separate gate."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / 'src/GloomhavenVR/Core/Perf/ScenarioFigureDetailBudget.cs'
source = path.read_text()
checks = 0

def require(value, message):
    global checks
    checks += 1
    if not value:
        raise AssertionError(message)

for token in (
    'HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor)',
    'CActor.EType.Player or CActor.EType.HeroSummon',
    'native.IsMonsterType',
    'root.scene != scene',
    'root.GetComponent<ProceduralScenario>()',
    '_scenarioScopes.TryGetValue(scene.handle, out bool known)',
    'if (!VRSession.IsRunning || !BudgetActive)',
    'if (_seen.Contains(actor.GetInstanceID())) continue;',
    'ActorBehaviour.SetActor',
    'm_RootGameObject',
    'm_ForcingPositionChangeCounter',
    'if (!forcingPosition && !Cloth.enabled) Cloth.enabled = true;',
    'if (Cloth.enabled) { Cloth.enabled = false; Owned = true; }',
    'if (!SameTable(current, Applied ?? Original))',
    'if (Group == null || !SameTable(Group.GetLODs(), Applied))',
    'if (coarse > 0 && coarse < full) admitted.Add(i);',
    'if (unsafeLevel) continue;',
    'Group.SetLODs(levels);',
    'levels[i].renderers = Original[selected].renderers;',
    'MaskOmittedRenderers(levels);',
    '!retained.Contains(renderer)',
    '&& !renderer.forceRenderingOff',
    'if (renderer != null && renderer.forceRenderingOff) renderer.forceRenderingOff = false;',
    'try { lod.Restore(); } catch',
    'catch (Exception error) { Fail(error); }',
    'if (_faulted) return;',
    'enabled = false;',
    '_lods[_ownershipCursor++].CheckOwnership();',
    'if (VRLog.WantsDebug)',
):
    require(token in source, 'Missing runtime boundary: '+token)
for forbidden in ('renderer.enabled =', 'Animator.enabled =', 'Collider.enabled =',
                  'SetActive(', '.sharedMesh =', '.sharedMaterials =', 'ForceLOD(',
                  'ScenarioRuleLibrary.', 'Type.Enemy ='):
    require(forbidden not in source, 'Unexpected native presentation/gameplay write: '+forbidden)
require('ScenarioFigureDetailBudget' not in (ROOT/'src/GloomhavenVR/Core/FigureRendererGuard.cs').read_text(),
        'Independent figure compromise must never relax the wall/visibility actor exemption')
runner = (ROOT/'scripts/check-scenario-figure-detail-runtime.py').read_text()
require("'Figures.cs': figures.replace('Time.unscaledTime', 'FigureClock.Now')" in runner,
        'Runtime gate must execute the complete production driver with clock-only substitution')
require('Compilation failure is not a passing negative control' in runner,
        'Negative controls must compile and fail the intended behavioral assertion')
require("'miss-steady-ownership'" in runner and "'cancel-native-reset'" in runner,
        'Ownership and native cloth lifecycle require negative controls')
print(f'PASS: scenario figure-detail boundaries ({checks} assertions; real Unity runtime proof is separate)')
