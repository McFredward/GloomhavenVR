using System;
using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

internal static partial class WorldMaterialBudget
{
    private struct WorldScope { internal bool Valid, Generated, Scenario, Registered; }
    private static bool NativeWorldCoordinator(Component component)
    {
        // All 15 shipped procedural maps and 114 editor maps carry the exact
        // ApparanceMap/ProceduralMapConfig tuple. The native ProcGen Maps root
        // carries the other two coordinators (level8 pathID2). They retain native
        // metadata, placement completion or light-shadow bookkeeping; none moves
        // renderer vertices or turns a world renderer into an interactive actor.
        // Excluding them vetoed every real room while the 64-source terrain factory
        // still drew private variants: camera priority switched walls between dark
        // native lighting and pale raw albedo. Keep exact types, not subclasses or
        // an arbitrary Behaviour exemption. Their native callbacks remain running.
        Type type = component.GetType();
        return type == typeof(ApparanceMap) || type == typeof(ProceduralMapConfig)
            || type == typeof(ProceduralPlacementNotifierHandler) || type == typeof(LightShadowsModifierController);
    }
    private sealed partial class Driver
    {
        private readonly Dictionary<Transform, WorldScope> _scopes = new();
        private readonly List<Transform> _ancestry = new();
        private readonly List<Component> _components = new();
        private readonly Dictionary<int, bool> _sceneScopes = new();
        private bool InWorld(MeshRenderer renderer)
        {
            if (HeldProps.OwnsRendererOf(renderer.transform) || PropGrab.OwnsRendererOf(renderer.transform)) return false;
            _ancestry.Clear();
            WorldScope scope = new() { Valid = true };
            for (Transform? node = renderer.transform; node != null; node = node.parent)
            {
                if (_scopes.TryGetValue(node, out scope)) break;
                scope = new WorldScope { Valid = true }; _ancestry.Add(node);
            }
            for (int i = _ancestry.Count - 1; i >= 0; i--)
            {
                Transform node = _ancestry[i];
                node.GetComponents(_components);
                bool allowed = node.name != "Preview";
                foreach (Component component in _components)
                {
                    if (component is null) continue;
                    if (component is Canvas or ActorBehaviour or ProceduralProp or ProceduralDoorway
                        or UnityGameEditorDoorProp or CInteractable or Animator or Animation or Rigidbody
                        or SkinnedMeshRenderer or ParticleSystem or ParticleSystemRenderer or VideoPlayer
                        or Light or Projector) allowed = false;
                    // Real native tiles require ProceduralStyle/ApparanceEntity.
                    // They generate geometry through the existing content/placement
                    // boundaries; the experimental periodically animated style stays
                    // entirely native. Never disable their generators or callbacks.
                    if (component is ProceduralStyle style && style.AnimateStyle) allowed = false;
                    // Positive provenance does not permit arbitrary scripted animated
                    // descendants. Known native world/visibility generators are the
                    // explicit exception; their presentation and write hooks stay live.
                    if (component is Behaviour && component is not ProceduralBase
                        && component is not ProceduralScenario && component is not RoomVisibilityTracker
                        && component is not TilesOcclusionVolume && component is not MaterialLoader
                        && component is not MapChoreographer && component is not ProceduralStyle
                        && component is not ApparanceEntity && !NativeWorldCoordinator(component)) allowed = false;
                    scope.Scenario |= component is ProceduralScenario;
                }
                _components.Clear();
                scope.Valid &= allowed;
                scope.Generated |= node.name == "Generated Content";
                scope.Registered |= _worldRoots.Contains(node);
                _scopes[node] = scope;
            }
            _ancestry.Clear();
            if (scope.Valid && scope.Generated && !scope.Scenario)
            {
                Scene scene = renderer.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) return false;
                if (!_sceneScopes.TryGetValue(scene.handle, out bool scenario))
                {
                    scene.GetRootGameObjects(_roots);
                    foreach (GameObject root in _roots) if (root.GetComponent<ProceduralScenario>() != null) { scenario = true; break; }
                    _roots.Clear(); _sceneScopes.Add(scene.handle, scenario);
                }
                scope.Scenario = scenario;
            }
            return scope.Valid && (scope.Registered || scope.Generated && scope.Scenario);
        }
    }
}
