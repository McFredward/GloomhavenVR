using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// Drives per-finger curl (0 = straight .. 1 = fully curled) by rotating the three
/// <see cref="HandRig"/> joints of each finger around their local X axis (the
/// convention baked into both the procedural hand and the bundle prefab contract:
/// +Z along the finger, +Y away from the palm ⇒ positive local-X rotation curls
/// toward the palm). LCVR FingerCurler pattern, no Animator involved.
///
/// Curl targets are set once per frame from controller input by <see cref="VRHand"/>:
///   trigger + trigger CAPACITIVE touch → index (trigger untouched = pointing, even at
///   full grip; see VRHand.UpdateCurlTargets); grip value → middle/ring/pinky;
///   thumb capacitive touch (primaryTouch/secondaryTouch/primary2DAxisTouch) → thumb
///   (forced fully closed while the hand makes a fist, see VRHand.UpdateCurlTargets).
/// Actual joint rotations are smoothed (exponential lerp) to avoid jitter from the
/// binary touch signals and controller value noise.
///
/// WHOSE grip a curler animates is stated by the caller (<see cref="GripLimits"/>): the
/// local dials by default, the hand OWNER's synced ones for a peer's hands.
///
/// FIST DIAGNOSTICS (on-device "mostly no fist" investigation): the full-curl joint
/// angles are read live from those limits (the local <see cref="HandsConfig"/> dials
/// unless the caller supplied the owner's; defaults 75/95/65; thumb
/// 25/45/60 scaled proportionally), the actually-applied per-joint angles are recorded
/// for <see cref="GetAppliedAngles"/>, and <see cref="Tick"/> measures whether any
/// driven joint was rotated AWAY from what we applied last frame (an Animator or other
/// writer running after our Update would show up here) — read via
/// <see cref="ConsumeExternalDrift"/>.
/// </summary>
internal sealed class FingerCurler
{
    /// <summary>
    /// Default full-curl joint angles (degrees, local X) per joint index (root/mid/tip).
    /// Raised 65/80/50 → 75/95/65 (2026-07 hardware evidence: even at curl 1.0 the
    /// 65/80/50 fist read visibly open on the bulky styled hands — a real fist flexes
    /// roughly 90/100/70 at MCP/PIP/DIP). Render-verified per style via the
    /// unity/hand-prep/rig_hand.py fist matrix at the new angles.
    /// </summary>
    internal static readonly Vector3 DefaultFingerMaxAngles = new(75f, 95f, 65f);
    internal static readonly Vector3 DefaultThumbMaxAngles = new(25f, 45f, 60f);

    /// <summary>
    /// GLOVE-PINKY splay fix (2026-07 hardware round): the glove pinky MESH tube leans
    /// ~18° outward in XZ (PCA (+0.30,+0.25,+0.92)) while its bone chain is straight
    /// (0,0,1), so a full local-X curl left the curled pinky mesh visibly splayed
    /// outward from the fist. The runtime compensation is a small counter-abduction about
    /// the root joint's local Z (the palm normal at rest; Unity's ZXY euler order applies
    /// Z BEFORE the X flexion) coupled to the curl value. Positive local-Z adducts the
    /// LEFT pinky toward the ring finger (local +X→+Y under +Z; the bone tilts toward
    /// world -X); the RIGHT hand mirrors, so the sign flips per side. Glove style only —
    /// the styled hands' refit chains follow their mesh tubes and need no compensation.
    ///
    /// <para>SUPERSEDED BY THE ASSET (2026-07; value kept, default deliberately unchanged).
    /// A Blender bone-ROLL cannot fix this — roll only spins the flexion axis within the
    /// plane perpendicular to the bone — but RE-FRAMING the bone node can (local +X := the
    /// digit's hinge axis, local Y := the digit direction, heads and mesh untouched), and
    /// unity/hand-prep/aim_curl_axes.py did exactly that to VRHand_{L,R}_rig.fbx: the
    /// glove's hinge-vs-digit error is now 0.00° on all five digits (was pinky +15.2°,
    /// index −10.5°, thumb −9.0°), so the splay this term compensates no longer exists.
    /// Render-measured on the fixed rig, a full fist at 0° already tucks the pinky beside
    /// the ring finger (tip spacing 11.9 mm, ring↔middle 22.4 mm); the historical 14°
    /// pulls it a further 3.4 mm and over-adducts. RECOMMENDED VALUE FOR THE FIXED RIG: 0.
    /// The default is left at 14 here because it is a persisted user setting — change it
    /// deliberately, not as a side effect.</para>
    /// </summary>
    internal const float DefaultGlovePinkyCounterAbductionDeg = Defaults.GlovePinkyCounterAbduction;

