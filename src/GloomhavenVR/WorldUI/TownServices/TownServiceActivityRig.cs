using System.Collections.Generic;
using UnityEngine;
using GloomhavenVR.Net;

namespace GloomhavenVR.WorldUI;

/// <summary>Deterministic two-bone contact correction after the native body sample. Imported
/// bone axes are measured from real children; no Humanoid Avatar or mesh writes are required.</summary>
internal sealed class TownServiceActivityRig
{
    private sealed class Arm
    {
        internal Transform Upper = null!, Fore = null!, Hand = null!;
        internal Quaternion RestHand, SampledUpper, SampledFore, SampledHand;
        internal bool Applied;
        internal Vector3 PalmForward, PalmNormal;
        internal Transform? IndexPinch, ThumbPinch, PalmContact, ThumbBase;
        internal Vector3 PalmOffset;
        internal readonly List<Transform> Supports = new();
        internal bool Anatomical;
        internal Transform Grip = null!;
        internal readonly List<Transform> Fingers = new();
        internal readonly List<Quaternion> FingerRest = new();
        internal readonly List<Vector3> CurlAxes = new();
        internal readonly List<float> CurlFactors = new();
    }
    private readonly Transform _root;
    private readonly byte _service;
    private readonly Arm? _left, _right;
    private readonly Transform? _neck, _chest, _actor;
    private readonly float _actorRestHeight;
    private Quaternion _sampledNeck, _sampledChest;
    private bool _applied;
    internal bool Ready => _left != null && _right != null;
    internal TownServiceActivityRig(Transform root, byte service)
    {
        _root = root; _service = service;
        _actor = root.Find("Actor"); _actorRestHeight = _actor != null ? _actor.localPosition.y : 0f; _left = Find(root, "L"); _right = Find(root, "R");
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        { if (child.name == "Neck") _neck = child; else if (child.name == "Chest") _chest = child; }
    }
    internal void BeforeBodySample()
    {
        if (_applied && _chest != null) _chest.localRotation = _sampledChest;
        if (_applied && _neck != null) _neck.localRotation = _sampledNeck;
        Restore(_left); Restore(_right); _applied = false;
    }
    internal void Suspend()
    {
        BeforeBodySample();
        ResetFingers(_left); ResetFingers(_right);
    }
    private static void ResetFingers(Arm? arm)
    {
        if (arm == null) return;
        for (int n = 0; n < arm.Fingers.Count; n++)
            if (arm.Fingers[n] != null) arm.Fingers[n].localRotation = arm.FingerRest[n];
    }
    private static void Restore(Arm? arm)
    {
        if (arm == null || !arm.Applied) return;
        arm.Upper.localRotation = arm.SampledUpper; arm.Fore.localRotation = arm.SampledFore;
        arm.Hand.localRotation = arm.SampledHand; arm.Applied = false;
    }
    private static Arm? Find(Transform root, string side)
    {
        var arm = new Arm();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "UpperArm." + side) arm.Upper = t;
            else if (t.name == "Forearm." + side) arm.Fore = t;
            else if (t.name == "Hand." + side) arm.Hand = t;
            else if (t.name == "PalmContact." + side) arm.PalmContact = t;
            else if ((t.name.StartsWith("Thumb") || t.name.StartsWith("Index") || t.name.StartsWith("Middle") || t.name.StartsWith("Ring") || t.name.StartsWith("Little")) && t.name.EndsWith("." + side) && !t.name.Contains("Tip") && !t.name.Contains("Pad"))
            { arm.Fingers.Add(t); arm.FingerRest.Add(t.localRotation); }
        }
        if (arm.Upper == null || arm.Fore == null || arm.Hand == null) return null;
        arm.RestHand = arm.Hand.localRotation;
        Transform? index = null, little = null;
        foreach (Transform finger in arm.Fingers)
        {
            if (finger.name == "Index2." + side) arm.IndexPinch = finger;
            if (finger.name == "Thumb1." + side) arm.ThumbBase = finger;
            if (finger.name == "Thumb3." + side) arm.ThumbPinch = finger;
            if (finger.name == "Index1." + side) index = finger;
            if (finger.name == "Little1." + side) little = finger;
        }
        arm.Grip = new GameObject(side == "L" ? "ActivityGripLeft" : "ActivityGripRight").transform;
        arm.Grip.SetParent(root, false);
        foreach (Transform finger in arm.Fingers)
            if (finger.name == "Middle1." + side) arm.PalmForward = arm.Hand.InverseTransformDirection(finger.position - arm.Hand.position).normalized;
        if (arm.PalmForward.sqrMagnitude < .5f) arm.PalmForward = Vector3.up;
        Vector3 across = index != null && little != null ? arm.Hand.InverseTransformDirection(index.position - little.position) : Vector3.right;
        arm.PalmNormal = Vector3.Cross(arm.PalmForward, across).normalized * (side == "L" ? -1f : 1f);
        if (arm.PalmContact != null)
        {
            // Authored against the actual anatomical surface, then attached to Hand in
            // the neutral imported pose. No guessed wrist length or shared actor offset.
            arm.Anatomical = true;
            arm.Supports.Add(arm.PalmContact);
            arm.PalmOffset = arm.Hand.InverseTransformPoint(arm.PalmContact.position);
            arm.PalmForward = arm.Hand.InverseTransformDirection(arm.PalmContact.up).normalized;
            arm.PalmNormal = arm.Hand.InverseTransformDirection(arm.PalmContact.forward).normalized;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "IndexTip." + side) arm.IndexPinch = child;
                if (child.name == "ThumbTip." + side) arm.ThumbPinch = child;
                if (child.name.EndsWith("Pad." + side)) arm.Supports.Add(child);
            }
        }
        foreach (Transform finger in arm.Fingers)
        {
            arm.CurlAxes.Add(arm.Anatomical ? Vector3.right
                : finger.InverseTransformDirection(arm.Hand.TransformDirection(Vector3.Cross(arm.PalmForward, arm.PalmNormal))));
            // The old generated hand remains a compatibility fallback. New joints have
            // a true anatomical local-X hinge and support a natural grasp.
            arm.CurlFactors.Add(finger.name.StartsWith("Thumb") ? (arm.Anatomical ? 38f : 5f) : 65f);
        }
        return arm;
    }
    internal void Apply(in TownActivityPose state)
    {
        TownActivityVisual visual = TownServiceActivityMotion.Visual(_service, in state);
        Apply(in visual);
    }
    internal void Apply(in TownActivityVisual visual)
    {
        if (!Ready) return;
        if (_chest != null)
        {
            _sampledChest = _chest.localRotation; _applied = true;
            // A higher terrain sample raises the resident's feet, not the fixed worktop.
            // A small additional lean preserves palm contact without stretching arms or
            // lengthening the neck. This depends only on the replicated grounding offset.
            float terrainLean = _actor != null ? Mathf.Clamp((_actor.localPosition.y - _actorRestHeight) * 180f, 0f, 6f) : 0f;
            _chest.rotation = Quaternion.AngleAxis(6f + 2f * visual.Writing + terrainLean, -_root.right) * _chest.rotation;
        }
        if (_neck != null)
        {
            _sampledNeck = _neck.localRotation; _applied = true;
            _neck.rotation = Quaternion.AngleAxis(4f * (1f - visual.Attention), -_root.right) * _neck.rotation;
        }
        Solve(_left!, visual.Left, 1f, visual.Curl, visual.Attention, 0f);
        Solve(_right!, visual.Right, -1f, visual.Curl, visual.Attention, visual.Writing);
    }
    private void Solve(Arm arm, Vector3 localTarget, float side, float curl, float attention, float writing)
    {
        arm.SampledUpper = arm.Upper.localRotation; arm.SampledFore = arm.Fore.localRotation;
        arm.SampledHand = arm.Hand.localRotation; arm.Applied = true;
        Vector3 shoulder = arm.Upper.position, elbow = arm.Fore.position, wrist = arm.Hand.position;
        float upper = Vector3.Distance(shoulder, elbow), lower = Vector3.Distance(elbow, wrist);
        if (upper < .001f || lower < .001f) return;
        Quaternion table = Quaternion.LookRotation(-_root.forward, -_root.up);
        Quaternion orientation = _service == 2
            ? Quaternion.Slerp(Quaternion.LookRotation(_root.up, -side * _root.right), table, attention) : table;
        orientation = Quaternion.AngleAxis(45f * writing, _root.forward) * orientation;
        Quaternion handRotation = orientation * Quaternion.Inverse(Quaternion.LookRotation(arm.PalmForward, arm.PalmNormal));
        // Evaluate relaxed finger surfaces before IK. Their offsets from the wrist do
        // not depend on arm reach; resetting these rotations also prevents accumulation.
        arm.Hand.rotation = handRotation;
        for (int n = 0; n < arm.Fingers.Count; n++)
            arm.Fingers[n].localRotation = arm.FingerRest[n] * Quaternion.AngleAxis(curl * arm.CurlFactors[n], arm.CurlAxes[n]);
        Vector3 target = _root.TransformPoint(localTarget);
        if (arm.Anatomical && attention > 0f)
        {
            // Visual.Left/Right interpolate to the physical palm contact, not the wrist.
            // Subtract the rotated marker offset through the same smooth interruption.
            Vector3 palmOffset = handRotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale);
            target -= palmOffset * attention;
            float lowest = Vector3.Dot(palmOffset, _root.up);
            foreach (Transform support in arm.Supports)
                if (support != null) lowest = Mathf.Min(lowest, Vector3.Dot(support.position - arm.Hand.position, _root.up));
            // A relaxed hand has an arched palm. Place its actual lowest palmar pad on
            // the wood, rather than driving the whole central palm into the surface.
            target += _root.up * (Vector3.Dot(palmOffset, _root.up) - lowest) * attention;
        }
        Vector3 delta = target - shoulder;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
        Vector3 direction = delta.normalized;
        Vector3 pole = _root.TransformPoint(new Vector3(side * .34f, .98f, .56f)) - shoulder;
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
        if (bend.sqrMagnitude < .5f) bend = _root.right * side;
        float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
        Vector3 wantedElbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        arm.Upper.rotation = Quaternion.FromToRotation(elbow - shoulder, wantedElbow - shoulder) * arm.Upper.rotation;
        arm.Fore.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Fore.position,
            shoulder + direction * distance - arm.Fore.position) * arm.Fore.rotation;
        arm.Hand.localRotation = arm.RestHand;
        arm.Hand.rotation = handRotation;
        if (arm.Anatomical && writing > 0f && arm.ThumbBase != null && arm.ThumbPinch != null && arm.IndexPinch != null)
        {
            // Oppose the real thumb toward the index pad in its own anatomical plane.
            // A signed geometric angle works for both mirrored hands without a guessed
            // left/right constant; the bounded base rotation cannot invert the thumb.
            Vector3 tip = arm.ThumbBase.InverseTransformPoint(arm.ThumbPinch.position);
            Vector3 index = arm.ThumbBase.InverseTransformPoint(arm.IndexPinch.position);
            tip.z = 0f; index.z = 0f;
            float opposition = Mathf.Clamp(Vector3.SignedAngle(tip, index, Vector3.forward), -25f, 25f) * writing;
            arm.ThumbBase.localRotation *= Quaternion.AngleAxis(opposition, Vector3.forward);
        }
        Vector3 pinch = arm.IndexPinch != null && arm.ThumbPinch != null
            ? (arm.IndexPinch.position + arm.ThumbPinch.position) * .5f : arm.Hand.position;
        Vector3 toolShaft = (arm.Hand.position - pinch + _root.up * .045f).normalized;
        arm.Grip.SetPositionAndRotation(pinch, Quaternion.FromToRotation(Vector3.up, toolShaft));
    }
}
