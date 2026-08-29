using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GloomhavenVR.Hands;

/// <summary>
/// Persistent driver that owns both <see cref="VRHand"/>s. Hands live under the
/// Phase-1 rig root (<see cref="VRRigDriver.RigRoot"/>) so device poses share the
/// head's tracking space and diorama scale; they are torn down with the rig.
///
/// Desktop simulation ([Dev] SimulateHands, no HMD): hands are parented to the main
/// camera at diorama scale and animated procedurally (bobbing pose; hold T = trigger,
/// hold G = grip via InputSystem's Keyboard) so every consumer of the hand API can be
/// exercised flat.
///
/// RIG-REBUILD CHAIN (hardware test #3, root cause #4): this driver POLLS
/// <see cref="VRRigDriver.RigRoot"/> every frame — when the rig driver tears down and
/// rebuilds (head camera died/disabled/scene swap), the old hands root is destroyed
/// with the old rig root (it is a child) and <see cref="Build"/> re-creates the hands
/// under the new root the same/next frame. The chain is rig-state-driven, never
/// scene-driven: no scene event is needed for hands to re-home. The MainMenu failure
/// was upstream (the rig itself never rebuilt), not here.
///
/// Applies the <see cref="VRModeStateMachine"/> interactor policy on every mode change.
/// </summary>
internal sealed class HandsDriver : MonoBehaviour
{
    private const float SimScale = 12f;

    private GameObject? _handsRoot;
    private VRHand? _left;
    private VRHand? _right;
    private bool _simActive;
    private float _simTrigger;
    private float _simGrip;

    // Cached step delegates so the guarded per-frame calls allocate nothing (instance
    // method groups would allocate a fresh delegate each frame). Built once in Awake.
    private System.Action? _tickRig;
    private System.Action? _tickSim;
    private System.Action? _tickGrip;
    private System.Action? _tickCloth;
    private System.Action? _tickGhost;

    private void Awake()
    {
        _tickRig = TickRig;
        _tickSim = AnimateSimulation;
        _tickGrip = Cards.HeldCardGrip.Tick;
        _tickCloth = SceneClothHands.Tick;
        _tickGhost = HandGhosts.Tick;

        // BOOT STALL (hardware log ModBuild 107 / b765a5b6e): the mod's first touch of
        // gloomhavenvr.bundle cost 981.48 ms on the main thread, under the black Unity splash.
        // This Awake runs at BepInEx chainloader time, several hundred ms BEFORE the hands are
        // first built (frame 4), so starting the load here lets Unity's loading thread inflate
        // the archive concurrently with the game's own boot. Measurement, root cause and the
        // rejected alternatives all live on HandVisuals.Prewarm — do not restate them here.
        HandVisuals.Prewarm();
    }

    private void OnEnable()
    {
        VRModeStateMachine.ModeChanged += OnModeChanged;
        // Dominance is live-switchable (Menu2D trigger switch / settings panel toggle
        // both write [Hands] PrimaryHand) — reapply the per-hand masks on change.
        Plugin.PrimaryHand.SettingChanged += OnPrimaryHandChanged;
        // Hand STYLE is live-switchable too (settings panel cycle writes [Hands]
        // HandStyle) — tear the hand tree down; TickRig rebuilds it next frame with the
        // newly selected prefab pair (the same rig-rebuild chain a scene swap uses,
        // mirroring CardsConfig.Board.SettingChanged -> CardsDriver.RebuildBoard).
        Plugin.HandStyle.SettingChanged += OnHandStyleChanged;
    }

    private void OnDisable()
    {
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        Plugin.PrimaryHand.SettingChanged -= OnPrimaryHandChanged;
        Plugin.HandStyle.SettingChanged -= OnHandStyleChanged;
    }

    private void OnHandStyleChanged(object sender, System.EventArgs e)
    {
        VRLog.Info("Hands", $"[Hands] HandStyle is now '{Plugin.HandStyle.Value}' — " +
                            "rebuilding the hand visuals.");
        TearDown(); // TickRig re-Builds under the current rig root on the next frame
    }

    private void OnPrimaryHandChanged(object sender, System.EventArgs e)
    {
        ApplyMode(VRModeStateMachine.CurrentMode);
        VRLog.Info("Hands", $"[Hands] PrimaryHand is now '{Plugin.PrimaryHand.Value}' — " +
                            "per-hand interactor masks reapplied.");
    }

