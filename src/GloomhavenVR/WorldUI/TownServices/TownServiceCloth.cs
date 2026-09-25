using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Bounded, owner-authored cloth on the priestess and enchantress tables.
/// The tabletop rows stay pinned. Two damped edge controls react to spherical hand
/// and mask contact; TLV79 carries those controls so observers reproduce the same
/// intermediate drape. Colliders follow the rendered mesh on the Ignore Raycast
/// layer and cannot become invisible cabinet or service laser targets.</summary>
internal sealed class TownServiceCloth : IDisposable
{
    private sealed class Runner
    {
        internal sealed class Decoration
        {
            internal MeshFilter Filter = null!;
            internal Mesh Mesh = null!;
            internal Vector3[] Rest = Array.Empty<Vector3>(), Deformed = Array.Empty<Vector3>();
            internal float[] Freedom = Array.Empty<float>(), Side = Array.Empty<float>();
        }
        internal MeshFilter Filter = null!;
        internal Mesh Mesh = null!;
        internal MeshCollider Collider = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>(), Deformed = Array.Empty<Vector3>();
        internal float[] Freedom = Array.Empty<float>(), Side = Array.Empty<float>();
        internal float LeftX, RightX, BottomY, TopY, FrontZ;
        internal TownClothRunnerState State;
        internal float NextCollider, NextRender;
        internal readonly List<Decoration> Decorations = new();
    }

    private readonly Transform _station;
    private readonly Runner[] _runners;
    private readonly List<int> _peers = new(4);
    private readonly byte _service;
    private bool _disposed;

    internal TownServiceCloth(Transform station, byte service)
    {
        _station = station; _service = service;
        var filters = new List<MeshFilter>(2);
        foreach (MeshFilter filter in station.GetComponentsInChildren<MeshFilter>(true))
            if (filter.name.StartsWith("ClothRunner_", StringComparison.Ordinal)) filters.Add(filter);
        // Imported FBX cloth objects can share the same pivot even when their
        // actual sheets occupy opposite sides of the altar. Sort the mesh
        // centres, not equal pivots: left/right controls must retain the same
        // semantic runner order in the owner's TLV and every observer clone.
        filters.Sort((a, b) => _station.InverseTransformPoint(
                a.transform.TransformPoint(a.sharedMesh.bounds.center)).x
            .CompareTo(_station.InverseTransformPoint(
                b.transform.TransformPoint(b.sharedMesh.bounds.center)).x));
        if (filters.Count != (service == 2 ? 2 : service == 3 ? 1 : 0))
            throw new InvalidOperationException("Town cloth mesh count does not match the authored furniture for service " + service);
        _runners = new Runner[filters.Count];
        for (int i = 0; i < filters.Count; i++) _runners[i] = Build(filters[i]);
    }

