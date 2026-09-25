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
        internal readonly List<Transform> ClosingBases = new(), ClosingTips = new();
        internal readonly List<Vector3> CurlAxes = new();
        internal readonly List<float> CurlFactors = new();
        internal readonly List<Transform> Twist = new();
        internal readonly List<Quaternion> TwistRest = new();
    }
    private readonly Transform _root;
    private readonly byte _service;
    private readonly Arm? _left, _right;
    private readonly Transform? _neck, _chest, _actor;
    private readonly float _actorRestHeight;
    private readonly Transform _workFocus;
    private readonly Transform?[] _body = new Transform?[12];
    private readonly Quaternion[] _bodyLocal = new Quaternion[12], _bodyWorld = new Quaternion[12];
    private Vector3 _hipsPosition;
    private readonly Vector3[] _bodyPositions = new Vector3[12];
    private bool _bodyApplied;
    private readonly Vector3[] _kneePoles = new Vector3[2];
    internal Transform? OfferingPalm { get; }
    private Quaternion _sampledNeck, _sampledChest;
    private bool _applied;
    internal bool Ready => _left != null && _right != null;
    internal TownServiceActivityRig(Transform root, byte service)
    {
        _root = root; _service = service;
        _workFocus = new GameObject("ActivityWorkFocus").transform;
        _workFocus.SetParent(root, false);
        _workFocus.localPosition = TownServiceActivityMotion.RestFocus(service);
        _actor = root.Find("Actor"); _actorRestHeight = _actor != null ? _actor.localPosition.y : 0f; _left = Find(root, "L"); _right = Find(root, "R");
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Neck") _neck = child; else if (child.name == "Chest") _chest = child;
            for (int i=0;i<_body.Length;i++) if (child.name==TownServiceMotionClips.BodyBones[i]) _body[i]=child;
        }
        // The generated reference and the imported actor have different leg axes.
        // A nearly straight animated knee cannot define a stable IK bend plane.
        // Keep the anatomical forward plane in station space; weight shifting is
        // supplied by the hips, never by a pole flipping around a planted ankle.
        for (int leg = 0; leg < 2; leg++)
        {
            int i = leg == 0 ? 6 : 9;
            if (_body[i] != null && _body[i + 1] != null && _body[i + 2] != null)
            {
                Vector3 direction = (_body[i + 2]!.position - _body[i]!.position).normalized;
                Vector3 bend = Vector3.ProjectOnPlane(-root.forward, direction).normalized;
                _kneePoles[leg] = root.InverseTransformDirection(bend);
            }
        }
        if ((service == 1 || service == 3) && _right != null)
        {
            OfferingPalm = new GameObject("ActivityOfferingPalm").transform;
            OfferingPalm.SetParent(root, false);
        }
    }
    internal void BeforeBodySample()
    {
        if (_applied && _chest != null) _chest.localRotation = _sampledChest;
        if (_applied && _neck != null) _neck.localRotation = _sampledNeck;
        Restore(_left); Restore(_right); _applied = false;
        if (_bodyApplied)
        {
            for (int i=0;i<_body.Length;i++) if (_body[i]!=null) _body[i]!.localRotation=_bodyLocal[i];
            if (_body[0]!=null) _body[0]!.localPosition=_hipsPosition;
            _bodyApplied=false;
        }
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
        for (int n = 0; n < arm.Twist.Count; n++) arm.Twist[n].localRotation = arm.TwistRest[n];
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
        for (int n = 1; n <= 3; n++)
            foreach (Transform child in arm.Fore.GetComponentsInChildren<Transform>(true))
                if (child.name == "ForearmTwist" + n + "." + side)
                { arm.Twist.Add(child); arm.TwistRest.Add(child.localRotation); }
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
            if (finger.name.StartsWith("Index1.") || finger.name.StartsWith("Ring1.") || finger.name.StartsWith("Little1."))
            {
                string nextName = finger.name.Replace("1.", "2.");
                foreach (Transform next in arm.Fingers)
                    if (next.name == nextName) { arm.ClosingBases.Add(finger); arm.ClosingTips.Add(next); break; }
            }
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
        ApplyBody(in visual.Body);
        if (_chest != null)
        {
            _sampledChest = _chest.localRotation; _applied = true;
            // A higher terrain sample raises the resident's feet, not the fixed worktop.
            // A small additional lean preserves palm contact without stretching arms or
            // lengthening the neck. This depends only on the replicated grounding offset.
            float terrainLean = _actor != null ? Mathf.Clamp((_actor.localPosition.y - _actorRestHeight) * 180f, 0f, 6f) : 0f;
            _chest.rotation = _root.rotation * Quaternion.Euler(visual.Chest + new Vector3(-6f - terrainLean, 0f, 0f))
                * Quaternion.Inverse(_root.rotation) * _chest.rotation;
        }
        if (_neck != null)
        {
            _sampledNeck = _neck.localRotation; _applied = true;
            _neck.rotation = Quaternion.AngleAxis(4f * (1f - visual.Attention) * (1f - visual.Body.Weight), -_root.right) * _neck.rotation;
        }
        Solve(_left!, visual.Left, 1f, visual.LeftCurl, visual.Attention, visual.LeftRoll, _service == 1, visual.LeftElbow);
        Solve(_right!, visual.Right, -1f, visual.RightCurl, visual.Attention, visual.RightRoll, false, visual.RightElbow);
        if (OfferingPalm != null)
        {
            Arm right = _right!;
            Vector3 normal = right.Hand.TransformDirection(right.PalmNormal).normalized;
            Vector3 forward = right.Hand.TransformDirection(right.PalmForward).normalized;
            OfferingPalm.SetPositionAndRotation(right.PalmContact != null ? right.PalmContact.position : right.Hand.position,
                Quaternion.LookRotation(forward, normal));
        }
        _workFocus.position = _service == 1 ? _left!.Grip.position : _root.TransformPoint(TownServiceActivityMotion.RestFocus(_service));
        if (_service == 3 && OfferingPalm != null)
            _workFocus.position = Vector3.Lerp(_workFocus.position,
                OfferingPalm.position + OfferingPalm.up * (.07f + .10f * visual.Cast), Mathf.Clamp01(visual.Cast * 3f));
    }
    private void ApplyBody(in TownMotionBody body)
    {
        // Capture every world baseline before changing any parent. Applying child
        // deltas to an already rotated parent would double the generated torso turn.
        for (int i=0;i<_body.Length;i++)
            if (_body[i]!=null) { _bodyLocal[i]=_body[i]!.localRotation; _bodyWorld[i]=_body[i]!.rotation; _bodyPositions[i]=_body[i]!.position; }
        if (_body[0]!=null) _hipsPosition=_body[0]!.localPosition;
        _bodyApplied=true;
        // Keep the same stance solver at zero work weight. Returning early here
        // snapped both knees to the base clip on the last greeting frame.
        if (_body[0]!=null) _body[0]!.position += _root.TransformVector(body.Offset)*body.Weight;
        Quaternion inverse=Quaternion.Inverse(_root.rotation);
        for (int i=0;i<_body.Length;i++)
            if (_body[i]!=null) _body[i]!.rotation=_root.rotation*Quaternion.Slerp(Quaternion.identity,body.Get(i),body.Weight)*inverse*_bodyWorld[i];
        // The target skin has longer, straighter legs than the generation skeleton.
        // Lower the pelvis only as far as necessary to keep both original soles
        // reachable; clamping each leg independently would visibly lift one foot.
        if (_body[0]!=null) _body[0]!.position-=_root.up*Mathf.Max(RequiredPelvisDrop(6),RequiredPelvisDrop(9));
        PlantFoot(6); PlantFoot(9);
    }
    private float RequiredPelvisDrop(int upperIndex)
    {
        Transform? upper=_body[upperIndex],lower=_body[upperIndex+1],foot=_body[upperIndex+2];
        if(upper==null||lower==null||foot==null)return 0f;
        Vector3 delta=upper.position-_bodyPositions[upperIndex+2];
        float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,foot.position)-.012f*Mathf.Abs(_root.lossyScale.x);
        float horizontal=Vector3.ProjectOnPlane(delta,_root.up).sqrMagnitude;
        float available=Mathf.Sqrt(Mathf.Max(0f,length*length-horizontal));
        return Mathf.Max(0f,Vector3.Dot(delta,_root.up)-available);
    }
    private void PlantFoot(int upperIndex)
    {
        Transform? upper=_body[upperIndex],lower=_body[upperIndex+1],foot=_body[upperIndex+2];
        if (upper==null||lower==null||foot==null) return;
        Vector3 hip=upper.position,knee=lower.position,ankle=foot.position;
        Vector3 delta=_bodyPositions[upperIndex+2]-hip;
        float a=Vector3.Distance(hip,knee),b=Vector3.Distance(knee,ankle);
        if(a<.001f||b<.001f||delta.sqrMagnitude<1e-8f)return;
        float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.0001f);
        Vector3 direction=delta.normalized,bend=Vector3.ProjectOnPlane(_root.TransformDirection(_kneePoles[upperIndex == 6 ? 0 : 1]),direction).normalized;
        if(bend.sqrMagnitude<.5f)bend=-_root.forward;
        float along=(a*a-b*b+distance*distance)/(2f*distance);
        Vector3 targetKnee=hip+direction*along+bend*Mathf.Sqrt(Mathf.Max(0f,a*a-along*along));
        Quaternion sole=_bodyWorld[upperIndex+2];
        upper.rotation=Quaternion.FromToRotation(knee-hip,targetKnee-hip)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,hip+direction*distance-lower.position)*lower.rotation;
        foot.rotation=sole;
    }
    private void Solve(Arm arm, Vector3 localTarget, float side, float curl, float attention, float roll, bool pinchTarget,
        Vector3 authoredElbow)
    {
        arm.SampledUpper = arm.Upper.localRotation; arm.SampledFore = arm.Fore.localRotation;
        arm.SampledHand = arm.Hand.localRotation; arm.Applied = true;
        Vector3 shoulder = arm.Upper.position, elbow = arm.Fore.position, wrist = arm.Hand.position;
        float upper = Vector3.Distance(shoulder, elbow), lower = Vector3.Distance(elbow, wrist);
        if (upper < .001f || lower < .001f) return;
        Quaternion table = Quaternion.LookRotation(-_root.forward, -_root.up);
        Quaternion orientation = _service == 2
            ? Quaternion.LookRotation(_root.up, -side * _root.right) : table;
        orientation = Quaternion.AngleAxis(roll, _root.forward) * orientation;
        Quaternion handRotation = orientation * Quaternion.Inverse(Quaternion.LookRotation(arm.PalmForward, arm.PalmNormal));
        // Evaluate relaxed finger surfaces before IK. Their offsets from the wrist do
        // not depend on arm reach; resetting these rotations also prevents accumulation.
        arm.Hand.rotation = handRotation;
        for (int n = 0; n < arm.Fingers.Count; n++)
        {
            Transform finger = arm.Fingers[n];
            float amount = pinchTarget && !finger.name.StartsWith("Index") && !finger.name.StartsWith("Thumb") ? curl * .3f : curl;
            finger.localRotation = arm.FingerRest[n] * Quaternion.AngleAxis(amount * arm.CurlFactors[n], arm.CurlAxes[n]);
        }
        if (_service == 2)
        {
            // A prayer joins relaxed fingers; the neutral imported open-hand splay
            // otherwise makes the two hands look interlaced or spread apart.
            for (int n = 0; n < arm.ClosingBases.Count; n++)
            {
                Transform finger = arm.ClosingBases[n];
                Quaternion together = Quaternion.FromToRotation(arm.ClosingTips[n].position - finger.position, handRotation * arm.PalmForward);
                finger.rotation = together * finger.rotation;
            }
        }
        if (_service == 2 && arm.ThumbBase != null && arm.ThumbPinch != null)
        {
            // The imported relaxed thumb points out of the palm plane. Keeping that
            // spread while joining two palms drives the thumbs through each other.
            // Adduct both toward the common finger direction before solving contact.
            Vector3 thumb = arm.ThumbPinch.position - arm.ThumbBase.position;
            Vector3 upright = handRotation * arm.PalmForward;
            Vector3 axis = Vector3.Cross(thumb, upright);
            if (axis.sqrMagnitude > 1e-10f)
                arm.ThumbBase.rotation = Quaternion.AngleAxis(Mathf.Min(40f, Vector3.Angle(thumb, upright)), axis.normalized) * arm.ThumbBase.rotation;
        }
        if (arm.Anatomical && pinchTarget && arm.ThumbBase != null && arm.ThumbPinch != null && arm.IndexPinch != null)
        {
            Vector3 tip = arm.ThumbPinch.position - arm.ThumbBase.position;
            Vector3 index = arm.IndexPinch.position - arm.ThumbBase.position;
            float a = tip.magnitude, b = index.magnitude;
            if (a > .001f && b > .001f)
            {
                // The original flat-plane thumb swing left a measured 35 mm gap
                // around a 26 mm coin. Solve the opposition triangle from the real
                // pads, retaining a small contact allowance for their skin thickness.
                float gap = .024f * Mathf.Abs(_root.lossyScale.x);
                float angle = Mathf.Acos(Mathf.Clamp((a * a + b * b - gap * gap) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
                float close = Mathf.Clamp(Vector3.Angle(tip, index) - angle, 0f, 35f) * Mathf.Clamp01(curl / .55f);
                Vector3 axis = Vector3.Cross(tip, index);
                if (axis.sqrMagnitude > 1e-10f) arm.ThumbBase.rotation = Quaternion.AngleAxis(close, axis.normalized) * arm.ThumbBase.rotation;
            }
        }
        // Retarget the entire forearm/palm frame, not just an unconstrained wrist
        // position. The former world-axis half-turn disagreed with the angled
        // forearm and wrung the cuff even though every contact marker was correct.
        Vector3 wantedNormal = handRotation * arm.PalmNormal;
        Vector3 foreDirection = Vector3.forward;
        for (int pass = 0; pass < 20; pass++)
        {
            arm.Upper.localRotation = arm.SampledUpper;
            arm.Fore.localRotation = arm.SampledFore;
            arm.Hand.rotation = handRotation;
            Vector3 target = _root.TransformPoint(localTarget);
            if (pinchTarget && arm.IndexPinch != null && arm.ThumbPinch != null)
                target -= (arm.IndexPinch.position + arm.ThumbPinch.position) * .5f - arm.Hand.position;
            else if (arm.Anatomical)
            {
                // Targets now describe actual palm surfaces throughout the entire animation,
                // including the offered hand. A wrist-space target left spells hovering over
                // the knuckles and made authored object contact impossible to maintain.
                Vector3 palmOffset = handRotation * Vector3.Scale(arm.PalmOffset, arm.Hand.lossyScale);
                target -= palmOffset;
                float lowest = Vector3.Dot(palmOffset, _root.up);
                foreach (Transform support in arm.Supports)
                    if (support != null) lowest = Mathf.Min(lowest, Vector3.Dot(support.position - arm.Hand.position, _root.up));
                // A relaxed hand has an arched palm. Place its actual lowest palmar pad on
                // the wood, rather than driving the whole central palm into the surface.
                float tableContact = (1f - Mathf.SmoothStep(0f, 1f, (localTarget.y - .960f) / .025f))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(roll) / 25f));
                target += _root.up * (Vector3.Dot(palmOffset, _root.up) - lowest) * tableContact;
            }
            Vector3 delta = target - shoulder;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
            Vector3 direction = delta.normalized;
            // Generated elbow motion preserves changing shoulder/elbow coordination.
            // A fixed pole was one reason the former hands moved like mechanical arms.
            Vector3 guide = authoredElbow.sqrMagnitude > .01f ? authoredElbow : new Vector3(side * .34f, .98f, .46f);
            Vector3 greeting = _service == 1
                ? new Vector3(side * .49f, 1.08f, .16f)
                : _service == 2 ? new Vector3(side * .33f, 1.08f, .16f)
                : new Vector3(side * .35f, 1.04f, .45f);
            guide = Vector3.Lerp(guide, greeting, attention);
            // The generated human reference is narrower than the merchant's actual
            // coat/belly. Keep the elbow's approach outside that measured silhouette.
            float clearance = _service == 1 ? Mathf.Lerp(.85f, .36f, attention) : .40f;
            // Preserve the recorded lateral and forward elbow excursion outside the
            // silhouette. Hard-clamping every source x/z to the same minimum used
            // to erase that coordination and made the hands pivot on fixed poles.
            guide.x = side * (clearance + Mathf.Max(0f, side * guide.x - .14f) * .25f);
            guide.y = _service == 3 ? .84f : _service == 1
                ? Mathf.Lerp(Mathf.Max(1.10f, guide.y), 1f, attention)
                : Mathf.Max(1.10f, guide.y);
            guide.z = _service == 1 ? Mathf.Lerp(.28f + Mathf.Clamp(guide.z - .50f, 0f, .20f) * .4f,
                .16f, attention) : .28f + Mathf.Clamp(guide.z - .50f, 0f, .20f) * .4f;
            // Keep the lowered joined hands clear of the actual robe as well as
            // the original prayer. A rearward pole cut three sleeve/torso triangles.
            if (_service == 2)
                guide = Vector3.Lerp(new Vector3(side * .42f, .70f, .10f),
                    new Vector3(side * .32f, 1f, .18f), attention);
            Vector3 pole = _root.TransformPoint(guide) - shoulder;
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (bend.sqrMagnitude < .5f) bend = _root.right * side;
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            Vector3 wantedElbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            arm.Upper.rotation = Quaternion.FromToRotation(elbow - shoulder, wantedElbow - shoulder) * arm.Upper.rotation;
            arm.Fore.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Fore.position,
                shoulder + direction * distance - arm.Fore.position) * arm.Fore.rotation;
            foreDirection = (arm.Hand.position - arm.Fore.position).normalized;
            // The lateral axis remains well conditioned for raised and lowered arms.
            // Projecting world-down becomes singular as a casting forearm passes upright.
            Vector3 lateral = Vector3.ProjectOnPlane(_root.right, foreDirection);
            if (lateral.sqrMagnitude < .001f)
                lateral = Vector3.Cross(arm.Fore.rotation * arm.RestHand * arm.PalmNormal, foreDirection);
            Vector3 freeNormal = Vector3.Cross(foreDirection, lateral.normalized).normalized;
            // The spell-supporting palm keeps the same level reference while raised.
            // Fading that reference with hand height left it vertical even at an
            // authored half-turn, so a correct roll value did not mean palm-up.
            float contactFrame = _service == 1 || _service == 3 ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (localTarget.y - .970f) / .22f);
            Quaternion palmFrame;
            if (_service == 2)
            {
                Vector3 fingerDirection = Vector3.ProjectOnPlane(_root.up - _root.forward * .8f, wantedNormal).normalized;
                fingerDirection = Vector3.RotateTowards(foreDirection, fingerDirection, 55f * Mathf.Deg2Rad, 0f).normalized;
                palmFrame = Quaternion.LookRotation(fingerDirection,
                    Vector3.ProjectOnPlane(wantedNormal, fingerDirection).normalized);
            }
            else
            {
                palmFrame = Quaternion.LookRotation(foreDirection, freeNormal);
                // Pronation follows a signed angle about the forearm, including a full
                // offered half turn. Slerping between opposing palm normals introduces
                // an ambiguous 180-degree branch. Only flexion is corrected here.
                Vector3 flatFingers = _service == 1 && side > 0f ? -_root.forward : Vector3.ProjectOnPlane(foreDirection, _root.up);
                if (flatFingers.sqrMagnitude > .001f)
                {
                    Vector3 bent = Vector3.RotateTowards(foreDirection, flatFingers.normalized, 55f * Mathf.Deg2Rad, 0f).normalized;
                    Quaternion flexion = Quaternion.FromToRotation(foreDirection, bent);
                    palmFrame = Quaternion.Slerp(Quaternion.identity, flexion, Mathf.Max(contactFrame, attention)) * palmFrame;
                }
                Vector3 fingers = palmFrame * Vector3.forward;
                Vector3 levelNormal = Vector3.ProjectOnPlane(-_root.up, fingers);
                if (levelNormal.sqrMagnitude > .001f)
                {
                    float baselineRoll = Vector3.SignedAngle(palmFrame * Vector3.up, levelNormal, fingers);
                    palmFrame = Quaternion.AngleAxis(baselineRoll * Mathf.Max(contactFrame, attention), fingers) * palmFrame;
                }
                palmFrame = Quaternion.AngleAxis(-roll, fingers) * palmFrame;
            }
            Quaternion corrected = palmFrame * Quaternion.Inverse(Quaternion.LookRotation(arm.PalmForward, arm.PalmNormal));
            float correction = (handRotation * arm.PalmForward - corrected * arm.PalmForward).sqrMagnitude
                + (handRotation * arm.PalmNormal - corrected * arm.PalmNormal).sqrMagnitude;
            handRotation = corrected;
            if (correction < 1e-9f) break;
        }
        Vector3 relaxedNormal = arm.Fore.rotation * arm.RestHand * arm.PalmNormal;
        float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(relaxedNormal, foreDirection),
            Vector3.ProjectOnPlane(handRotation * arm.PalmNormal, foreDirection), foreDirection);
        // Keep the authored turn count. A wrapped +/-180-degree angle is equivalent
        // at the hand, but its one-third support rotation would jump by 120 degrees.
        if (_service != 2) twist = Mathf.DeltaAngle(0f, twist + roll) - roll;
        // Three longitudinal supports distribute pronation through real skin. The
        // proximal elbow is left untwisted; sleeve and hidden skin share the distal
        // support. Existing bundles retain a functional single-bone fallback.
        if (arm.Twist.Count == 3)
            for (int n = 0; n < 3; n++)
            {
                arm.Twist[n].localRotation = arm.TwistRest[n];
                arm.Twist[n].rotation = Quaternion.AngleAxis(twist * (n + 1f) / 3f, foreDirection) * arm.Twist[n].rotation;
            }
        else arm.Fore.rotation = Quaternion.AngleAxis(twist, foreDirection) * arm.Fore.rotation;
        arm.Hand.rotation = handRotation;
        Vector3 pinch = arm.IndexPinch != null && arm.ThumbPinch != null
            ? (arm.IndexPinch.position + arm.ThumbPinch.position) * .5f : arm.Hand.position;
        Vector3 toolShaft = (arm.Hand.position - pinch + _root.up * .045f).normalized;
        arm.Grip.SetPositionAndRotation(pinch, Quaternion.FromToRotation(Vector3.up, toolShaft));
    }
}
