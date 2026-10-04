#!/usr/bin/env python3
"""Guard the scenario-figure compromise boundaries; Unity runtime proof is a separate gate."""
from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / 'src/GloomhavenVR/Core/Perf/ScenarioFigureDetailBudget.cs'
source = path.read_text()
effects = (ROOT / 'src/GloomhavenVR/Core/Perf/ScenarioFigureEffects.cs').read_text()
checks = 0

def require(value, message):
    global checks
    checks += 1
    if not value:
        raise AssertionError(message)

for token in (
    'bool cloth = restore || PerfConfig.FigureClothSimulationEnabled;',
    'int wanted = restore ? 100 : Mathf.Clamp(ActorDetail(Actor), 0, 100);',
    'FigureCloth.TakeDisabledSimulationOwnership(item.Cloth)',
    '[DefaultExecutionOrder(29900)]',
    'ClientScenarioManager.s_ClientScenarioManager',
    'root.transform.IsChildOf(board.transform)',
    'CActor.EType.Player or CActor.EType.HeroSummon',
    'native.IsMonsterType',
    'if (!IsScenarioActorRoot(root, scene))',
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
    'PerfConfig.FigureEffectsDensityPercent < 100',
    'record.Effects = ScenarioFigureEffects.Record.Capture(root, actor);',
    'Effects.Apply(restore ? 100 : PerfConfig.FigureEffectsDensityPercent);',
    'try { record.Effects.Restore(); } catch',
    'renderer.isVisible',
    '!renderer.forceRenderingOff && renderer.isVisible',
    'last-frame visible unmasked body meshes/vertices',
    '_roots.TryGetValue(current.gameObject.GetInstanceID(), out ActorRecord record)',
    'depth++ < 64',
    'record.Effects.MaterialReady(renderer, record.Root, record.Actor)',
    'MaterialLoaderData_CheckAllMaterialLoaded_FigureEffectsPatch',
    'if (renderer != null && renderer.enabled) ScenarioFigureDetailBudget.MaterialReady(renderer);',
):
    require(token in source, 'Missing runtime boundary: '+token)
for forbidden in ('renderer.enabled =', 'Animator.enabled =', 'Collider.enabled =',
                  'SetActive(', 'ForceLOD(',
                  'ScenarioRuleLibrary.', 'Type.Enemy ='):
    require(forbidden not in source and forbidden not in effects, 'Unexpected native presentation/gameplay write: '+forbidden)
for property in ('sharedMesh', 'sharedMaterials'):
    require(not re.search(r'\.' + property + r'\s*=(?!=)', source + effects),
            'Native asset assignment is forbidden: '+property)
for token in ('P_WindDemon_Idle', 'P_SunDemon_Idle', 'P_NightDemon_Idle', 'P_FlameDemon',
              'MO_WindDemon_Alpha_MAT', 'MO_FlameDemon_Alpha_MAT',
              'nativeBody.sharedMesh.vertexCount > 0',
              'system.main.stopAction == ParticleSystemStopAction.None',
              '&& !system.collision.enabled && !system.trigger.enabled',
              'MayPause && System.isPlaying && !System.isPaused',
              'System.Pause(false); Paused = true;', 'if (System.isPaused) System.Play(false);',
              'Mask?.Apply(reduce);', 'if (!Renderer.forceRenderingOff)',
              'if (Renderer.emitting) { Renderer.emitting = false; OwnedEmission = true; }'):
    require(token in effects, 'Missing identified ambient/state ownership boundary: '+token)
require('name.EndsWith("(Instance)", StringComparison.Ordinal)' in effects,
        'Native renderer material instancing must retain authored alpha provenance')
require('if (density == 100 && _density == 100) return;' in effects,
        'Restored FX must bypass per-frame particle and renderer reads while LOD/cloth remains active')
# Independent read-only original asset metadata covers all exported model variants, not a
# test graph assembled from the production dictionary. Every model/effect admission must
# occur in a shipped prefab, and all known noncombat resident containers must be admitted.
provenance = json.loads((ROOT/'tests/GloomhavenVR.ScenarioSceneryBudgetTests/NativeDetailProvenance.json').read_text())
catalog = {model: set(json.loads('['+names+']')) for model, names in
           re.findall(r'\["([^"]+)"\] = new\(StringComparer.Ordinal\) \{ (.*?) \},', effects)}
