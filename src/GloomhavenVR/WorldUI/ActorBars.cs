using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// True world-space actor HP/effect bars (ROADMAP P3c #4).
///
/// The game's "worldspace" bars are fake: one shared Screen-Space canvas whose
/// panels are re-projected every LateUpdate via <c>WorldToScreenPoint</c>
/// (UI-ARCH §3.3). In VR that plane is head-locked garbage. This class adopts every
/// <c>WorldspacePanelUIController</c> (per-actor panel holding HealthBar/EffectsBar/
/// ShieldBar/AttackModBar/InfoBar), moves it onto its own world-space host canvas
/// above the miniature, billboards it to the HMD and scales it with a distance clamp.
/// The DATA flow (UpdateHealth/UpdateEffects/ShowDamage/...) is untouched — the game
/// keeps feeding the very same components.
///
/// TRANSFORM OWNERSHIP (UI-ARCH §9.5 LateUpdate-fight): the game's per-frame writers
/// are prefix-skipped for ADOPTED panels only —
/// verified via ilspycmd (GH.Runtime.dll, WorldspaceDisplayPanelBase):
/// <code>
///   public void TrackCharacter()      // IL 318 B — WorldToScreenPoint projection
///   private void LateUpdate()         // calls TrackCharacter() + distance scaling
/// </code>
/// Both belong to <c>WorldspaceDisplayPanelBase</c>; the LateUpdate skip also stops
/// the screen-space scaling curve from fighting our world scale. Panels NOT adopted
/// (other subclasses, VR off) run 100% vanilla — the prefixes return true.
///
/// Occlusion ordering (WorldspaceUITools.OrderWorldspaceUIPanels) keeps running; it
/// only re-sorts sibling order, which is meaningless-but-harmless once each panel is
/// alone under its own host canvas (real depth testing orders world canvases).
/// </summary>
internal static class ActorBars
{
    private sealed class Adopted
    {
        public WorldspacePanelUIController Controller = null!;
        public ConvertedPanel Panel = null!;

        /// <summary>
        /// BOARD-SPACE (world-unit) anchor height above the TRACK point, cached at
        /// adopt (P6 fix #4): derived from the miniature's renderer bounds, so the
        /// bar clears the mini at EVERY diorama zoom — a real-meter offset (the old
        /// 0.045 × worldScale) shrank in board units when the player pinch-zoomed
        /// the table larger and sank the bars into the miniatures.
        /// </summary>
        public float AnchorOffsetWU;
    }

    private static readonly HashSet<WorldspaceDisplayPanelBase> Owned = new();
    private static readonly Dictionary<WorldspacePanelUIController, Adopted> Adoptions = new();
    private static readonly List<WorldspacePanelUIController> Scratch = new(32);
    private static readonly List<Renderer> RendererScratch = new(16);

    /// <summary>Patch gate: true when the game must NOT drive this panel's transform.</summary>
    internal static bool Owns(WorldspaceDisplayPanelBase panel) => Owned.Contains(panel);

    internal static void Tick()
    {
        bool want = WorldUIConfig.ActorBars.Value && WorldUIConfig.ConversionActive
                    && Choreographer.s_Choreographer != null;

        if (!want)
        {
            if (Adoptions.Count > 0)
                ReleaseAll();
            return;
        }

        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null)
            return;

