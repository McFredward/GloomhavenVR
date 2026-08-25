using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE FREE HAND DISTURBS THE CLOTH — let the hand that is NOT holding a figure push that figure's
/// cape around by touching it.
///
/// <para>USER REQUEST (hardware test, ModBuild 286, verbatim): "Manche Elemente an einer Figur
/// reagieren auf meine Handbewegungen wenn ich die figur grabbe zB Umhänge. Das ist ziemlich cool
/// und soll auch so bleiben. Ich will das es noch weiter geht, sie sollen auch auf meine andere Hand
/// reagieren, wenn ich mit der freien VR hand diese elemente berühre."</para>
///
/// <para>WHAT ALREADY WORKS AND IS NOT TOUCHED. The holding hand already drives the cape, because
/// moving the figure moves the cloth's transform and the solver reads that as travel. Nothing here
/// changes that path. This adds the OTHER hand as something the cloth collides with.</para>
///
/// <para>THE MECHANISM. <c>Cloth.sphereColliders</c> takes an array of
/// <see cref="ClothSphereColliderPair"/>, and a pair of two spheres is a CONIC CAPSULE between
/// them — which is the shape of a hand from the palm to the fingertip, so one pair covers the whole
/// hand rather than approximating it with a ball. The two spheres live on a single scene-root probe
/// object this class owns, on Unity's built-in Ignore Raycast layer (2) so they are invisible to
/// <c>Physics.DefaultRaycastLayers</c> and cannot be picked up by the mod's or the game's picking.
/// Their world positions are written from <c>Rig.PalmCenter</c> and <c>Rig.IndexTip</c> every frame
/// the probe is attached.</para>
///
/// <para>COST — MEASURED, not assumed, in the same Unity 2021.3.5f1 Linux player as
/// <see cref="FigureCloth"/>'s harness, 8 reps per cell, with a NULL control (an empty timed body,
/// 0.0000-0.0001 ms) and a known-positive control (<c>AddComponent&lt;Cloth&gt;</c>, which must cook
/// the fabric: 17.09 / 24.67 / 40.24 ms at 1681 / 3721 / 6561 vertices):
/// <list type="bullet">
/// <item><c>sphereColliders = pairs</c>, first assignment .... 0.0165 ms</item>
/// <item><c>sphereColliders = pairs</c>, re-assignment ....... 0.0083-0.0100 ms</item>
/// <item><c>capsuleColliders = one capsule</c> .............. 0.0086-0.0103 ms</item>
/// <item><c>sphereColliders = empty</c> (clearing) .......... 0.0047-0.0099 ms</item>
/// <item>moving an assigned collider's transform ............ 0.0041 ms</item>
/// </list>
/// ASSIGNING THE COLLIDER ARRAYS DOES NOT RE-COOK. The numbers are three orders of magnitude below
/// the 20.1 / 34.3 / 56.4 ms an enable transition costs at those same vertex counts, and — the part
/// that actually settles it — they are FLAT in vertex count (0.0165 ms at both 3721 and 6561), while
/// a cook is linear in it. So the arrays could be written every frame and it would not matter. They
/// are still written once per attach, because there is no reason to write them more often.</para>
///
/// <para>WHEN IT COSTS NOTHING. The probe is attached only while EXACTLY ONE hand holds a figure
/// (two held figures means there is no free hand, and none held means there is nothing to touch),
/// and only while that free hand is tracked and within <see cref="FigureGrabConfig.ClothHandReachRealMeters"/>
/// of the figure. Outside that the captured arrays are written back and this class is a pair of null
/// checks and a return. The gate has hysteresis (<see cref="ReleaseFactor"/>) so a hand hovering on
/// the boundary cannot churn the assignment every frame.</para>
///
/// <para>THE UNIT IS REAL METRES AT THE HAND, scaled by <c>VRHand.WorldScale</c> — the same choice
/// and the same reasoning as <see cref="FigureGrabConfig.PickRadiusRealMeters"/>. His hand is a real
/// hand and the figure is however big he has made it; a world-unit radius would grow and shrink with
/// the rig zoom and stop matching his fingers. The probe object sits at the scene root at unit
/// scale, so a <see cref="SphereCollider.radius"/> written in world units is exactly that radius in
/// the world, with no lossyScale term to get wrong.</para>
///
/// <para><c>Physics.autoSyncTransforms = false</c> DOES NOT BREAK THIS, AND THAT WAS MEASURED
/// RATHER THAN ASSUMED. <c>PhysicsController.Setup</c> (PhysicsController.cs:18-31) turns
/// auto-sync OFF when <c>PlatformLayer.Setting.SimplifyPhysics</c> is on AND the active scene is
/// <c>"Game_gamepad"</c>; <c>SceneController.cs:1069</c> passes exactly that predicate and
/// <c>SceneController.cs:211</c> picks that scene name whenever <c>InputManager.GamePadInUse</c>.
/// His ModBuild 286 log contains <c>Added scene: Game_gamepad</c>, so THE SCENE HALF OF THE
/// PREDICATE IS MET ON HIS RIG. (The <c>SimplifyPhysics</c> half is a serialized platform setting
/// and cannot be read from a log; it is NOT checked and NOT assumed either way.) On that path a
/// collider's transform write does not reach PhysX until the next <c>FixedUpdate</c>, which for a
/// hand moved in <c>Update</c> would mean lagging the visible hand or missing a fast pass
/// entirely.</para>
///
/// <para>It does not. Same player, a sphere pair swept through a settled cloth, moved in
/// <c>Update</c> exactly as this class moves it, <c>fixedDeltaTime = 1/30</c> against a 90 Hz render
/// loop — the shape that makes a missed sync visible rather than theoretical. Worst cloth
/// displacement from rest: NULL CONTROL (the same sweep with NO collider assigned) 0.01923 m;
/// auto-sync ON, Unity's default, 0.08774 m; AUTO-SYNC OFF with nothing else done 0.08585 m; and
/// auto-sync off plus <c>Physics.SyncTransforms()</c> 0.08805 m. If the collider were not reaching
/// the solver the third row would read the null control's 0.019 — it reads 0.086, within 2 % of the
/// positive control against a 0.019 noise floor. Unity's <c>Cloth</c> takes its collider poses from
/// the MANAGED transform, not from the PhysX scene pose, so no <c>Physics.SyncTransforms()</c> is
/// added here and none is needed. It is recorded anyway, because a future scene rename or a
/// <c>SimplifyPhysics</c> flip would otherwise arm the question again silently — and because THIS
/// RESULT DOES NOT TRANSFER to particle-system collision, which does go through the PhysX
/// scene.</para>
///
/// <para>MULTIPLAYER — ZERO WIRE BYTES, AND CORRECT BY CONSTRUCTION.
/// <see cref="FigureGrabbable.HeldBy"/> only ever returns a figure attached to a LOCAL hand, so the
/// probe can only ever be attached to a figure this player is holding, driven by this player's own
/// free hand. A peer holding their own mini runs this same code on their own client against their
/// own hand, which is exactly the behaviour asked for ("a peer's cloth reacting to THEIR free hand
/// is correct; it must not react to yours"). Cloth vertex positions are not a wire field and never
/// have been — <c>NetFigures</c> carries a figure's POSE and its held SIZE factor (extension record
/// 30), not its simulation — so two clients already disagree about the exact swing of a cape and
/// always have. Nothing here adds a field, a record or a byte.</para>
///
/// <para>THE ORIGINAL ARRAYS ARE CAPTURED AND RESTORED. A character's Cloth may already carry
/// authored body colliders (that is what keeps a cape off the model's own back), so the probe is
/// APPENDED to whatever was there and the captured array is written back verbatim on detach. Never
/// assigned over.</para>
///
/// <para>REJECTED ALTERNATIVES.
/// (a) <c>capsuleColliders</c> with a single <see cref="CapsuleCollider"/> on the hand. Same cost,
/// but a capsule is a fixed radius along its whole length and a hand is not — the sphere PAIR gives
/// a wrist-to-fingertip taper for the same money.
/// (b) Colliders parented UNDER the figure so they inherit its scale. Rejected: the radius would
/// then be multiplied by the held size, so the hand would get fatter as he enlarged the mini. The
/// probe belongs in world space because the hand is in world space.
/// (c) A trigger collider. Unity does not document whether <c>Cloth</c> honours
/// <c>isTrigger</c>, and this lane does not ship behaviour it has not measured; the Ignore Raycast
/// layer achieves the "invisible to picking" half without depending on that.
/// (d) Driving it from the HOLDING hand as well. That hand already drives the cape by moving the
/// figure, and adding a collider there would fight the hold rather than add to it.</para>
///
/// <para>WHAT NEEDS HIS HARDWARE. Whether the two radii read as "my hand" rather than "a ball near
/// my hand", and whether this fights the stretch gesture — the free hand approaching the figure is
/// ALSO how <see cref="FigureStretch"/> is armed, which is precisely why he asked for a dial. If it
/// fights, <c>[FigureGrab] ClothFollowsFreeHand</c> turns it off without touching anything
/// else.</para>
/// </summary>
internal static class FigureClothHands
{
    /// <summary>Sphere radius at the palm, REAL METRES AT THE HAND. A palm is about 7 cm across.</summary>
    private const float PalmRadiusRealMeters = 0.035f;

