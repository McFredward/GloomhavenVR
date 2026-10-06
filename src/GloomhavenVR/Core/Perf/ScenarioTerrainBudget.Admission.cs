using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class ScenarioTerrainBudget
{
    [Flags]
    private enum ComponentRole { None = 0, Blocked = 1, Structural = 2, Scenario = 4 }
    private static readonly Dictionary<Type, ComponentRole> Roles = new();
    private static readonly Dictionary<string, bool> BlockedNames = new(StringComparer.Ordinal);

    private static ComponentRole Classify(Component component)
    {
        // Only immutable managed type identity is retained. The native component list,
        // parent chain and current name are still read for each distinct node every eye.
        // Add/remove/same-count replacement cannot inherit an old admission verdict.
        Type type = component.GetType();
        bool shared = PerfConfig.SharedEnvironmentMaterialReadsOn;
        if (shared && Roles.TryGetValue(type, out ComponentRole cached)) return cached;
        bool blocked = component is Canvas or ActorBehaviour or ProceduralProp or ProceduralDoorway
            or UnityGameEditorDoorProp or CInteractable or Animator or Rigidbody or Light or SkinnedMeshRenderer;
        ComponentRole role = blocked ? ComponentRole.Blocked : ComponentRole.None;
        if (component is ProceduralWall) role |= ComponentRole.Structural;
        if (component is ProceduralScenario) role |= ComponentRole.Scenario;
        if (shared) Roles[type] = role;
        return role;
    }
    private static bool BlockedName(string nodeName)
    {
        if (PerfConfig.SharedEnvironmentMaterialReadsOn && BlockedNames.TryGetValue(nodeName, out bool cached)) return cached;
        bool blocked = nodeName == "Preview"
            || nodeName.StartsWith("GloomhavenVR", StringComparison.Ordinal) || StructuralBoundary(nodeName);
        // Exact current strings, bounded to avoid retaining arbitrary procedural names.
        if (PerfConfig.SharedEnvironmentMaterialReadsOn && BlockedNames.Count < 1024) BlockedNames[nodeName] = blocked;
        return blocked;
    }
    private static bool FinitePlanes(Plane[] planes)
    {
        foreach (Plane plane in planes)
            if (float.IsNaN(plane.distance) || float.IsInfinity(plane.distance)
                || float.IsNaN(plane.normal.x) || float.IsNaN(plane.normal.y) || float.IsNaN(plane.normal.z)
                || float.IsInfinity(plane.normal.x) || float.IsInfinity(plane.normal.y) || float.IsInfinity(plane.normal.z)
                || plane.normal.sqrMagnitude < .00001f) return false;
        return true;
    }
    private static bool OutsideBounds(Bounds bounds, Plane[] authored, Plane[] left, Plane[] right, bool stereo)
    {
        return !GeometryUtility.TestPlanesAABB(authored, bounds)
            && (!stereo || !GeometryUtility.TestPlanesAABB(left, bounds)
                && !GeometryUtility.TestPlanesAABB(right, bounds));
    }
    private sealed partial class Driver
    {
        private readonly List<Surface> _priority = new();
        private bool _priorityDirty;
        private readonly Plane[] _cameraPlanes = new Plane[6], _leftPlanes = new Plane[6], _rightPlanes = new Plane[6];
        private bool _frustumReady, _stereoFrustum;
        private void PrepareFrustum(Camera camera, bool shared)
        {
            _frustumReady = false;
            if (!shared) return;
            try
            {
                GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix, _cameraPlanes);
                _stereoFrustum = camera.stereoEnabled;
                if (_stereoFrustum)
                {
                    // Keep the union of BOTH eyes and the authored culling override.
                    // Never classify the second eye through the first eye's projection.
                    GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                        * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left), _leftPlanes);
                    GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)
                        * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right), _rightPlanes);
                }
                _frustumReady = FinitePlanes(_cameraPlanes)
                    && (!_stereoFrustum || FinitePlanes(_leftPlanes) && FinitePlanes(_rightPlanes));
            }
            catch { /* Unavailable stereo projections keep original full admission. */ }
        }
        private bool OutsideFrustum(MeshRenderer renderer)
        {
            if (!_frustumReady) return false;
            Bounds bounds = renderer.bounds; // actual current native bounds, never retained
            return OutsideBounds(bounds, _cameraPlanes, _leftPlanes, _rightPlanes, _stereoFrustum);
        }
        private void PreparePriority()
        {
            if (!_priorityDirty) return;
            _priority.Clear(); _priority.AddRange(_surfaces.Values);
            // Immutable original triangle cost is an upper bound on possible savings.
            // Preparation order never gives one room a presentation/visibility advantage:
            // every omitted substitute retains its current original native renderer.
            _priority.Sort((left, right) =>
            {
                int cost = right.OriginalTriangles.CompareTo(left.OriginalTriangles);
                return cost != 0 ? cost : left.Identity.CompareTo(right.Identity);
            });
            _priorityDirty = false;
        }
    }
}
