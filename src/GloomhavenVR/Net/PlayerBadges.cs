using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// "VR" badges in the game's multiplayer player overviews: a small tag appended to the username
/// of every KNOWN MODDED peer (and the local player) in the two assign/overview windows —
/// <c>UIWindowID.MutiplayerHeroAssignPanel</c> (= <c>UIMultiplayerEscSubmenu</c>, whose roster is
/// <c>UIMultiplayerMerchantRoster.slots</c> → <c>UIMultiplayerMerchantSlot.owner</c> →
/// <c>UIMultiplayerUser.username</c>, decompiled UIMultiplayerMerchantSlot.cs /
/// UIMultiplayerUser.cs:60-70) and <c>UIWindowID.MutiplayerPlayerPicker</c>
/// (= <c>UIMultiplayerSelectPlayerScreen</c>, whose ACTIVE rows live in the
/// <c>assignedSlots</c> Dictionary&lt;NetworkPlayer, UIMultiplayerPlayerOption&gt; — the
/// serialized <c>slots</c> list is a pool of UNUSED spares, decompiled
/// UIMultiplayerSelectPlayerScreen.cs:254-296). Both windows already float in VR via
/// ModalFallback; this decoration is purely local, works on any modded client, and flat
/// players simply get no tag (their flatness IS the absence of mod traffic).
///
/// REVERSIBLE BY CONSTRUCTION (never permanently mutate game UI): a per-tick POLL appends the
/// badge to the live TMP text while the window is open and the peer is known-modded, and strips
/// it the moment the row's player stops qualifying, the window closes, the session ends or the
/// mod shuts down. The game's own refresh (<c>SetupUserName</c>) rewriting the text just removes
/// the badge for one poll interval — the poll re-appends it; nothing is patched.
///
/// <c>FFSNet.NetworkPlayer</c> is Bolt-derived, so — same contract as
/// <see cref="NetPlayerActors"/> — it is NEVER referenced at compile time: the two
/// player-fields are read via reflection and resolved once.
/// </summary>
internal static class PlayerBadges
{
    /// <summary>Search marker (also what a poll checks to stay idempotent).</summary>
    private const string Marker = "[VR]";

    /// <summary>The full appended tag: a dimmed, slightly smaller "[VR]" behind the name —
    /// TMP rich text, exactly the tooling the game's own host tag uses in these rows.</summary>
    private const string Badge = " <color=#8FD8FF><size=75%>[VR]</size></color>";

    private const float PollInterval = 0.25f;

    private static float _nextPoll;
    private static readonly HashSet<TMP_Text> Decorated = new();
    private static readonly HashSet<TMP_Text> Touched = new();
    private static readonly List<TMP_Text> Scratch = new();

    // Reflection (resolved once; NetworkPlayer-typed fields only — see class doc).
    private static bool _reflected;
    private static FieldInfo? _userPlayer;      // UIMultiplayerUser.player       (NetworkPlayer)
    private static FieldInfo? _optionPlayer;    // UIMultiplayerPlayerOption.player (NetworkPlayer)
    private static FieldInfo? _pickerAssigned;  // UIMultiplayerSelectPlayerScreen.assignedSlots

