using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// THE HANDS DISTURB THE SCENERY — curtains, hangings and every other piece of simulated cloth in
/// the room move out of the way when you reach through them, with EITHER hand, holding anything or
/// nothing.
///
/// <para>USER REQUEST (2026-08-29): of the six candidates in the interaction survey, "Ich will die
/// 6" — the environment reacting to the hands. This is that.</para>
///
/// <para>THE ART IS ALREADY THERE. This adds no assets and authors no cloth: the game's own
/// scenario scenery ships real <c>UnityEngine.Cloth</c> — <c>EN_CR_Curtain_Cloth</c> and
/// <c>EN_CR_Hanging_01_Cloth_Post</c> both appear by name in the wall-fade census
/// (<c>Core/WallFade/WallSegmentFade.*</c>), which is how this was confirmed without a headset.
/// Whatever a room has, this finds; a room with none costs a dictionary lookup and a return.</para>
///
/// <para>IT IS THE SAME PROBE <see cref="Board.FigureGrab.FigureClothHands"/> ALREADY USES, pointed
/// somewhere else. A <see cref="ClothSphereColliderPair"/> is a conic capsule between two spheres,
/// which is the shape of a hand from palm to fingertip; the pair sits on a scene-root object at
/// unit scale on Unity's Ignore Raycast layer, so a radius written in world units IS that radius in
/// the world and nothing can pick it. That class measured the cost and the numbers carry over
/// unchanged, because it is the same operation on the same array: assigning
/// <c>sphereColliders</c> is 0.008–0.017 ms and — the part that settles it — FLAT in vertex count,
/// while a cloth re-cook is linear in it. Assigning does not re-cook.</para>
///
/// <para>WHAT IS DIFFERENT HERE, and each difference is a hazard the figure version does not have:</para>
/// <list type="bullet">
/// <item><description>TWO HANDS, not one. The figure probe exists only while exactly one hand holds
/// a mini, so it can be a single static probe. Here either hand may be doing anything, so there are
/// two probes and a cloth may be touched by BOTH at once. That makes the capture/restore
/// bookkeeping the hard part of this file — see <see cref="Rewrite"/>.</description></item>
/// <item><description>MANY CLOTHS, not one figure's. Capped at
/// <see cref="MaxClothsPerHand"/> per hand, nearest first.</description></item>
/// <item><description>NO OWNER TO HANG THE SCAN OFF. A held figure gives the other class a root to
/// walk; a room does not, so this keeps a slowly-refreshed registry — see
/// <see cref="RescanSeconds"/>.</description></item>
/// </list>
///
/// <para>ACTOR CLOTH IS EXCLUDED, STRUCTURALLY. Any cloth with an <c>ActorBehaviour</c> above it
/// belongs to a figure, and figures already have an owner for this
/// (<see cref="Board.FigureGrab.FigureClothHands"/>). Both classes write the WHOLE
/// <c>sphereColliders</c> array, so two owners on one cloth would clobber each other's capture and
/// leave a stale probe in it forever. The exclusion is the fix, and it is also simply what the user
/// asked for: this one is about the environment.</para>
///
/// <para>MULTIPLAYER — ZERO WIRE BYTES, and the same reasoning the figure version records: cloth
/// vertex positions have never been a wire field, every client simulates its own scenery, and each
/// player's own hands disturb their own copy. Nothing here is authoritative, nothing desyncs, and a
/// peer who is not running this build sees their curtains hang still.</para>
///
/// <para>SCALE. Every radius and every reach is REAL METRES AT THE HAND multiplied by
/// <c>VRHand.WorldScale</c> — the convention <c>ProximityGrabber.ReachMeters</c> and
/// <c>FigureGrabConfig.PickRadiusRealMeters</c> already follow, and the one the user's own ruling
/// demands ("der Bereich nicht größer wird mit dem zoomen sondern an der Hand bleibt"). The mod's
/// zoom scales the RIG, so the room keeps its world size while the player grows: a hand-anchored
/// reach is the only kind that still means "my hand is in the curtain" at every zoom.</para>
/// </summary>
internal static class SceneClothHands
{
    /// <summary>Palm sphere, real metres at the hand. The wide end of the conic capsule.</summary>
    private const float PalmRadiusRealMeters = 0.035f;