    /// <summary>
    /// WHOSE grip this curler is animating — and therefore WHO gets to say how many DEGREES a full
    /// curl is. Shaped after <see cref="Cards.CardDustFx.Permission"/>, which is this project's
    /// post-mortem for exactly this defect class, and added for exactly the same reason.
    ///
    /// <para>THE DEFECT, stated so it cannot come back: a finger rides the wire as a curl 0..1 and
    /// nothing else, so <c>Net.Remote.RemoteAvatar</c> fed a peer's OWN curl into a curler that then
    /// asked the LOCAL <see cref="HandsConfig"/> how far "fully curled" is. Every one of the four
    /// answers was the viewer's: [Hands] CurlProximal/CurlMiddle/CurlTip (75/95/65 degrees shipped)
    /// and GlovePinkyCounterAbduction (14 degrees shipped), all live-tunable from the in-VR options.
    /// Set CurlTip to 130 in your own options and every teammate's fingertips folded to 130 on YOUR
    /// screen while their own screens kept 65 — your grip imposed on everyone you looked at. The
    /// owner's four now ride record 28 (ids 201..204) and arrive here through
    /// <see cref="FromOwner"/>.</para>
    ///
    /// <para>Deliberately NOT a "remote" boolean. One <see cref="FingerCurler"/> serves the local
    /// hands (<c>Hands.VRHand</c>), this player's own mirror (<c>WorldUI.AvatarMirror</c> — still
    /// the LOCAL answer, it is this player) and every peer's hands, and a bool would have to be read
    /// as "am I remote?" at each of them. This states which QUESTION is still open instead, and
    /// <c>default</c> is <see cref="Whose.AskMyOwnDials"/>, so a future call site that forgets can
    /// only fall back to today's local behaviour — never curl one player's fingers by another
    /// player's dials.</para>
    /// </summary>
    internal readonly struct GripLimits
    {
        /// <summary>Who has already answered "how many degrees is a full curl?".</summary>
        internal enum Whose
        {
            /// <summary>
            /// "How far do MY OWN fingers curl?" — the local [Hands] dials are the whole answer and
            /// this struct asks them. Every local hand path means this.
            ///
            /// <para>It is the DEFAULT deliberately: an omitted answer resolves to the one that is
            /// always safe to give, because the local dials are by definition the right dials for
            /// the local player, and a peer's hands are built at exactly two call sites.</para>
            /// </summary>
            AskMyOwnDials = 0,

            /// <summary>
            /// "How far does the OWNER of this hand curl their fingers?" — already answered by the
            /// caller off that owner's tuning record (wire ids 201..204) and clamped to the owner's
            /// own 0..130 / -30..30 windows by <c>Net.RemoteBoardTuning</c> before it got here. The
            /// viewer's own dials are not consulted and MUST not be: they answer a question about
            /// the viewer's OWN hands. The only caller is the remote avatar.
            /// </summary>
            OwnerAlreadyAnswered,
        }

        private readonly Whose _whose;
        private readonly Vector3 _fingerMaxAngles;
        private readonly float _pinkySplayDeg;

        private GripLimits(Whose whose, Vector3 fingerMaxAngles, float pinkySplayDeg)
        {
            _whose = whose;
            _fingerMaxAngles = fingerMaxAngles;
            _pinkySplayDeg = pinkySplayDeg;
        }

        /// <summary>
        /// The hand OWNER's own full-curl angles (degrees, root/mid/tip — pass
        /// <c>RemoteBoardTuning.HandCurlAngles</c>, which is this exact triple as one value) and
        /// their glove-pinky counter-abduction. Re-clamped to the same windows the local reads use
        /// (0..130 and -30..30) so the guarantee "no angle a player could not have curled to" is
        /// this struct's own rather than borrowed — a no-op for today's only caller, whose four
        /// values <c>RemoteBoardTuning</c> already clamped on receipt.
        /// </summary>
        internal static GripLimits FromOwner(Vector3 fingerMaxAngles, float pinkySplayDeg) =>
            new(Whose.OwnerAlreadyAnswered,
                new Vector3(
                    Mathf.Clamp(fingerMaxAngles.x, 0f, 130f),
                    Mathf.Clamp(fingerMaxAngles.y, 0f, 130f),
                    Mathf.Clamp(fingerMaxAngles.z, 0f, 130f)),
                Mathf.Clamp(pinkySplayDeg, -30f, 30f));

        /// <summary>Full-curl joint angles (degrees: root/mid/tip); 75/95/65 at the shipped
        /// defaults, whichever player they came from.</summary>
        internal Vector3 FingerMaxAngles => _whose == Whose.OwnerAlreadyAnswered
            ? _fingerMaxAngles
            : HandsConfig.FingerMaxAnglesSafe(DefaultFingerMaxAngles);

        /// <summary>Glove-pinky counter-abduction at full curl (degrees); 14 at the shipped
        /// defaults. Only the glove rig applies it — see <c>_pinkySplaySign</c>.</summary>
        internal float PinkySplayDegrees => _whose == Whose.OwnerAlreadyAnswered
            ? _pinkySplayDeg
            : HandsConfig.GlovePinkyCounterAbductionSafe(DefaultGlovePinkyCounterAbductionDeg);
    }