    private static void EnsureReflection()
    {
        if (_reflected)
            return;
        _reflected = true;
        try
        {
            _userPlayer = AccessTools.Field(typeof(UIMultiplayerUser), "player");
            _optionPlayer = AccessTools.Field(typeof(UIMultiplayerPlayerOption), "player");
            _pickerAssigned = AccessTools.Field(typeof(UIMultiplayerSelectPlayerScreen), "assignedSlots");
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"PlayerBadges reflection resolution failed — badges disabled: {e.Message}");
        }
    }

    /// <summary>
    /// Poll-decorate while <paramref name="active"/> (online, sync running, guard not in
    /// flat-net mode); restore everything otherwise. Called from the driver's version phase —
    /// throttled to <see cref="PollInterval"/>, so the per-frame cost is a time compare.
    /// </summary>
    internal static void Tick(bool active, int localPlayerId)
    {
        if (Time.unscaledTime < _nextPoll)
            return;
        _nextPoll = Time.unscaledTime + PollInterval;

        if (!active)
        {
            RestoreAll();
            return;
        }

        EnsureReflection();
        Touched.Clear();

        try { DecorateHeroAssign(localPlayerId); }
        catch (Exception e) { VRLog.Warn("Net", $"PlayerBadges hero-assign poll failed: {e.Message}"); }
        try { DecoratePlayerPicker(localPlayerId); }
        catch (Exception e) { VRLog.Warn("Net", $"PlayerBadges player-picker poll failed: {e.Message}"); }

        // Anything decorated earlier but not seen this poll (row re-bound to another player,
        // window closed, peer gone) loses its badge — the restore half of the reversibility
        // contract.
        Scratch.Clear();
        foreach (TMP_Text label in Decorated)
        {
            if (!Touched.Contains(label))
                Scratch.Add(label);
        }
        for (int i = 0; i < Scratch.Count; i++)
        {
            Strip(Scratch[i]);
            Decorated.Remove(Scratch[i]);
        }
    }

    /// <summary>Strip every badge (session end, flat-net mode, shutdown).</summary>
    internal static void RestoreAll()
    {
        if (Decorated.Count == 0)
            return;
        foreach (TMP_Text label in Decorated)
            Strip(label);
        Decorated.Clear();
    }

    // ---- the two windows ----------------------------------------------------------------

    private static void DecorateHeroAssign(int localPlayerId)
    {
        if (_userPlayer == null || !Singleton<UIMultiplayerEscSubmenu>.IsInitialized)
            return;
        UIMultiplayerEscSubmenu submenu = Singleton<UIMultiplayerEscSubmenu>.Instance;
        if (submenu == null || submenu.Window == null || !submenu.Window.IsOpen)
            return;

        UIMultiplayerMerchantRoster roster = submenu.heroSlotsController;
        if (roster == null || roster.slots == null)
            return;
        for (int i = 0; i < roster.slots.Count; i++)
        {
            UIMultiplayerMerchantSlot slot = roster.slots[i];
            if (slot == null)
                continue;
            DecorateUser(slot.owner, localPlayerId);
            // Each row has a mirrored host/client twin the setters keep in sync — badge it too,
            // whichever variant is currently the visible one.
            if (slot.twinSlot != null)
                DecorateUser(slot.twinSlot.owner, localPlayerId);
        }
    }

    private static void DecorateUser(UIMultiplayerUser? user, int localPlayerId)
    {
        if (user == null || !user.gameObject.activeInHierarchy)
            return;
        int pid = NetPlayerActors.PlayerIdOf(_userPlayer!.GetValue(user));
        Apply(user.username, IsVr(pid, localPlayerId));
    }

    private static void DecoratePlayerPicker(int localPlayerId)
    {
        if (_optionPlayer == null || _pickerAssigned == null
            || !Singleton<UIMultiplayerSelectPlayerScreen>.IsInitialized)
            return;
        UIMultiplayerSelectPlayerScreen picker = Singleton<UIMultiplayerSelectPlayerScreen>.Instance;
        if (picker == null || !picker.IsOpen)
            return;

        if (_pickerAssigned.GetValue(picker) is not IDictionary assigned)
            return;
        foreach (DictionaryEntry entry in assigned)
        {
            if (entry.Value is not UIMultiplayerPlayerOption option || option == null)
                continue;
            int pid = NetPlayerActors.PlayerIdOf(entry.Key);
            Apply(option.userName, IsVr(pid, localPlayerId));
        }
    }

    /// <summary>VR = the local player (we run the mod, by definition) or any peer we saw a
    /// valid mod packet from this session. Everyone else is implicitly flat — no tag.</summary>
    private static bool IsVr(int playerId, int localPlayerId) =>
        playerId > 0 && (playerId == localPlayerId || VersionGuard.IsModdedPeer(playerId));

    // ---- badge text surgery ---------------------------------------------------------------

    private static void Apply(TMP_Text? label, bool vr)
    {
        if (label == null)
            return;
        string text = label.text ?? string.Empty;
        if (vr)
        {
            if (!text.Contains(Marker) && text.Length > 0)
                label.text = text + Badge;
            Decorated.Add(label);
            Touched.Add(label);
        }
        else if (Decorated.Contains(label))
        {
            Strip(label);
            Decorated.Remove(label);
        }
    }

    private static void Strip(TMP_Text label)
    {
        if (label == null) // Unity-null: the window was destroyed with the badge still on
            return;
        string text = label.text ?? string.Empty;
        if (text.Contains(Badge))
            label.text = text.Replace(Badge, string.Empty);
    }
}
