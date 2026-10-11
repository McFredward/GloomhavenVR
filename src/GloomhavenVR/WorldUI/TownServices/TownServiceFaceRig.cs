using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Applies authored facial motion after the original body animation has been sampled.
/// Discovery happens once. No native controllers, material clones, or per-frame hierarchy walks.</summary>
internal sealed class TownServiceFaceRig
{
    internal static readonly string[] ShapeNames = { "BlinkLeft", "BlinkRight", "JawOpen", "MouthWide", "MouthRound", "Smile", "BrowRaise", "LidUpLeft", "LidDownLeft", "LidUpRight", "LidDownRight" };
    private readonly Transform _root;
    private readonly Transform? _head, _left, _right;
    private readonly Quaternion _leftRest, _rightRest;
    private readonly List<Binding> _shapes = new();
    private readonly float[] _lastWeights = new float[11];
    private bool _weightsApplied;
    internal bool Complete { get; }
    private readonly struct Binding
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly int Index, Shape;
        internal Binding(SkinnedMeshRenderer renderer, int index, int shape)
        { Renderer = renderer; Index = index; Shape = shape; }
    }
    private Quaternion _sampledHead;
    private bool _applied;
    internal Transform Head => _head != null ? _head : _root;
    internal void BeforeBodySample()
    {
        if (_applied && _head != null) _head.localRotation = _sampledHead;
        if (_left != null) _left.localRotation = _leftRest;
        if (_right != null) _right.localRotation = _rightRest;
        _applied = false;
    }
    internal Quaternion OpticalRotation => _left != null ? _left.rotation : _root.rotation;
    internal Vector3 HeadPosition => _head != null ? _head.position : EyePosition;
    internal bool Ready => _head != null && _left != null && _right != null;
    internal Vector3 EyePosition => _left != null && _right != null
        ? (_left.position + _right.position) * .5f : _root.TransformPoint(new Vector3(0f, 1.6f, .65f));
    internal Vector3 LeftPosition => _left != null ? _left.position : EyePosition;
    internal Vector3 RightPosition => _right != null ? _right.position : EyePosition;
    internal TownServiceFaceRig(Transform root)
    {
        _root = root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Head") _head = child;
            else if (child.name == "EyeLeft") _left = child;
            else if (child.name == "EyeRight") _right = child;
        }
        _leftRest = _left != null ? _left.localRotation : Quaternion.identity;
        _rightRest = _right != null ? _right.localRotation : Quaternion.identity;
        bool complete = true; int facialRenderers = 0;
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null) continue;
            bool facial = mesh.GetBlendShapeIndex("BlinkLeft") >= 0 || mesh.GetBlendShapeIndex("JawOpen") >= 0;
            if (facial) facialRenderers++;
            for (int shape = 0; shape < ShapeNames.Length; shape++)
            {
                int index = mesh.GetBlendShapeIndex(ShapeNames[shape]);
                if (index >= 0) _shapes.Add(new Binding(renderer, index, shape));
                else if (facial && shape < 7) complete = false;
            }
        }
        // Completeness describes the available anatomy, not the number of LODs.
        // Fixed-detail assets have one skin; older bundles may retain several.
        Complete = Ready && complete && facialRenderers > 0;
    }
    internal void Apply(in TownServiceFacePose pose)
    {
        if (!Ready) return;
        _sampledHead = _head!.localRotation; _applied = true;
        // Imported bone and station axes are not optical axes. Use the authored neutral
        // eye frame after body sampling; the NPC can face inward along station -Z.
        Quaternion frame = OpticalRotation;
        _head!.rotation = frame * Quaternion.Euler(pose.HeadPitch, pose.HeadYaw, pose.HeadRoll)
            * Quaternion.Inverse(frame) * _head.rotation;
        _left!.localRotation = _leftRest * Quaternion.Euler(pose.LeftPitch, pose.LeftYaw, 0f);
        _right!.localRotation = _rightRest * Quaternion.Euler(pose.RightPitch, pose.RightYaw, 0f);
        for (int shape = 0; shape < ShapeNames.Length; shape++)
        {
            float weight = pose.Weight(shape);
            if (_weightsApplied && weight == _lastWeights[shape]) continue;
            _lastWeights[shape] = weight;
            foreach (Binding binding in _shapes)
                if (binding.Shape == shape && binding.Renderer != null)
                    binding.Renderer.SetBlendShapeWeight(binding.Index, weight * 100f);
        }
        _weightsApplied = true;
    }
}

internal struct TownServiceFacePose
{
    internal float HeadYaw, HeadPitch, HeadRoll, LeftYaw, LeftPitch, RightYaw, RightPitch;
    internal float BlinkLeft, BlinkRight, JawOpen, MouthWide, MouthRound, Smile, BrowRaise;
    internal float Weight(int index) => index == 0 ? BlinkLeft : index == 1 ? BlinkRight : index == 2 ? JawOpen
        : index == 3 ? MouthWide : index == 4 ? MouthRound : index == 5 ? Smile : index == 6 ? BrowRaise
        : index == 7 ? Mathf.Clamp01(-LeftPitch / 15f) * (1f - BlinkLeft)
        : index == 8 ? Mathf.Clamp01(LeftPitch / 15f) * (1f - BlinkLeft)
        : index == 9 ? Mathf.Clamp01(-RightPitch / 15f) * (1f - BlinkRight)
        : Mathf.Clamp01(RightPitch / 15f) * (1f - BlinkRight);
}