        // Adopt new panels (list is the game's own registry; publicized private).
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController controller = controllers[i];
            if (controller == null || Adoptions.ContainsKey(controller))
                continue;
            Adopt(controller);
        }

        // Drop adoptions whose controller died (actor removed / scene unloading).
        if (Adoptions.Count > 0)
        {
            Scratch.Clear();
            foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
            {
                if (pair.Key == null || !pair.Value.Panel.IsAlive)
                    Scratch.Add(pair.Key!);
            }
            for (int i = 0; i < Scratch.Count; i++)
                Release(Scratch[i]);
        }
    }

    /// <summary>Placement pass — runs in the driver's LateUpdate, allocation-free.</summary>
    internal static void LateTick()
    {
        if (Adoptions.Count == 0)
            return;

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        Vector3 headPos = head.transform.position;
        float worldScale = PanelLayout.WorldScale;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
        {
            WorldspacePanelUIController controller = pair.Key;
            ConvertedPanel panel = pair.Value.Panel;
            if (controller == null || panel.HostGo == null)
                continue;

            if (!TryGetTrackPoint(controller, out Vector3 track))
                continue;

            // Anchor in BOARD units (P6 fix #4): the cached bounds-derived offset
            // scales with the diorama by construction — zooming the table keeps the
            // bar exactly above the miniature instead of inside it.
            Vector3 pos = track + Vector3.up * pair.Value.AnchorOffsetWU;

            // Billboard: uGUI front faces -forward → +Z away from the viewer.
            Vector3 fromHead = pos - headPos;
            if (fromHead.sqrMagnitude < 1e-6f)
                continue;
            Quaternion rot = Quaternion.LookRotation(fromHead.normalized, Vector3.up);

            // Distance clamp in HMD-RELATIVE REAL meters: world distance ÷ diorama
            // scale (rig lossyScale incl. the live pinch multiplier) — real size up
            // close, gently growing when far so bars stay readable across the table
            // (clamped ×2.5). The panel scale is world units = real meters × scale.
            float realDistance = fromHead.magnitude / worldScale;
            float grow = Mathf.Clamp(realDistance / 0.6f, 1f, 2.5f);

            Transform t = panel.HostGo.transform;
            t.SetPositionAndRotation(pos, rot);
            t.localScale = Vector3.one * (metersPerPixel * worldScale * 0.35f * grow);
        }
    }

    /// <summary>
    /// Track point: same selection the game's own <c>TrackCharacter</c> uses (head
    /// bone / base / base + head offset; decompiled WorldspaceDisplayPanelBase.cs:185
    /// — publicized private fields).
    /// </summary>
    private static bool TryGetTrackPoint(WorldspacePanelUIController controller, out Vector3 track)
    {
        Transform headBone = controller.m_HeadBonePoint;
        Transform basePoint = controller.m_BasePoint;
        if (controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.HeadBone && headBone != null)
        {
            track = headBone.position;
            return true;
        }
        if (basePoint != null)
        {
            track = controller.m_PointToTrackOnActor == WorldspaceDisplayPanelBase.PoinToTrack.Base
                ? basePoint.position
                : basePoint.position + controller.m_HeadBaseOffset;
            return true;
        }
        track = default;
        return false;
    }

    /// <summary>
    /// Board-space anchor height above the track point, from the miniature's renderer
    /// bounds (world-space AABB — already in board units). Vanilla equivalent: the game
    /// adds <c>m_WorldspaceOffsetY</c> (world units, from <c>Init(..., float height =
    /// 1.8f)</c>) before projecting to the screen (decompiled WorldspaceDisplayPanel
    /// Base.cs:186, WorldspacePanelUIController.cs:94); we anchor at the actual bounds
    /// top (tighter than the fixed 1.8) with the vanilla offset as the fallback.
    /// Called once per adoption — the GetComponentsInChildren allocation is a rare,
    /// per-actor-spawn event, not per-frame.
    /// </summary>
    private static float ComputeAnchorOffsetWU(WorldspacePanelUIController controller)
    {
        float fallback = Mathf.Max(controller.m_WorldspaceOffsetY, 0.2f);

        GameObject tracked = controller.m_ObjectToTrack;
        if (tracked == null || !TryGetTrackPoint(controller, out Vector3 track))
            return fallback;

        RendererScratch.Clear();
        tracked.GetComponentsInChildren(includeInactive: false, RendererScratch);
        if (RendererScratch.Count == 0)
            return fallback;

        float maxY = float.MinValue;
        float minY = float.MaxValue;
        for (int i = 0; i < RendererScratch.Count; i++)
        {
            Bounds b = RendererScratch[i].bounds;
            if (b.max.y > maxY) maxY = b.max.y;
            if (b.min.y < minY) minY = b.min.y;
        }
        RendererScratch.Clear();

        float height = maxY - minY;
        if (height <= 0.01f)
            return fallback; // degenerate bounds (still spawning) — vanilla height

        // Clear the top of the mini by ~12% of its own height, everything in board units.
        float offset = (maxY - track.y) + 0.12f * height;
        return Mathf.Clamp(offset, 0.05f, fallback + height);
    }

    private static void Adopt(WorldspacePanelUIController controller)
    {
        ConvertedPanel? panel = CanvasConversion.Convert(
            controller.transform as RectTransform, "ActorBar", pokeable: false);
        if (panel == null)
            return;

        Adoptions[controller] = new Adopted
        {
            Controller = controller,
            Panel = panel,
            AnchorOffsetWU = ComputeAnchorOffsetWU(controller),
        };
        Owned.Add(controller);
    }

    private static void Release(WorldspacePanelUIController controller)
    {
        // NOTE: the key may be Unity-dead ("== null" true) but the CLR reference is
        // still a valid dictionary key — always use it for the map ops.
        if (Adoptions.TryGetValue(controller, out Adopted adopted))
            CanvasConversion.Release(adopted.Panel);
        Adoptions.Remove(controller);
        Owned.Remove(controller);
    }

    internal static void ReleaseAll()
    {
        foreach (KeyValuePair<WorldspacePanelUIController, Adopted> pair in Adoptions)
            CanvasConversion.Release(pair.Value.Panel);
        Adoptions.Clear();
        Owned.Clear();
    }
}

/// <summary>
/// Prefix-skips the game's per-frame transform writers for ADOPTED actor bars only.
/// Vanilla behavior everywhere else (returns true). Signatures verified via ilspycmd
/// (GH.Runtime.dll): <c>public void TrackCharacter()</c> (IL 318 B, PATCH-TARGETS §1.7 ✅)
/// and <c>private void LateUpdate()</c> (Unity magic method — invoked directly by the
/// engine, never inlined).
/// </summary>
[HarmonyPatch(typeof(WorldspaceDisplayPanelBase))]
internal static class WorldspaceDisplayPanelBase_Patches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(WorldspaceDisplayPanelBase.TrackCharacter))]
    private static bool TrackCharacter_Prefix(WorldspaceDisplayPanelBase __instance)
        => !ActorBars.Owns(__instance);

    [HarmonyPrefix]
    [HarmonyPatch("LateUpdate")]
    private static bool LateUpdate_Prefix(WorldspaceDisplayPanelBase __instance)
        => !ActorBars.Owns(__instance);
}