    /// <summary>Sphere radius at the index tip, REAL METRES AT THE HAND. A fingertip is about
    /// 2 cm across, and the pair between this and the palm is the conic capsule.</summary>
    private const float TipRadiusRealMeters = 0.010f;

    /// <summary>How much further than the attach reach the hand must travel before the probe is
    /// detached again. Pure hysteresis: without it a hand resting exactly on the boundary would
    /// assign and clear the arrays on alternate frames forever.</summary>
    private const float ReleaseFactor = 1.5f;

    /// <summary>Unity's built-in "Ignore Raycast" layer — the one layer
    /// <c>Physics.DefaultRaycastLayers</c> excludes.</summary>
    private const int IgnoreRaycastLayer = 2;

    private static GameObject? _probe;
    private static Transform? _palm;
    private static Transform? _tip;
    private static SphereCollider? _palmSphere;
    private static SphereCollider? _tipSphere;
    private static ClothSphereColliderPair[] _pair = System.Array.Empty<ClothSphereColliderPair>();

    /// <summary>The figure the probe is currently appended to, or null.</summary>
    private static GameObject? _attachedRoot;

    /// <summary>The last figure whose subtree held no simulating Cloth. Without it, holding a
    /// cloth-less figure with the free hand nearby re-runs <c>GetComponentsInChildren&lt;Cloth&gt;</c>
    /// over the whole subtree EVERY FRAME, because <see cref="Attach"/> never sets
    /// <see cref="_attachedRoot"/> in that case and so never looks attached. A per-frame subtree
    /// sweep is this project's default suspect for a rising frame cost and it is not going to be
    /// introduced here by omission.</summary>
    private static GameObject? _noClothRoot;