native = {record['model']: record for record in provenance['models']}
combat = re.compile(r'attack|condition|invisib|projectile|hit|heal|ranged|release|buildup', re.I)
for model, record in native.items():
    authored = {name.rstrip() for part in record['particles'] for name in part['path'].split('/')[1:]}
    expected = {name for name in authored if (name.startswith(('P_', 'p_')) or 'idle' in name.lower()
                or name in ('ManaSphere_FX', 'Hail_FX', 'Hail_Censer_FX')) and not combat.search(name)}
    if model == 'SU_HealingSprite_PR': expected.add('P_HealingSprite')
    require(catalog.get(model, set()) == expected, 'Original resident FX coverage/provenance differs for '+model)
for model, names in catalog.items():
    require(model in native, 'Unknown model added to cosmetic provenance: '+model)
    require(not any(name.startswith('P_Gain') for name in names), 'Combat buff prefab admitted as resident cosmetic')
require(len(native) == 190 and len(provenance['actor_bundles']) == 188,
        'Original actor census must retain every hero/NPC bundle and exported normal/elite/summon model')
mesh_catalog = dict(re.findall(r'\["([^"|]+\|[^\"]+)"\] = "([^"]+)",', effects))
native_meshes = {record['model']+'|'+part['path'].split('/')[-1]: part['materials'][0]
                for record in native.values() for part in record['mesh_fx']
                if part['kind'] in ('MeshRenderer', 'SkinnedMeshRenderer', 'LineRenderer')
                and len(part['materials']) == 1 and part['materials'][0] != '<external>'}
require(mesh_catalog == native_meshes, 'Optional mesh-material pairs must exactly match original resident FX provenance')
require('!supplemental && !KnownAmbientMesh(renderer, root.transform)' in effects,
        'Mesh FX require exact original model/effect/material provenance rather than a blanket shader rule')
for forbidden in ('System.Stop(', 'System.Clear(', '.Simulate(', '.SetActive(',
                  '.collision.enabled =', '.trigger.enabled =', '.loop =', '.StopAction ='):
    require(forbidden not in effects, 'Ambient reduction must preserve native clocks/gameplay/callbacks: '+forbidden)
require('ScenarioFigureDetailBudget' not in (ROOT/'src/GloomhavenVR/Core/FigureRendererGuard.cs').read_text(),
        'Independent figure compromise must never relax the wall/visibility actor exemption')
runner = (ROOT/'scripts/check-scenario-figure-detail-runtime.py').read_text()
require("'Figures.cs': figures.replace('Time.unscaledTime', 'FigureClock.Now')" in runner,
        'Runtime gate must execute the complete production driver with clock-only substitution')
require('Compilation failure is not a passing negative control' in runner,
        'Negative controls must compile and fail the intended behavioral assertion')
require("'miss-steady-ownership'" in runner and "'cancel-native-reset'" in runner
        and "'reject-native-game-board'" in runner and "'forget-disabled-cook-claim'" in runner,
        'Ownership and native cloth lifecycle require negative controls')
require("sources['Effects.cs']" in runner and "'-nographics'" not in runner
        and "'keep-particle-solver-running'" in runner and "'skip-particle-mask'" in runner,
        'Complete ambient helper must run with real graphics and simulation/render negative controls')
bank = (ROOT/'src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs').read_text()
for token in ('ScenarioFigureMeshBank.Prepare(players);', 'mesh.Apply(meshDetail);',
              'mesh.Restore();', 'actual != null ? actual.vertexCount : 0',
              'renderer.GetComponent<Cloth>() == null', 'Verified derivatives='):
    require(token in source, 'Offline original-body derivative boundary: '+token)
for token in ('Distance.Select(wanted, bounds, head.transform.position,',
              'HeldFigures.Owns(Actor) || NetHeldFigures.Owns(Actor)',
              'skin.Apply(restore);', 'PerfConfig.MaximumSkinningBones > 0'):
    require(token in source, 'Optional distance/skinning must retain cap, held and exact restoration boundaries: '+token)
for token in ('if (current != (_applied ?? Original))', 'Resolve(Original, detail) ?? Original',
              'detail >= 100', 'AssetBundle.LoadFromFile(path)',
              'SourceKeys.Count >= 2048', 'Missing.Count < 32'):
    require(token in bank, 'Owned reversible shared mesh boundary: '+token)
require('ScenarioFigureMeshTopology' not in source+bank,
        'Expensive mesh simplification is offline only')
require('.Unload(' not in bank and '.Destroy(' not in bank,
        'Shared immutable derivatives must outlive local/remote visual twins')
print(f'PASS: scenario figure-detail boundaries ({checks} assertions; real Unity runtime proof is separate)')