    private void Update()
    {
        // Both per-frame steps are ISOLATED + attributed via the shared Core.TickGuard so
        // a throw in the rig-homing/build path can't abort simulation (and vice versa) and
        // the log NAMES the thrower ("[Hands] Tick 'Hands.<step>' threw <exc + stack>")
        // instead of an anonymous per-frame NullReferenceException. Order preserved:
        // rig-homing first (sets _simActive + hand refs), simulation after. Cached
        // delegates → no per-frame allocation.
        TickGuard.Run("Hands.Rig", _tickRig!);
        if (_simActive)
            TickGuard.Run("Hands.Simulation", _tickSim!);
        // Held-card MODE ([Cards] InHandHold): is each hand READING its card (it billboards to
        // your head) or HOLDING it (rigid in the fist, turnable, shown to others)? This sits
        // between the two steps it sits between for a reason: AFTER the rig step, because it reads
        // what each hand grabbed and what its grip button is doing this frame, and BEFORE the ghost
        // step, which is one of its five readers — a hand really holding a card must not fade.
        TickGuard.Run("Hands.CardGrip", _tickGrip!);
        // Scenery cloth ([Hands] HandsDisturbScenery): arm the hand-shaped collider probe on any
        // curtain or hanging a hand is inside. AFTER the rig step, because it reads live hand
        // anchors, and under its own guard because it writes into the GAME's Cloth components —
        // a surprise there must not be able to abort hand tracking itself.
        TickGuard.Run("Hands.SceneCloth", _tickCloth!);
        // Ghost hand ([Hands] GhostHandOnFan): fade the hand carrying the OPEN card fan. Runs
        // AFTER the rig step so a hand rebuilt this frame is already in place, and under its own
        // guard so a material/shader surprise can never abort hand tracking itself.
        TickGuard.Run("Hands.Ghost", _tickGhost!);
    }

    /// <summary>
    /// Per-frame rig-homing: resolve the parent (rig root in VR, main camera in desktop
    /// simulation) and build / reparent / tear down the hand tree to match. Extracted from
    /// <see cref="Update"/> so it can run under <see cref="TickGuard"/>.
    /// </summary>
    private void TickRig()
    {
        // Non-blocking: realizes the boot prewarm the frame it finishes (one isDone read), so the
        // bundle enters Unity's loaded-bundle registry — and becomes adoptable by Cards/WorldUI —
        // as early as possible rather than only when the hands are built. See HandVisuals.Prewarm.
        HandVisuals.PumpPrewarm();

        bool simulate = Plugin.SimulateHands.Value && !VRSession.IsRunning;

        Transform? parent = null;
        if (VRSession.IsRunning)
        {
            parent = VRRigDriver.RigRoot;
        }
        else if (simulate)
        {
            Camera? cam = Camera.main;
            if (cam != null)
                parent = cam.transform;
        }

        if (parent == null)
        {
            // Also covers the rig root being destroyed underneath us (Unity-null):
            // clear the static hand access even though the GOs are already gone.
            if (_handsRoot != null || VRHands.Left != null)
                TearDown();
            return;
        }

        if (_handsRoot == null)
            Build(parent, simulate); // includes rebuilding after external destruction
        else if (_handsRoot.transform.parent != parent)
            Reparent(parent, simulate);

        EnforceGlobalSkinWeights();
    }

    /// <summary>
    /// INTRO FINGER DISTORTION (repeat complaint — root cause of the 5b119e0 miss):
    /// at boot the game enables its LOWEST quality level ("[PlatformLayer.cs] Enabling
    /// QualitySettings named Fastest", Player.log), whose skinWeights is ONE bone; the
    /// user's saved graphics settings only apply once the menu is up. Unity's global
    /// <see cref="QualitySettings.skinWeights"/> is a hard CAP that per-renderer
    /// <see cref="SkinnedMeshRenderer.quality"/> can only lower, never raise — so the
    /// Bone4 pin in HandVisuals was silently clamped to 1 bone during the intro, and
    /// curling a finger snapped every vertex rigidly to its single heaviest bone
    /// (proximity-skinned glove → extreme tearing). Enforce a 4-bone global floor every
    /// frame while hands exist; the game re-lowers it on quality-level swaps and via its
    /// own skin-weights selector, hence re-assert (cheap enum compare) instead of
    /// set-once. Raising the global only lifts a cap — meshes authored with fewer
    /// weights are unaffected.
    /// </summary>
    private void EnforceGlobalSkinWeights()
    {
        SkinWeights current = QualitySettings.skinWeights;
        if ((int)current >= (int)SkinWeights.FourBones)
            return;
        QualitySettings.skinWeights = SkinWeights.FourBones;
        string quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        float rootScale = _handsRoot != null ? _handsRoot.transform.lossyScale.x : -1f;
        float worldScale = _left != null ? _left.WorldScale : -1f;
        VRLog.Info("Hands", $"Global skinWeights raised {current} → FourBones (quality level '{quality}'); " +
                            $"the global value CAPS per-renderer SkinQuality, so the Bone4 pin alone cannot fix " +
                            $"1-bone intro skinning. handsRoot lossyScale={rootScale:F2}, hand WorldScale={worldScale:F2}.");
    }

    // ISOLATED for the reason spelled out on VRHand.OnDestroy: this runs in the destroy wave of a
    // scenario teardown, and an unguarded throw would leave VRHands pointing at dead hands and the
    // ghost materials un-freed, under one anonymous stackless NullReferenceException.
    private void OnDestroy() => TickGuard.Run("Hands.Teardown.Driver", TearDown, "Hands");

    private void OnModeChanged(VRModeChange change) => ApplyMode(change.To);