    /// <summary>
    /// Per-STYLE curl-range clamp, indexed by (int)<see cref="HandStyle"/> (Glove/
    /// Plate/Arcane). History: the first styled builds over-closed ("donut" tips)
    /// because the skin weights dragged palm membranes, so 0.72/0.85 clamps were
    /// added — but combined with the round-2 weight caps that froze the MCP
    /// knuckles, the result was "almost only the fingertips move". With the round-3
    /// rig (adaptive digit caps in unity/hand-prep/rig_hand.py: knuckle zone
    /// t&gt;=-0.10 owned by its finger, caps scaled to measured shell thickness) the
    /// posed-mesh closure metric puts Plate/Arcane fingertips 65-76 mm from the palm
    /// anchor at FULL range — the same closure band as the accepted glove
    /// (59-72 mm) — with no tip-through-palm in the 9-pose render matrix. So all
    /// styles now run the full default-angle range. Kept as a tuning point for
    /// future styles whose authored rest pose over- or under-closes.
    ///
    /// <para>RE-MEASURED AT ModBuild 243, because the plate gauntlet's mesh, rig and weights
    /// were all replaced by the artist's and none of the numbers above were measured on it.
    /// unity/hand-prep/fist_metrics.py at curl 1.0, tip centroid to Anchor_Palm, in mm:
    /// glove 47-58, arcane 41-70, plate NOW 40-64 (was 43-63 on the AI shell). The new plate
    /// is 2.4 mm tighter at the middle finger and 1.0 mm looser at the pinky — the same band,
    /// not a new one, so the 1.0 entry stands unchanged. Its fingertips also end the fist
    /// 75.8 mm apart where the AI shell left them 91.3 mm apart, i.e. the replacement closes
    /// into a TIGHTER fist, toward the glove's 48.5 mm, which is the direction a fist should
    /// move. NOTE the historical figures in the paragraph above (65-76 / 59-72) belong to the
    /// round-3 GENERATED rigs and no longer describe anything shipped; they are kept as the
    /// record of what the 0.72/0.85 clamps were reacting to, not as current measurements.</para>
    ///
    /// <para>KEEP — this is NOT dead code, despite being all-1.0 (refactor Batch D, verified at
    /// HEAD): the constructor below indexes it every time a hand is built, so it is a live
    /// lookup whose current values happen to be identity. "All entries are 1" is the RESULT the
    /// round-3 rig achieved, and the history above is the record of what the earlier
    /// 0.72/0.85 clamps cost.</para>
    /// </summary>
    private static readonly float[] StyleCurlScale = { 1f, 1f, 1f };

    /// <summary>Smoothing rate (1/s). ~60 ms to close most of the gap.</summary>
    private const float LerpSpeed = 18f;

    private readonly HandRig _rig;
    private readonly float _curlScale;
    private readonly Quaternion[][] _baseRotations = new Quaternion[5][];
    private readonly float[] _current = new float[5];
    private readonly float[] _target = new float[5];

    // Diagnostics: what THIS curler actually wrote last Tick, per finger/joint.
    private readonly Vector3[] _appliedAngles = new Vector3[5];
    private readonly Quaternion[][] _lastWritten = new Quaternion[5][];
    private bool _hasWritten;
    private float _maxExternalDriftDeg;

    /// <summary>±1 on the glove (sign per hand side), 0 for styled hands — see
    /// <see cref="DefaultGlovePinkyCounterAbductionDeg"/>.</summary>
    private readonly float _pinkySplaySign;

    internal FingerCurler(HandRig rig, HandSide side)
    {
        _rig = rig;
        _curlScale = StyleCurlScale[(int)HandStyles.Clamp((int)rig.VisualStyle)];
        _pinkySplaySign = rig.VisualStyle == HandStyle.Glove
            ? (side == HandSide.Left ? 1f : -1f)
            : 0f;
        for (int f = 0; f < 5; f++)
        {
            FingerJoints joints = rig.GetFinger((Finger)f);
            _baseRotations[f] = new[]
            {
                joints.Root.localRotation,
                joints.Mid.localRotation,
                joints.Tip.localRotation
            };
            _lastWritten[f] = new Quaternion[3];
            // Slight rest curl so an idle hand does not look like a plank.
            _current[f] = _target[f] = 0.1f;
        }
    }