    /// <summary>Fingertip sphere, real metres at the hand. The narrow end.</summary>
    private const float TipRadiusRealMeters = 0.010f;

    /// <summary>How close the palm must come to a cloth's BOUNDS before its probe is attached, in
    /// real metres at the hand. Generous compared with the figure pick radius on purpose: this
    /// arms a collider, it does not grab anything, so being early costs a few microseconds and
    /// being late costs the hand passing through a curtain untouched.</summary>
    private const float ReachRealMeters = 0.25f;

    /// <summary>Hysteresis on the release side of that gate — the same 1.5 the figure probe uses,
    /// so a hand hovering on the boundary cannot churn the assignment every frame.</summary>
    private const float ReleaseFactor = 1.5f;

    /// <summary>Most cloths one hand arms at once. A hand cannot physically be inside more than a
    /// couple; the cap is what stops a room full of banners turning one reach into a sweep.</summary>
    private const int MaxClothsPerHand = 4;

    /// <summary>
    /// Seconds between registry rescans.
    ///
    /// <para>THE SCAN IS THE ONLY REAL COST IN THIS FILE and it is a
    /// <c>FindObjectsOfType</c>, which this project has been burned by twice — "near-free is false
    /// and has shipped twice", and once it owned 12.6 ms of an 11.11 ms budget. So it does NOT run
    /// per frame: it runs at this cadence, only while a hand is tracked, and the FIRST few scans
    /// are timed and logged with their object count so the next hardware log carries the real
    /// number instead of this comment's assurance.</para>
    /// </summary>
    private const float RescanSeconds = 3f;

    /// <summary>Unity's built-in Ignore Raycast layer — invisible to
    /// <c>Physics.DefaultRaycastLayers</c>, so neither the mod's picking nor the game's can see the
    /// probe spheres.</summary>
    private const int IgnoreRaycastLayer = 2;

    private static readonly Probe Left = new(HandSide.Left);
    private static readonly Probe Right = new(HandSide.Right);

    // The registry: every scenery cloth in the scene, with the renderer bounds used for the
    // distance gate. Rebuilt on a slow cadence; entries whose cloth died are dropped on use.
    private static readonly List<Cloth> _scene = new(16);
    private static float _nextScanAt;
    private static int _scansTimed;

    // ORIGINAL collider arrays, captured ONCE per cloth on the first attach by either hand and
    // written back verbatim when the last hand lets go. Keyed by cloth because a curtain may be
    // touched by both hands at the same time and there is exactly one authored array to restore.
    private static readonly Dictionary<Cloth, ClothSphereColliderPair[]> _original = new(8);

    private static bool _loggedFirstAttach;

    /// <summary>The feature switch ([Hands] HandsDisturbScenery); false before the config is bound,
    /// which is the same defensive shape <c>HandGhosts.Enabled</c> uses and for the same reason —
    /// this is called from a per-frame path that runs before and after the config's lifetime.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return HandsConfig.HandsDisturbScenery != null
                       && HandsConfig.HandsDisturbScenery.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Per-frame step, called from <see cref="HandsDriver"/> under its own
    /// <see cref="TickGuard"/>. Strict no-op when the feature is off or no hand is tracked.</summary>
    internal static void Tick()
    {
        if (!Enabled)
        {
            Clear();
            return;
        }

        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        bool anyTracked = (left != null && left.IsTracked) || (right != null && right.IsTracked);
        if (!anyTracked)
        {
            Clear();
            return;
        }

        Rescan();
        Left.Tick(left);
        Right.Tick(right);
    }

    /// <summary>Module shutdown / hot reload / scene change: restore every cloth and destroy the
    /// probes. The arrays are written back FIRST — a scene that is merely being re-entered keeps
    /// its curtains, and a curtain left holding a destroyed collider is a curtain that never
    /// simulates correctly again.</summary>
    internal static void Shutdown()
    {
        Clear();
        Left.Destroy();
        Right.Destroy();
        _scene.Clear();
        _nextScanAt = 0f;
        _scansTimed = 0;
        _loggedFirstAttach = false;
    }

    private static void Clear()
    {
        Left.Release();
        Right.Release();
        RestoreAll();
    }

