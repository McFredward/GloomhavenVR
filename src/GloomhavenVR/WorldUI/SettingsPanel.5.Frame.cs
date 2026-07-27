using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class SettingsPanel : IPanelGrabOwner
{
    // ---- grab frame (bar + FOLLOW/PINNED pin) -------------------------------------------------

    /// <summary>
    /// Build the mod-owned frame: holder (diorama scale) → frame (grab root) → brass grab
    /// bar + FOLLOW/PINNED pin. Mirrors <c>CombatLogSurface.EnsureFrame</c> so the panel
    /// moves/scales/persists through the shared <see cref="PanelGrabHandle"/> exactly like
    /// the control board and the combat log.
    /// </summary>
    private void BuildFrame()
    {
        var holderGo = new GameObject("GloomhavenVR.SettingsPanel");
        _holder = holderGo.transform;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        float panelWidth = PanelWidthPx * CanvasMetersPerPixel;
        float barWidth = panelWidth * BarWidthFraction;

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        UnityEngine.Object.Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(_frame, worldPositionStays: false);
        bar.transform.localScale = new Vector3(barWidth, BarThickness, BarThickness);
        bar.GetComponent<MeshRenderer>().sharedMaterial =
            WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f)); // brass — same "grab me" as the tray
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.size = new Vector3(panelWidth * ZoneWidthFraction, 0.05f, 0.05f);
        _grabZone.isTrigger = true;
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, bar.GetComponent<MeshRenderer>(), "WorldUI", "Settings");

        // FOLLOW/PINNED pin, right of the bar (the tray's toggle, same look & feel).
        _pinAnchor = new GameObject("PinToggle").transform;
        _pinAnchor.SetParent(_frame, worldPositionStays: false);
        _pinAnchor.localPosition = new Vector3(barWidth * 0.5f + 0.05f, 0f, -0.002f);
        _pin = PlayTray.BoardButton.Create(_pinAnchor, new Vector2(0.068f, 0.030f),
            new Color(0.75f, 0.55f, 0.2f), Loc.Mod("follow"), TogglePin);
        ApplyPinVisual();

        VRLog.Info("WorldUI", "Settings panel frame built (grab bar + FOLLOW/PINNED pin).");
    }

    /// <summary>Cycle-button readout for the head-mask picker: "Mask 1".."Mask 3" (id 0..2 + 1).</summary>
    private static string MaskLabel()
    {
        int id = Net.NetModule.MaskId != null ? Net.NetModule.MaskId.Value : 0;
        id = Mathf.Clamp(id, 0, Net.HeadMaskLibrary.MaskCount - 1);
        return $"{Loc.Mod("mask")} {id + 1}";
    }

    /// <summary>Advance the local head mask 0→1→2→0 (writes [Net] MaskId; BepInEx persists on set).</summary>
    private static void CycleMask()
    {
        if (Net.NetModule.MaskId == null)
            return;
        int cur = Mathf.Clamp(Net.NetModule.MaskId.Value, 0, Net.HeadMaskLibrary.MaskCount - 1);
        Net.NetModule.MaskId.Value = (cur + 1) % Net.HeadMaskLibrary.MaskCount;
    }

    /// <summary>Stepper readout for the head-mask size ("1.00x"), matching the other size
    /// steppers' format. Falls back to the authored size when the [Net] config is unbound.</summary>
    private static string MaskSizeLabel()
    {
        float v = Net.NetModule.MaskSize != null ? Net.NetModule.MaskSize.Value : 1f;
        return $"{v:0.00}x";
    }

    /// <summary>Nudge the head-mask size by 0.05 (writes [Net] MaskSize; BepInEx persists on set).
    /// Clamped to the entry's own AcceptableValueRange, which is exactly the wire's quantization
    /// window — so what the panel allows is always transmittable to the letter.</summary>
    private static void StepMaskSize(int direction)
    {
        ConfigEntry<float>? e = Net.NetModule.MaskSize;
        if (e == null)
            return;
        e.Value = Mathf.Clamp(e.Value + direction * 0.05f,
            Net.NetProtocol.MaskSizeMin, Net.NetProtocol.MaskSizeMax);
    }

    /// <summary>Cycle-button readout for the hand-style picker (enum name, like the
    /// control-board selector's Oak/Steel/Bronze).</summary>
    private static string HandStyleLabel()
    {
        try
        {
            return Plugin.HandStyle != null ? Plugin.HandStyle.Value.ToString() : Hands.HandStyle.Glove.ToString();
        }
        catch
        {
            return Hands.HandStyle.Glove.ToString();
        }
    }

    /// <summary>Advance the hand style Glove→Plate→Arcane→Glove (writes [Hands] HandStyle;
    /// BepInEx persists on set and HandsDriver live-rebuilds via SettingChanged).</summary>
    private static void CycleHandStyle()
    {
        if (Plugin.HandStyle == null)
            return;
        int cur = (int)Hands.HandStyles.Clamp((int)Plugin.HandStyle.Value);
        Plugin.HandStyle.Value = (Hands.HandStyle)((cur + 1) % Hands.HandStyles.Count);
    }

    /// <summary>
    /// Cycle-button readout for the USER-FACING control-board picker: the LOCALIZED board name
    /// ("Eiche" / "Stahl" / "Bronze"), not the raw enum member. Guarded like the hand-style readout
    /// so an unbound [Cards] config (panel opened before the Cards module bound) shows the default
    /// board instead of throwing inside a UI refresher.
    /// </summary>
    private static string ControlBoardLabel()
    {
        try
        {
            return ControlBoards.DisplayName(
                CardsConfig.Board != null ? CardsConfig.Board.Value : ControlBoard.Oak);
        }
        catch
        {
            return ControlBoards.DisplayName(ControlBoard.Oak);
        }
    }

    /// <summary>
    /// Advance the control board Eiche→Stahl→Bronze→Eiche (writes [Cards] Board; BepInEx persists
    /// on set and CardsDriver live-rebuilds the tray via SettingChanged, keeping the old world
    /// pose). Mirrors <see cref="CycleHandStyle"/> one-for-one — same shape, same guard, same
    /// single source for the cycle length (<see cref="ControlBoards.Next"/>).
    ///
    /// The VRLog line is the proof-of-apply a hardware log needs: it names the board the USER just
    /// picked, and the rebuild it triggers logs its own "[Cards] Control board switched to …" plus
    /// the factory's "Control board '…' → '…' loaded from bundle." — so the whole chain
    /// (menu press → config write → prefab load) is greppable end to end.
    /// </summary>
    private static void CycleControlBoard()
    {
        if (CardsConfig.Board == null)
            return;
        ControlBoard next = ControlBoards.Next(ControlBoards.Clamp((int)CardsConfig.Board.Value));
        CardsConfig.Board.Value = next;
        VRLog.Info("WorldUI", $"Control board SELECTED by the user: '{next}' " +
                              $"(\"{ControlBoards.DisplayName(next)}\") — VR settings → " +
                              "Avatar → Kontrollbrett, the same user-facing surface as the head " +
                              "mask and the hand style. Written to [Cards] Board (persisted); the " +
                              "tray rebuilds live at its current world pose.");
    }

    /// <summary>Cycle-button readout for the remote-boards visibility setting.</summary>
    private static string RemoteBoardsLabel()
    {
        var v = Net.NetModule.RemoteBoards != null ? Net.NetModule.RemoteBoards.Value : Net.RemoteBoardVisibility.ActionPhaseOnly;
        return v switch
        {
            Net.RemoteBoardVisibility.Off => Loc.Mod("remote_boards_off"),
            Net.RemoteBoardVisibility.Always => Loc.Mod("remote_boards_always"),
            _ => Loc.Mod("remote_boards_action"),
        };
    }

    /// <summary>
    /// Advance remote-board visibility Off→ActionPhaseOnly→Always→Off (writes [Net] RemoteBoards;
    /// BepInEx persists on set). The write-side log exists because the READ-side confirmation
    /// (<see cref="Net.RemoteBoardGate.LogModeIfChanged"/>) only fires while a peer's board is
    /// actually ticking — in single player the button would otherwise leave no trace at all, which
    /// is exactly what made this control look dead in the last hardware logs. Grep:
    /// "Remote board visibility".
    /// </summary>
    private static void CycleRemoteBoards()
    {
        if (Net.NetModule.RemoteBoards == null)
            return;
        int cur = (int)Net.NetModule.RemoteBoards.Value;
        var next = (Net.RemoteBoardVisibility)((cur + 1) % 3);
        Net.NetModule.RemoteBoards.Value = next;
        VRLog.Info("WorldUI", $"Remote board visibility SET from the VR settings panel: " +
                              $"[Net] RemoteBoards = {next}. It governs the peer board frame, every " +
                              "parity widget, the inert furniture, the board-anchored item and " +
                              "pile-browse fans and the board-bound card flights; a peer's hands, head " +
                              "and hand-HELD fans are avatar content and are never hidden by it.");
    }

    private static bool BoardConfigSafe(Func<bool> read)
    {
        try
        {
            return Board.BoardConfig.ForceFarMode != null && read();
        }
        catch
        {
            return false;
        }
    }

    private static float CurrentScaleMultiplier()
    {
        Transform? rig = RigTarget.Current;
        float baseScale = RigTarget.BaseScale;
        if (rig == null || baseScale <= 0f)
            return ComfortSettings.IsBound ? ComfortSettings.SavedScaleMultiplier.Value : 1f;
        return rig.localScale.x / baseScale;
    }

    private void RefreshAll()
    {
        for (int i = 0; i < _refreshers.Count; i++)
            _refreshers[i]();
    }

}
