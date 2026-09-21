using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Stable terrain contact offsets resolved by the resident author, never by observers.</summary>
internal sealed class TownServiceGrounding
{
    private readonly Transform _station;
    private readonly Transform? _actor;
    private readonly Vector3 _actorRest;
    private readonly List<Support> _supports = new();
    private readonly List<Vector3> _footprint = new();
    private bool _applied;
    private float _actorOffset, _furnitureBottom;

    private readonly struct Support
    {
        internal readonly Transform Part;
        internal readonly Vector3 Position, Scale;
        internal Support(Transform part)
        { Part = part; Position = part.localPosition; Scale = part.localScale; }
    }

    internal TownServiceGrounding(Transform station)
    {
        _station = station;
        _actor = station.Find("Actor");
        _actorRest = _actor != null ? _actor.localPosition : Vector3.zero;
        Transform? furniture = station.Find("Counter") ?? station.Find("Shrine") ?? station.Find("Workbench");
        if (furniture == null) return;
        Add(furniture.Find("FootPlinth"));
        Add(furniture.Find("AltarPlinth"));
        foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 }) Add(furniture.Find("WorkbenchLeg" + x + z));
    }

    private void Add(Transform? part)
    {
        if (part == null) return;
        _supports.Add(new Support(part));
        // Authored support cubes have their actual contacting footprint at local Y=-.5.
        // Save it before stretching: repeated resolves must not sample a previous correction.
        foreach (float x in new[] { -.5f, .5f })
            foreach (float z in new[] { -.5f, .5f })
                _footprint.Add(_station.InverseTransformPoint(part.TransformPoint(new Vector3(x, -.5f, z))));
    }

    internal void Resolve(out float actorOffset, out float furnitureBottom)
    {
        actorOffset = furnitureBottom = 0f;
        Transform? room = SkyAlternative.PlacedRoomRoot;
        if (room == null) return;
        // The final prefab's sole correction is already baked into Actor.localPosition.y.
        // Resolve the separate terrain delta at stable foot anchors, not animated bounds/hands.
        if (_actor != null)
            actorOffset = Mathf.Min(Height(room, new Vector3(-.12f, 0f, .65f)),
                Height(room, new Vector3(.12f, 0f, .65f)));
        if (_footprint.Count == 0) return;
        furnitureBottom = float.PositiveInfinity;
        foreach (Vector3 point in _footprint) furnitureBottom = Mathf.Min(furnitureBottom, Height(room, point));
    }

    private float Height(Transform room, Vector3 local)
    {
        Vector3 world = _station.TransformPoint(local);
        world.y = TownServicePlacement.GroundHeight(room, world);
        return _station.InverseTransformPoint(world).y;
    }

    internal void Apply(float actorOffset, float furnitureBottom)
    {
        if (!Finite(actorOffset) || !Finite(furnitureBottom)) return;
        if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom) return;
        _applied = true; _actorOffset = actorOffset; _furnitureBottom = furnitureBottom;
        if (_actor != null) _actor.localPosition = _actorRest + Vector3.up * actorOffset;
        foreach (Support support in _supports)
        {
            if (support.Part == null || support.Part.parent == null) continue;
            // Keep the authored top fixed. Only the concealed lower portion of the existing
            // support reaches down to the lowest floor sample; the work surface never tilts.
            Vector3 worldBottom = _station.TransformPoint(new Vector3(0f, furnitureBottom, 0f));
            float bottom = support.Part.parent.InverseTransformPoint(worldBottom).y;
            float top = support.Position.y + support.Scale.y * .5f;
            Vector3 position = support.Position, scale = support.Scale;
            scale.y = Mathf.Max(.001f, top - bottom);
            position.y = top - scale.y * .5f;
            support.Part.localScale = scale;
            support.Part.localPosition = position;
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