    private static void Rescan()
    {
        float now = Time.unscaledTime;
        if (now < _nextScanAt && _scene.Count > 0)
            return;
        _nextScanAt = now + RescaleGuard(RescanSeconds);

        var watch = _scansTimed < 3 ? System.Diagnostics.Stopwatch.StartNew() : null;
        Cloth[] found = Object.FindObjectsOfType<Cloth>();
        _scene.Clear();
        int actorOwned = 0;
        for (int i = 0; i < found.Length; i++)
        {
            Cloth c = found[i];
            if (c == null || !c.enabled)
                continue;   // a cloth that is not simulating cannot be disturbed by anything
            // ACTOR CLOTH IS SOMEBODY ELSE'S. See the class doc: FigureClothHands owns a held
            // figure's cape, both classes write the whole array, and two owners on one cloth
            // corrupt each other's capture. "Is this cloth part of an actor?" is a RELATEDNESS
            // question, which is the one thing GetComponentInParent actually answers.
            if (c.GetComponentInParent<ActorBehaviour>() != null)
            {
                actorOwned++;
                continue;
            }
            _scene.Add(c);
        }

        if (watch != null)
        {
            watch.Stop();
            _scansTimed++;
            VRLog.Info("Hands", $"Scenery cloth scan #{_scansTimed}: {_scene.Count} simulating "
                + $"scenery cloth(s) of {found.Length} found ({actorOwned} skipped as actor cloth) "
                + $"in {watch.Elapsed.TotalMilliseconds:F3} ms. This runs every "
                + $"{RescanSeconds:0.#} s while a hand is tracked, never per frame.");
        }
    }

    /// <summary>The rescan cadence, never below a frame — a defensive clamp so a future dial or a
    /// zero constant cannot turn the registry sweep into a per-frame one.</summary>
    private static float RescaleGuard(float seconds) => Mathf.Max(0.5f, seconds);

    /// <summary>Rewrite one cloth's collider array from the ORIGINAL plus whichever hands are
    /// currently on it, and restore + forget it when neither is.
    ///
    /// <para>This is the whole reason the captures live in a dictionary keyed by cloth rather than
    /// in each probe. With two hands the naive per-probe capture is wrong in a way that does not
    /// show up until both touch the same curtain: hand B captures an array that ALREADY contains
    /// hand A's pair, and when B restores it, A's pair is written back into a cloth A has long
    /// since left — permanently, because A restored its own (correct) capture first. One capture
    /// per cloth, taken by whoever arrives first, makes that unrepresentable.</para></summary>
    private static void Rewrite(Cloth? c)
    {
        if (c == null)
            return;
        bool l = Left.Holds(c);
        bool r = Right.Holds(c);
        if (!_original.TryGetValue(c, out ClothSphereColliderPair[] original))
        {
            if (!l && !r)
                return;
            original = c.sphereColliders ?? System.Array.Empty<ClothSphereColliderPair>();
            _original[c] = original;
        }

        if (!l && !r)
        {
            c.sphereColliders = original;
            _original.Remove(c);
            return;
        }

        int extra = (l ? 1 : 0) + (r ? 1 : 0);
        var next = new ClothSphereColliderPair[original.Length + extra];
        for (int k = 0; k < original.Length; k++)
            next[k] = original[k];          // APPENDED — the authored body colliders stay
        int at = original.Length;
        if (l)
            next[at++] = Left.Pair;
        if (r)
            next[at] = Right.Pair;
        c.sphereColliders = next;

        if (!_loggedFirstAttach)
        {
            _loggedFirstAttach = true;
            VRLog.Info("Hands", "Scenery cloth: a hand is inside a curtain/hanging for the first "
                + "time — its authored colliders are kept and the hand's sphere pair is APPENDED. "
                + "Restored verbatim when the last hand leaves. No wire field: every client "
                + "simulates its own scenery and its own hands.");
        }
    }

    private static void RestoreAll()
    {
        if (_original.Count == 0)
            return;
        foreach (KeyValuePair<Cloth, ClothSphereColliderPair[]> pair in _original)
        {
            if (pair.Key != null)
                pair.Key.sphereColliders = pair.Value;
        }
        _original.Clear();
    }