    /// <summary>The cloths on that figure, and the sphereCollider array each one had BEFORE we
    /// appended to it. Restored verbatim on detach.</summary>
    private static readonly List<Cloth> _cloths = new(2);
    private static readonly List<ClothSphereColliderPair[]> _original = new(2);
    private static bool _logged;

    /// <summary>
    /// Attach, drive or detach the free hand's probe. Called once per frame from
    /// <c>FigureGrabbable.TickHeldScale</c>, beside <see cref="FigureCloth.Tick"/> and for the same
    /// reason: it belongs to the per-frame job that owns held figures, and it must not become a new
    /// step in the locked frame order. Strict no-op with nothing held.
    /// </summary>
    internal static void Tick()
    {
        if (FigureGrabConfig.ClothFollowsFreeHand == null || !FigureGrabConfig.ClothFollowsFreeHand.Value)
        {
            Detach();
            return;
        }

        // EXACTLY ONE hand holding. Two held figures leaves no free hand; none held leaves nothing
        // to touch. HeldBy only ever returns a LOCALLY attached grabbable — see MULTIPLAYER above.
        FigureGrabbable? left = FigureGrabbable.HeldBy(HandSide.Left);
        FigureGrabbable? right = FigureGrabbable.HeldBy(HandSide.Right);
        FigureGrabbable? held;
        HandSide freeSide;
        if (left != null && right == null) { held = left; freeSide = HandSide.Right; }
        else if (right != null && left == null) { held = right; freeSide = HandSide.Left; }
        else { Detach(); return; }

        GameObject? root = held.RootObject;
        VRHand? free = VRHands.Get(freeSide);
        if (root == null || free == null || !free.IsTracked || !free.Rig.IsComplete)
        {
            Detach();
            return;
        }

        float scale = Mathf.Max(free.WorldScale, 1e-4f);
        Vector3 palm = free.Rig.PalmCenter.position;
        float reach = FigureGrabConfig.ClothHandReachRealMeters * scale;
        float gate = ReferenceEquals(root, _attachedRoot) ? reach * ReleaseFactor : reach;
        if ((palm - root.transform.position).sqrMagnitude > gate * gate)
        {
            Detach();
            return;
        }

        if (!ReferenceEquals(root, _attachedRoot))
        {
            if (ReferenceEquals(root, _noClothRoot))
                return;   // already scanned: nothing on this figure simulates. Do not sweep again.
            Detach();
            if (!Attach(root))
            {
                _noClothRoot = root;
                return;
            }
        }

        // The probe object is at the scene root at unit scale, so these radii ARE world radii.
        Vector3 tip = free.Rig.IndexTip.position;
        _palm!.position = palm;
        _tip!.position = tip;
        _palmSphere!.radius = PalmRadiusRealMeters * scale;
        _tipSphere!.radius = TipRadiusRealMeters * scale;
    }