    private void ApplyMode(VRMode mode)
    {
        // P5 (MISSION A.4): the policy is per-hand — the dominant hand may keep the
        // ray while the non-dominant hand owns the palm gate/fan (matrix in
        // docs/INTERFACES-P2.md §4). Dominance follows [Hands] PrimaryHand.
        bool leftIsDominant = VRHands.Primary == _left && _left != null;
        _left?.SetInteractorMask(VRModeStateMachine.InteractorsFor(
            mode, leftIsDominant ? HandRole.Dominant : HandRole.NonDominant));
        _right?.SetInteractorMask(VRModeStateMachine.InteractorsFor(
            mode, leftIsDominant ? HandRole.NonDominant : HandRole.Dominant));
    }

    // ---- build / teardown -------------------------------------------------------------

    private void Build(Transform parent, bool simulate)
    {
        _simActive = simulate;

        _handsRoot = new GameObject("GloomhavenVR.Hands");
        ConfigureRoot(parent, simulate);

        _left = CreateHand(HandSide.Left);
        _right = CreateHand(HandSide.Right);
        VRHands.Set(_left, _right);

        // Whole hand tree (palms, fingers, anchors) on the mod layer so the head
        // camera renders it regardless of the game camera's mask (CAMERA-POLICY §2).
        // No-op in desktop simulation (VRLayers.Apply gates on VRSession.IsRunning).
        VRLayers.Apply(_handsRoot);

        ApplyMode(VRModeStateMachine.CurrentMode);
        VRLog.Info("Hands", $"Hands built under '{parent.name}'{(simulate ? " (SIMULATED)" : string.Empty)}.");
    }

    private void ConfigureRoot(Transform parent, bool simulate)
    {
        Transform root = _handsRoot!.transform;
        root.SetParent(parent, worldPositionStays: false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        // Under the rig root the diorama scale is inherited; the sim parent (camera)
        // is unscaled, so apply an equivalent scale locally.
        root.localScale = simulate ? Vector3.one * SimScale : Vector3.one;
    }

    private VRHand CreateHand(HandSide side)
    {
        var go = new GameObject($"VRHand_{side}");
        go.transform.SetParent(_handsRoot!.transform, worldPositionStays: false);
        var hand = go.AddComponent<VRHand>();
        hand.Initialize(side);
        hand.SetSimulated(_simActive);
        return hand;
    }

    private void Reparent(Transform parent, bool simulate)
    {
        _simActive = simulate;
        ConfigureRoot(parent, simulate);
        _left?.SetSimulated(simulate);
        _right?.SetSimulated(simulate);
    }

    private void TearDown()
    {
        VRHands.Set(null, null);
        // Ghost hand: restore + free the cloned materials BEFORE the hand tree dies. Cloned
        // materials are ASSETS — Unity does not free them with the GameObject that referenced
        // them, so a style switch or rig rebuild under an open fan would leak one per renderer.
        HandGhosts.Shutdown();
        // Held-card MODE: forget which hand was holding rigidly. The hands about to be destroyed
        // are the ones the latch names, so leaving it set would let a rebuilt hand come back
        // already in the grip with no card in it — and the ghost, the curls and the wire bit all
        // read that latch.
        Cards.HeldCardGrip.Shutdown();
        // Scenery cloth: write the authored collider arrays back BEFORE the hands die. A curtain
        // left holding a destroyed collider never simulates correctly again, and a scene that is
        // merely being re-entered keeps its curtains.
        SceneClothHands.Shutdown();
        if (_handsRoot != null)
            Destroy(_handsRoot);
        _handsRoot = null;
        _left = null;
        _right = null;
        _simActive = false;
        VRLog.Info("Hands", "Hands torn down.");
        Core.TeardownReport.Note("hands (both hand trees, ghost materials restored, VRHands cleared)");
    }

    // ---- desktop simulation --------------------------------------------------------------

    private void AnimateSimulation()
    {
        if (_left == null || _right == null)
            return;

        Keyboard? keyboard = Keyboard.current;
        float targetTrigger = keyboard != null && keyboard.tKey.isPressed ? 1f : 0f;
        float targetGrip = keyboard != null && keyboard.gKey.isPressed ? 1f : 0f;
        _simTrigger = Mathf.MoveTowards(_simTrigger, targetTrigger, Time.deltaTime * 6f);
        _simGrip = Mathf.MoveTowards(_simGrip, targetGrip, Time.deltaTime * 6f);

        float t = Time.unscaledTime;
        float bob = Mathf.Sin(t * 1.2f) * 0.02f;
        float sway = Mathf.Sin(t * 0.7f) * 0.015f;

        // Camera-local offsets (meters, scaled by the sim root): hands rest in view.
        _left.SetSimulatedInput(
            new Vector3(-0.18f + sway, -0.22f + bob, 0.5f),
            Quaternion.Euler(-20f + Mathf.Sin(t * 0.9f) * 8f, 10f, 5f),
            _simTrigger, _simGrip);
        _right.SetSimulatedInput(
            new Vector3(0.18f - sway, -0.22f - bob, 0.5f),
            Quaternion.Euler(-20f - Mathf.Sin(t * 0.9f) * 8f, -10f, -5f),
            _simTrigger, _simGrip);
    }
}