    /// <summary>One hand's probe: two spheres at the scene root, the cloths it is currently inside,
    /// and the gate that decides which those are.</summary>
    private sealed class Probe
    {
        private readonly HandSide _side;
        private readonly List<Cloth> _held = new(MaxClothsPerHand);
        private GameObject? _root;
        private Transform? _palm;
        private Transform? _tip;
        private SphereCollider? _palmSphere;
        private SphereCollider? _tipSphere;

        internal Probe(HandSide side) => _side = side;

        internal ClothSphereColliderPair Pair => new(_palmSphere, _tipSphere);

        internal bool Holds(Cloth c) => _held.Contains(c);

        internal void Tick(VRHand? hand)
        {
            if (hand == null || !hand.IsTracked || hand.Rig == null || !hand.Rig.IsComplete)
            {
                Release();
                return;
            }
            if (!Ensure())
            {
                Release();
                return;
            }

            float scale = Mathf.Max(hand.WorldScale, 1e-4f);
            Vector3 palm = hand.Rig.PalmCenter.position;
            float reach = ReachRealMeters * scale;

            // The probe object is at the scene root at unit scale, so these radii ARE world radii.
            _palm!.position = palm;
            _tip!.position = hand.Rig.IndexTip.position;
            _palmSphere!.radius = PalmRadiusRealMeters * scale;
            _tipSphere!.radius = TipRadiusRealMeters * scale;

            // DROP what is out of range first, on the WIDER release radius, then take what is in.
            // Doing it in that order means a hand sliding along a row of banners lets go of one
            // before it can be refused the next by the cap.
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                Cloth c = _held[i];
                if (c == null || !c.enabled || Distance(c, palm) > reach * ReleaseFactor)
                {
                    _held.RemoveAt(i);
                    Rewrite(c);
                }
            }

            if (_held.Count >= MaxClothsPerHand)
                return;
            for (int i = 0; i < _scene.Count && _held.Count < MaxClothsPerHand; i++)
            {
                Cloth c = _scene[i];
                if (c == null || !c.enabled || _held.Contains(c))
                    continue;
                if (Distance(c, palm) > reach)
                    continue;
                _held.Add(c);
                Rewrite(c);
            }
        }

        /// <summary>Palm-to-cloth distance, measured against the cloth's RENDERER BOUNDS rather
        /// than its transform origin. A curtain's origin is its hanging point; the fabric is metres
        /// of it below, so an origin distance would arm the probe only when the hand was up at the
        /// rail.</summary>
        private static float Distance(Cloth c, Vector3 palm)
        {
            var r = c.GetComponent<Renderer>();
            if (r == null)
                return Vector3.Distance(c.transform.position, palm);
            return Mathf.Sqrt(r.bounds.SqrDistance(palm));
        }

        internal void Release()
        {
            if (_held.Count == 0)
                return;
            var was = _held.ToArray();
            _held.Clear();
            for (int i = 0; i < was.Length; i++)
                Rewrite(was[i]);
        }

        internal void Destroy()
        {
            Release();
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _palm = null;
            _tip = null;
            _palmSphere = null;
            _tipSphere = null;
        }

        private bool Ensure()
        {
            if (_root != null && _palmSphere != null && _tipSphere != null)
                return true;
            if (_root != null)
                Object.Destroy(_root);

            _root = new GameObject($"GloomhavenVR.SceneClothProbe.{_side}")
            { layer = IgnoreRaycastLayer };
            Object.DontDestroyOnLoad(_root);
            var palmGo = new GameObject("Palm") { layer = IgnoreRaycastLayer };
            var tipGo = new GameObject("Tip") { layer = IgnoreRaycastLayer };
            palmGo.transform.SetParent(_root.transform, false);
            tipGo.transform.SetParent(_root.transform, false);
            _palm = palmGo.transform;
            _tip = tipGo.transform;
            _palmSphere = palmGo.AddComponent<SphereCollider>();
            _tipSphere = tipGo.AddComponent<SphereCollider>();
            _palmSphere.radius = PalmRadiusRealMeters;
            _tipSphere.radius = TipRadiusRealMeters;
            return true;
        }
    }
}