    /// <summary>Current smoothed curl of a finger (0..1) — poke/pose logic reads this.</summary>
    public float GetCurl(Finger finger) => _current[(int)finger];

    /// <summary>Set the target curl of a finger (clamped 0..1).</summary>
    public void SetTarget(Finger finger, float curl) => _target[(int)finger] = Mathf.Clamp01(curl);

    /// <summary>The curl TARGET of a finger — what it is heading for, not
    /// <see cref="GetCurl"/>'s where-it-is-now.
    ///
    /// <para>Exists for the in-hand card grasp (<c>Cards.HeldCardGrip</c>), which blends the
    /// controller-driven curls toward a modelled hand pose over a short eased window. That blend
    /// has to read the value it is blending FROM, and reading the smoothed CURRENT value instead
    /// would feed this smoother its own output — a lag that compounds every frame and never
    /// reaches either end.</para></summary>
    public float GetTarget(Finger finger) => _target[(int)finger];

    /// <summary>The per-joint angles (degrees: root/mid/tip) actually written on the last <see cref="Tick"/>.</summary>
    public Vector3 GetAppliedAngles(Finger finger) => _appliedAngles[(int)finger];

    /// <summary>
    /// Largest angle (degrees) any driven joint had been rotated away from the value
    /// this curler wrote, measured at the start of each <see cref="Tick"/> since the
    /// last call. Nonzero ⇒ something ELSE (Animator pass, other script) overwrites
    /// the finger bones after our Update — the smoking gun for "curl applied but the
    /// rendered hand does not close". Resets on read.
    /// </summary>
    public float ConsumeExternalDrift()
    {
        float drift = _maxExternalDriftDeg;
        _maxExternalDriftDeg = 0f;
        return drift;
    }

    /// <summary>
    /// Advance smoothing and write joint rotations. Called once per frame by VRHand.
    /// <paramref name="limits"/> states WHOSE grip dials decide how far a full curl is; omitting it
    /// means the LOCAL player's, which is what every local hand wants — see <see cref="GripLimits"/>.
    /// </summary>
    public void Tick(float deltaTime, GripLimits limits = default)
    {
        // Live-tunable max angles ([Hands] CurlProximal/CurlMiddle/CurlTip, 75/95/65 degrees at the
        // shipped defaults) — this player's own unless the caller supplied the hand OWNER's. The
        // thumb keeps its authored 25/45/60 proportions by scaling with the finger ratios.
        Vector3 fingerMax = limits.FingerMaxAngles * _curlScale;
        Vector3 thumbMax = new(
            DefaultThumbMaxAngles.x * (fingerMax.x / DefaultFingerMaxAngles.x),
            DefaultThumbMaxAngles.y * (fingerMax.y / DefaultFingerMaxAngles.y),
            DefaultThumbMaxAngles.z * (fingerMax.z / DefaultFingerMaxAngles.z));

        float k = 1f - Mathf.Exp(-LerpSpeed * deltaTime);
        for (int f = 0; f < 5; f++)
        {
            _current[f] = Mathf.Lerp(_current[f], _target[f], k);

            FingerJoints joints = _rig.GetFinger((Finger)f);
            Vector3 max = f == (int)Finger.Thumb ? thumbMax : fingerMax;
            Quaternion[] baseRot = _baseRotations[f];
            Quaternion[] written = _lastWritten[f];
            float curl = _current[f];

            // External-overwrite detector: if the bone no longer holds what WE wrote
            // last frame, another writer ran after us (Animator update, other script).
            if (_hasWritten)
            {
                float drift = Quaternion.Angle(joints.Root.localRotation, written[0]);
                drift = Mathf.Max(drift, Quaternion.Angle(joints.Mid.localRotation, written[1]));
                drift = Mathf.Max(drift, Quaternion.Angle(joints.Tip.localRotation, written[2]));
                if (drift > _maxExternalDriftDeg)
                    _maxExternalDriftDeg = drift;
            }

            _appliedAngles[f] = max * curl;
            // Glove-pinky counter-abduction: curl-coupled local-Z on the ROOT joint only. Same
            // ownership rule as the curl angles above — 14 degrees at the shipped defaults, and
            // whose 14 it is comes from the caller, not from this client's config.
            float rootRz = f == (int)Finger.Pinky && _pinkySplaySign != 0f
                ? _pinkySplaySign * limits.PinkySplayDegrees * curl
                : 0f;
            written[0] = joints.Root.localRotation = baseRot[0] * Quaternion.Euler(max.x * curl, 0f, rootRz);
            written[1] = joints.Mid.localRotation = baseRot[1] * Quaternion.Euler(max.y * curl, 0f, 0f);
            written[2] = joints.Tip.localRotation = baseRot[2] * Quaternion.Euler(max.z * curl, 0f, 0f);
        }
        _hasWritten = true;
    }
}
