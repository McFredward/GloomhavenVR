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
    // ---- placement (world-static/pinnable like the combat log) --------------------------------

    /// <summary>
    /// Keep the panel diorama-scaled and, while not grabbed, placed. A fresh open (or the
    /// FOLLOW re-derive, or a first placement) routes through <see cref="PanelPlacement"/>
    /// so the pose is guaranteed inside the forward FOV; PINNED then freezes it in the world.
    /// </summary>
    private void Placement(Camera head)
    {
        if (_holder == null || _frame == null)
            return;

        float worldScale = PanelLayout.WorldScale;
        // Item 1/6: the panel SIZE targets the CONTROL BOARD width (SettingsPanelWidthMeters ≈
        // PlayTray.BoardW). Because _holder is a WORLD-space root while the board lives at the
        // diorama scale, the world-meter target must be multiplied by the diorama WorldScale or
        // the menu renders ~WorldScale× too small (item 1: "absolutely tiny"). To still satisfy
        // item 6 ("don't grow/shrink with table zoom"), WorldScale is SNAPSHOTTED at open/respawn
        // into _sizeScale and held while the panel stays open — later zoom no longer rescales it.
        // The frame's own localScale carries the user's SettingsScale on top; worldScale is also
        // used live for POSITION/distance below.
        if (_respawnRequested || _sizeScale <= 0f)
            _sizeScale = Mathf.Max(worldScale, 0.01f);
        _holder.localScale = Vector3.one *
            (SettingsPanelWidthMeters / (PanelWidthPx * CanvasMetersPerPixel)) * _sizeScale;

        if (_handle != null && _handle.IsGrabbed)
            return; // the grab core owns the pose while held

        if (_respawnRequested)
        {
            _respawnRequested = false;
            PlaceInView(head);
            _placedFromConfig = true;
            _facedPoseVersion = VRRigDriver.RigPoseVersion;
        }
        else if (WorldUIConfig.SettingsFollow.Value || !_placedFromConfig)
        {
            // REGRESSION FIX (menu rescaled with zoom / chased the head): this branch used to
            // re-derive the pose EVERY tick — position from the LIVE diorama scale, then
            // ClampIntoView against the CURRENT head. Two visible failures on hardware:
            // (a) a world-grab ZOOM changes WorldScale continuously, so `offset * worldScale`
            //     slid the panel nearer/farther every frame — reads as "the menu rescales with
            //     the zoom" (its world size is fixed, but the distance to the head is not);
            // (b) the HEAL RATCHET — ClampIntoView parks the pose exactly ON the ±35° cone
            //     edge and PersistLayout writes that back as the new offset, so the very next
            //     head motion is "out of view" again: the panel visibly dragged along with the
            //     head, and BepInEx saved the cfg file every frame (hardware log: hundreds of
            //     consecutive "healed back into the forward field of view" lines).
            // The panel is a WORLD-anchored object: derive the pose at EVENTS only — the first
            // placement after an open, and a rig rebuild/recenter (RigPoseVersion bump, the
            // seat FOLLOW re-derive) — and leave the world pose untouched in between. The grab
            // handle stays the only other pose writer (grab-move only).
            int poseVersion = VRRigDriver.RigPoseVersion;
            if (_placedFromConfig && poseVersion == _facedPoseVersion)
                return; // world-anchored between events: no per-tick zoom/head coupling

            if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
                return;
            Vector3 offset = new(
                WorldUIConfig.SettingsRight.Value,
                WorldUIConfig.SettingsUp.Value,
                WorldUIConfig.SettingsForward.Value);
            Vector3 candidate = anchor + yaw * (offset * worldScale);

            // Item 2: heal a stale/out-of-view persisted offset back into the forward FOV.
            bool healed = PanelPlacement.ClampIntoView(head, worldScale, ref candidate,
                out Quaternion facing);
            _frame.position = candidate;
            _frame.localScale = Vector3.one *
                Mathf.Clamp(WorldUIConfig.SettingsScale.Value, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

            // Orientation at events only (never per tick — the combat log's test #20 rule).
            // This whole block IS an event now, so the facing is (re)applied here.
            _frame.rotation = facing;
            _facedPoseVersion = poseVersion;
            if (healed)
            {
                PersistLayout();
                if (!_healLogged)
                {
                    _healLogged = true;
                    VRLog.Info("WorldUI", "Settings panel was out of view — healed back into " +
                                          "the forward field of view.");
                }
            }
            else
            {
                _healLogged = false;
            }
            _placedFromConfig = true;
        }
        else if (!WorldUIConfig.SettingsFollow.Value)
        {
            // PINNED + already placed: stay frozen, but heal the EXISTING world pose in if a
            // recenter/MR toggle (pose version bump) left it out of the new forward view (item 2).
            int poseVersion = VRRigDriver.RigPoseVersion;
            if (poseVersion != _facedPoseVersion)
            {
                _facedPoseVersion = poseVersion;
                Vector3 pos = _frame.position;
                if (PanelPlacement.ClampIntoView(head, worldScale, ref pos, out Quaternion facing))
                {
                    _frame.position = pos;
                    _frame.rotation = facing;
                    PersistLayout();
                    if (!_healLogged)
                    {
                        _healLogged = true;
                        VRLog.Info("WorldUI", "Settings panel (PINNED) was stranded out of view — " +
                                              "healed back into the forward field of view.");
                    }
                }
                else
                {
                    _healLogged = false;
                }
            }
        }
    }

    /// <summary>
    /// Fresh in-view spawn (item 1/3): a comfortable reading distance in front of the head,
    /// slightly below eye level, upright and facing the head, then persist it. Used on every
    /// open so the panel appears cleanly in front of the player, never overlapping the board
    /// or the combat log.
    /// </summary>
    private void PlaceInView(Camera head)
    {
        if (_frame == null)
            return;
        float worldScale = PanelLayout.WorldScale;
        PanelPlacement.Spawn(head, worldScale, out Vector3 pos, out Quaternion rot);
        _frame.position = pos;
        _frame.rotation = rot;
        _frame.localScale = Vector3.one *
            Mathf.Clamp(WorldUIConfig.SettingsScale.Value, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        _healLogged = false;
        PersistLayout();
    }

    // ---- IPanelGrabOwner ----------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;
    bool IPanelGrabOwner.GrabVisible =>
        _open && _holder != null && _holder.gameObject.activeInHierarchy;
    bool IPanelGrabOwner.GrabCarriesYaw => true; // world-static carry, yaws like the tray/combat log

    void IPanelGrabOwner.OnGrabFinished()
    {
        // Release snaps upright — zero roll/pitch, yaw toward the head at THIS moment — then frozen.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null && _frame != null)
        {
            Vector3 away = _frame.position - head.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-6f)
                _frame.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        PersistLayout();
    }

    // ---- FOLLOW/PINNED + laser wiring ---------------------------------------------------------

    private void TogglePin()
    {
        bool follow = !WorldUIConfig.SettingsFollow.Value;
        WorldUIConfig.SettingsFollow.Value = follow; // BepInEx persists on set
        if (!follow)
            PersistLayout(); // freeze: re-derives the pin from these offsets on the next open
        ApplyPinVisual();
        VRLog.Info("WorldUI", "Settings panel anchor mode → " +
                              $"{(follow ? "FOLLOW (seat-anchored)" : "PINNED (world-anchored)")}.");
    }

    private void ApplyPinVisual()
    {
        if (_pin == null)
            return;
        bool follow = WorldUIConfig.SettingsFollow.Value;
        _pin.SetState(true, accent: !follow);
        _pin.SetLabel(follow ? Loc.Mod("follow") : Loc.Mod("pinned"));
    }

    /// <summary>
    /// Laser support rides the tray's LaserTargets list while a tray exists and is visible
    /// (CardsDriver ray-tests it); the list dies with each tray, so re-register per tray
    /// INSTANCE. Poke needs none of this (the BoardButton self-registers).
    /// </summary>
    private void TickPin()
    {
        if (_pin == null)
            return;
        PlayTray? tray = PlayTray.Current;
        if (tray != null && !ReferenceEquals(tray, _laserTray) && _pin.Collider != null)
        {
            tray.RegisterLaserTarget(_pin.Collider, _pin);
            _laserTray = tray;
        }
    }

    /// <summary>
    /// Inverse of the FOLLOW placement: the frame pose as table-anchor offsets in real
    /// meters (seat-yaw space, divided by the diorama scale) + the size factor. BepInEx
    /// writes the ConfigFile on set, so the layout survives sessions. Silent (called on
    /// every open) — the open/anchor-mode logs already narrate placement.
    /// </summary>
    private void PersistLayout()
    {
        if (_frame == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        float worldScale = PanelLayout.WorldScale;
        if (worldScale < 1e-5f)
            return;
        Vector3 local = Quaternion.Inverse(yaw) * (_frame.position - anchor) / worldScale;
        WorldUIConfig.SettingsRight.Value = local.x;
        WorldUIConfig.SettingsUp.Value = local.y;
        WorldUIConfig.SettingsForward.Value = local.z;
        WorldUIConfig.SettingsScale.Value =
            Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
    }

    // ---- chord ---------------------------------------------------------------------------

    /// <summary>
    /// P6: fires on RELEASE via the shared <see cref="NonDominantHold"/> tracker —
    /// the same button carries the LONG-hold manual flat-screen chord (FlatScreen,
    /// fires at its threshold while held and marks the press Consumed). A short hold
    /// (≥ ChordHoldSeconds, released before the screen chord fired) toggles the
    /// settings panel; a consumed long hold does nothing extra here.
    /// </summary>
    private void TickChord()
    {
        float hold = WorldUIConfig.SettingsChordHoldSeconds.Value;
        if (hold <= 0f || !NonDominantHold.ReleasedThisFrame || NonDominantHold.Consumed)
            return;
        if (NonDominantHold.ReleasedAfterSeconds < hold)
            return;
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        Toggle();
    }

}