    private Runner Build(MeshFilter filter)
    {
        // mesh clones this resident's asset. Neither another station nor the bundle
        // template may be modified by a local cloth contact.
        Mesh mesh = filter.mesh; mesh.MarkDynamic();
        Vector3[] rest = mesh.vertices;
        var runner = new Runner { Filter = filter, Mesh = mesh, Rest = rest,
            Deformed = new Vector3[rest.Length], Freedom = new float[rest.Length], Side = new float[rest.Length] };
        Array.Copy(rest, runner.Deformed, rest.Length);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float frontZ = 0f; int frontCount = 0;
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            if (p.y < .65f) { frontZ += p.z; frontCount++; }
        }
        if (maxY - minY < .3f || maxX - minX < .1f || frontCount == 0)
            throw new InvalidOperationException("Town cloth dimensions do not match the pinned runner source.");
        runner.LeftX = minX; runner.RightX = maxX; runner.BottomY = minY; runner.TopY = maxY;
        runner.FrontZ = frontZ / frontCount;
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            runner.Freedom[n] = Freedom(p, maxY);
            runner.Side[n] = Mathf.Clamp01((p.x - minX) / (maxX - minX));
        }
        foreach (MeshFilter child in filter.GetComponentsInChildren<MeshFilter>(true))
        {
            if (child == filter || !child.name.StartsWith("ClothDecoration_", StringComparison.Ordinal)) continue;
            Mesh childMesh = child.mesh; childMesh.MarkDynamic();
            Vector3[] childRest = childMesh.vertices;
            var decoration = new Runner.Decoration { Filter = child, Mesh = childMesh, Rest = childRest,
                Deformed = new Vector3[childRest.Length], Freedom = new float[childRest.Length],
                Side = new float[childRest.Length] };
            Array.Copy(childRest, decoration.Deformed, childRest.Length);
            for (int n = 0; n < childRest.Length; n++)
            {
                Vector3 p = _station.InverseTransformPoint(child.transform.TransformPoint(childRest[n]));
                decoration.Freedom[n] = Freedom(p, maxY);
                decoration.Side[n] = Mathf.Clamp01((p.x - minX) / (maxX - minX));
            }
            runner.Decorations.Add(decoration);
        }
        // This collider is for ordinary physical objects. Virtual VR hands and
        // masks also use the same sheet geometry as an explicit sphere constraint.
        // Ignore Raycast prevents it from stealing card and cabinet laser hits.
        var collision = new GameObject("ClothCollision") { layer = 2 };
        collision.transform.SetParent(filter.transform, false);
        MeshCollider collider = collision.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh; collider.convex = false;
        runner.Collider = collider;
        return runner;
    }

    private float Freedom(Vector3 p, float top)
    {
        // The top of the old cloth was immobile until the hanging face began:
        // physical hands touching the runner on the table produced no feedback.
        // Each authored column follows the actual curved table lip. Its rear
        // seam remains pinned, the upper weave can shift modestly, and the
        // hanging face has full freedom without dragging through the stone.
        float radiusX = _service == 2 ? .81f : .86f;
        float edge = (_service == 2 ? 0f : .03f)
            - (_service == 2 ? .38f : .46f)
            * Mathf.Sqrt(Mathf.Max(0f, 1f - p.x * p.x / (radiusX * radiusX)));
        float topWeave = Mathf.Clamp01((edge + .145f - p.z) / .145f);
        float hanging = Mathf.Clamp01((top - p.y) / .38f);
        return Mathf.Pow(Mathf.Max(.55f * topWeave, hanging), 1.25f);
    }

    private static void Step(ref Vector2 position, ref Vector2 velocity, Vector2 target, float dt)
    {
        // Semi-implicit spring with bounded substeps remains stable across a VR
        // hitch, while contact lifts the free edge without moving the pinned top.
        float left = Mathf.Min(.08f, Mathf.Max(0f, dt));
        while (left > 0f)
        {
            float h = Mathf.Min(.011f, left); left -= h;
            velocity += (25f * (target - position) - 9f * velocity) * h;
            position += velocity * h;
            position.x = Mathf.Clamp(position.x, -.08f, .08f);
            // The authored free edge is only 4 mm outside the curved lip.
            // A larger inward shift would bury the moving fabric in stone.
            position.y = Mathf.Clamp(position.y, -.06f, .002f);
            velocity = Vector2.ClampMagnitude(velocity, .24f);
        }
    }

    private void Contact(Runner runner, Vector3 world, float physicalRadius, ref Vector2 left, ref Vector2 right)
    {
        Vector3 p = _station.InverseTransformPoint(world);
        // Tracked hand/head positions are in game-world units; the advertised
        // radii are real metres. The prior code divided 6.5 cm by the ~12x map
        // scale, shrinking a hand to a few millimetres in cloth space, smaller
        // than the 20 mm vertex spacing. No contact could pass the nearest-node
        // check in the headset. Convert through the *live* rig scale first.
        float rigScale = VRRigDriver.RigRoot != null
            ? Mathf.Abs(VRRigDriver.RigRoot.lossyScale.x)
            : Mathf.Max(.001f, VRRigDriver.BaseWorldScale);
        float radius = physicalRadius * rigScale
            / Mathf.Max(.0001f, Mathf.Abs(_station.lossyScale.x));
        if (p.y < runner.BottomY - radius || p.y > runner.TopY + radius
            || p.x < runner.LeftX - radius || p.x > runner.RightX + radius) return;
        if (p.z < runner.FrontZ - radius - .08f || p.z > .13f + radius) return;
        float best = radius * radius; int closest = -1; Vector3 nearest = Vector3.zero;
        // The authored mesh has fewer than 700 positions. Scan only after the
        // broad phase succeeds, and compare the actual displaced surface. This
        // makes the top fold and hanging front both respond to a hand or mask.
        for (int i = 0; i < runner.Deformed.Length; i++)
        {
            if (runner.Freedom[i] < .06f) continue;
            Vector3 point = _station.InverseTransformPoint(
                runner.Filter.transform.TransformPoint(runner.Deformed[i]));
            float square = (point - p).sqrMagnitude;
            if (square < best) { best = square; closest = i; nearest = point; }
        }
        if (closest < 0) return;
        float push = Mathf.Min(.055f, radius - Mathf.Sqrt(best));
        Vector2 away = new(nearest.x - p.x, nearest.z - p.z);
        if (away.sqrMagnitude < .00001f) away = new Vector2(0f, nearest.z >= p.z ? 1f : -1f);
        Vector2 movement = away.normalized * push * runner.Freedom[closest];
        // A finger in front of the free edge cannot shove the fabric through
        // the tabletop. Spill that resisted inward displacement sideways.
        if (movement.y > 0f && nearest.y < runner.TopY - .07f)
        {
            movement.x += (nearest.x < (runner.LeftX + runner.RightX) * .5f ? -1f : 1f)
                * movement.y * .7f;
            movement.y = 0f;
        }
        float side = runner.Side[closest];
        left += movement * (1f - side); right += movement * side;
    }

    internal void TickAuthor(float age, float dt, bool visible)
    {
        if (_disposed) return;
        _peers.Clear(); NetAvatarDriver.CollectTownFacePeers(_peers);
        foreach (Runner runner in _runners)
        {
            Vector2 left = Vector2.zero, right = Vector2.zero;
            if (VRRigDriver.HeadCamera != null)
                Contact(runner, VRRigDriver.HeadCamera.transform.position, .14f, ref left, ref right);
            if (VRHands.Left?.HasPose == true)
                Contact(runner, VRHands.Left.Rig.GrabAnchor.position, .065f, ref left, ref right);
            if (VRHands.Right?.HasPose == true)
                Contact(runner, VRHands.Right.Rig.GrabAnchor.position, .065f, ref left, ref right);
            foreach (int peer in _peers)
            {
                if (NetAvatarDriver.TryGetTownFaceHead(peer, out Vector3 head))
                    Contact(runner, head, .14f, ref left, ref right);
                if (NetAvatarDriver.TryGetTownClothHands(peer, out Vector3 l, out Vector3 r,
                    out bool lv, out bool rv))
                {
                    if (lv) Contact(runner, l, .065f, ref left, ref right);
                    if (rv) Contact(runner, r, .065f, ref left, ref right);
                }
            }
            TownClothRunnerState state = runner.State;
            Step(ref state.Left, ref state.LeftVelocity, Vector2.ClampMagnitude(left, .06f), dt);
            Step(ref state.Right, ref state.RightVelocity, Vector2.ClampMagnitude(right, .06f), dt);
            runner.State = state;
            if (visible) Render(runner, age, state);
        }
    }

    internal void TickObserver(float age, float elapsed, in TownClothRunnerState first,
        in TownClothRunnerState second, bool visible)
    {
        if (_disposed) return;
        for (int i = 0; i < _runners.Length; i++)
        {
            Runner runner = _runners[i];
            TownClothRunnerState state = i == 0 ? first : second;
            float prediction = Mathf.Clamp(elapsed, 0f, .12f);
            state.Left += state.LeftVelocity * prediction;
            state.Right += state.RightVelocity * prediction;
            if (visible) Render(runner, age, state);
            runner.State = state;
        }
    }

    internal TownClothRunnerState First => _runners.Length > 0 ? _runners[0].State : default;
    internal TownClothRunnerState Second => _runners.Length > 1 ? _runners[1].State : default;
    internal void SetVisible(bool visible)
    {
        foreach (Runner runner in _runners) runner.Collider.enabled = visible;
    }

    private void Render(Runner runner, float age, in TownClothRunnerState state)
    {
        if (Time.unscaledTime < runner.NextRender) return;
        runner.NextRender = Time.unscaledTime + 1f / 72f;
        Deform(runner.Filter, runner.Mesh, runner.Rest, runner.Deformed,
            runner.Freedom, runner.Side, age, in state);
        foreach (Runner.Decoration decoration in runner.Decorations)
            Deform(decoration.Filter, decoration.Mesh, decoration.Rest, decoration.Deformed,
                decoration.Freedom, decoration.Side, age, in state);
        if (Time.unscaledTime >= runner.NextCollider)
        {
            runner.NextCollider = Time.unscaledTime + .1f;
            runner.Collider.sharedMesh = null;
            runner.Collider.sharedMesh = runner.Mesh;
        }
    }

    private void Deform(MeshFilter filter, Mesh mesh, Vector3[] rest, Vector3[] deformed,
        float[] freedom, float[] sideValues, float age, in TownClothRunnerState state)
    {
        Vector3 localX = filter.transform.InverseTransformVector(_station.TransformVector(Vector3.right));
        Vector3 localZ = filter.transform.InverseTransformVector(_station.TransformVector(Vector3.forward));
        // A low-amplitude shared clock is wind; it continues between packets.
        // Local tracking can only enter through the owner's published controls.
        for (int n = 0; n < rest.Length; n++)
        {
            float influence = freedom[n], side = sideValues[n];
            Vector2 edge = Vector2.Lerp(state.Left, state.Right, side);
            float breeze = .0045f * Mathf.Sin(age * 1.7f + side * 5.1f + _service);
            deformed[n] = rest[n] + influence *
                (localX * (edge.x + breeze) + localZ * edge.y);
        }
        mesh.vertices = deformed;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (Runner runner in _runners)
        {
            if (runner.Collider != null) UnityEngine.Object.Destroy(runner.Collider);
            if (runner.Mesh != null) UnityEngine.Object.Destroy(runner.Mesh);
            foreach (Runner.Decoration decoration in runner.Decorations)
                if (decoration.Mesh != null) UnityEngine.Object.Destroy(decoration.Mesh);
        }
    }
}