    /// <summary>Detach and forget everything (driver teardown / scene change). The cloths are
    /// restored first, because a scene that is merely being re-entered keeps its figures.</summary>
    internal static void Clear()
    {
        Detach();
        if (_probe != null)
            Object.Destroy(_probe);
        _probe = null;
        _palm = null;
        _tip = null;
        _palmSphere = null;
        _tipSphere = null;
        _pair = System.Array.Empty<ClothSphereColliderPair>();
    }

    private static bool Attach(GameObject root)
    {
        if (!EnsureProbe())
            return false;

        Cloth[] found = root.GetComponentsInChildren<Cloth>(true);
        for (int i = 0; i < found.Length; i++)
        {
            Cloth c = found[i];
            if (c == null || !c.enabled)
                continue;  // a cloth that is not simulating cannot be disturbed by anything

            ClothSphereColliderPair[] was = c.sphereColliders ?? System.Array.Empty<ClothSphereColliderPair>();
            var next = new ClothSphereColliderPair[was.Length + 1];
            for (int k = 0; k < was.Length; k++)
                next[k] = was[k];
            next[was.Length] = _pair[0];   // APPENDED — the authored body colliders stay

            _cloths.Add(c);
            _original.Add(was);
            c.sphereColliders = next;
        }

        if (_cloths.Count == 0)
            return false;

        _attachedRoot = root;

        if (!_logged)
        {
            _logged = true;
            VRLog.Info("FigureGrab",
                $"FREE-HAND CLOTH on '{root.name}': {_cloths.Count} simulating Cloth(s) of "
                + $"{found.Length} in the subtree now collide with a conic capsule between the free "
                + $"hand's palm ({PalmRadiusRealMeters * 1000f:F0} mm real) and index tip "
                + $"({TipRadiusRealMeters * 1000f:F0} mm real), appended to the "
                + $"{_original[0].Length} authored sphere collider pair(s) that were already there "
                + "(never assigned over). Assigning the array does NOT re-cook the fabric — measured "
                + "0.0083-0.0165 ms and FLAT in vertex count, against 20.1-56.4 ms for an enable "
                + "transition which is linear in it. Detaches when the free hand leaves "
                + $"{FigureGrabConfig.ClothHandReachRealMeters * 1000f:F0} mm real "
                + $"(x{ReleaseFactor} hysteresis) or the figure is released. Local presentation "
                + "only: no wire field, and a peer's cape reacts to THEIR free hand on THEIR client. "
                + "Dial: [FigureGrab] ClothFollowsFreeHand.");
        }

        return true;
    }

    private static void Detach()
    {
        for (int i = 0; i < _cloths.Count; i++)
        {
            Cloth c = _cloths[i];
            if (c != null)
                c.sphereColliders = _original[i];
        }
        _cloths.Clear();
        _original.Clear();
        _attachedRoot = null;

        // The memo is dropped here and NOT while the same figure is still under the hand, which
        // makes it one scan per APPROACH rather than one per frame or one per figure for ever. That
        // matters because a figure's visual subtree is asynchronous (CharacterManager's Addressables
        // InstantiateAsync, and ChangeModelSMB replacing the model wholesale), so a cloth that was
        // not there on the first look must still be found on the next reach.
        _noClothRoot = null;
    }

    private static bool EnsureProbe()
    {
        if (_probe != null && _palmSphere != null && _tipSphere != null)
            return true;

        if (_probe != null)
            Object.Destroy(_probe);

        _probe = new GameObject("GloomhavenVR.ClothHandProbe") { layer = IgnoreRaycastLayer };
        Object.DontDestroyOnLoad(_probe);

        var palmGo = new GameObject("Palm") { layer = IgnoreRaycastLayer };
        var tipGo = new GameObject("Tip") { layer = IgnoreRaycastLayer };
        palmGo.transform.SetParent(_probe.transform, false);
        tipGo.transform.SetParent(_probe.transform, false);

        _palm = palmGo.transform;
        _tip = tipGo.transform;
        _palmSphere = palmGo.AddComponent<SphereCollider>();
        _tipSphere = tipGo.AddComponent<SphereCollider>();
        _palmSphere.radius = PalmRadiusRealMeters;
        _tipSphere.radius = TipRadiusRealMeters;
        _pair = new[] { new ClothSphereColliderPair(_palmSphere, _tipSphere) };
        return true;
    }
}
