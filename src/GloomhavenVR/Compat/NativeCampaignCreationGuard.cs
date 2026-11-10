using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FFSNet;
using GloomhavenVR.Core;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using HarmonyLib;

namespace GloomhavenVR.Compat;

/// <summary>
/// Keeps a campaign creator's provisional native character stable when the player
/// backs out of personal-quest selection and confirms the same name again.
/// </summary>
/// <remarks>
/// Paired Build753 logs, 2026-11-03: CampaignCreateCharacter #4 generated the same
/// two quest rolls on both machines. At repeated #8, the observer's original
/// IsNameRepeated skipped creation because its party already contained that name;
/// the creator's character was still provisional, outside its party, so it rolled
/// again. Subsequent characters diverged too. AssignPersonalQuest then threw
/// "Sequence contains no matching element" in the original party coroutine.
///
/// Reuse the first actual native result, including its exact quest objects, gold
/// and rewards. Do not invent a quest, reset randomness, consume another roll,
/// suppress the game's error, or modify any network command. A vanilla observer
/// already skips this repeated name, so it stays consistent with a fixed creator.
/// An unmodified creator's own duplicate generation cannot be repaired here.
/// </remarks>
internal static class NativeCampaignCreationGuard
{
    private sealed class NameClass
    {
        internal readonly string ClassId;
        internal NameClass(string classId) => ClassId = classId;
    }

    // Weak context identities avoid keeping an abandoned save or Bolt player alive.
    // The table owns only original provisional characters, never serialized state.
    private static WeakReference? _map;
    private static WeakReference? _player;
    private static readonly Dictionary<string, CMapCharacter> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<UICharacterCreatorNameStep, NameClass> NameClasses = new();
    private static readonly PropertyInfo? Creating = typeof(PlayerRegistry).Assembly
        .GetType("FFSNet.NetworkPlayer")?.GetProperty("CreatingCharacter");
    private static readonly PropertyInfo? PlayerId = typeof(PlayerRegistry).Assembly
        .GetType("FFSNet.NetworkPlayer")?.GetProperty("PlayerID");
    // A name alone cannot identify the service caller: a concurrent peer may
    // submit that name too. Only this synchronous original proxy invocation
    // supplies its owner. Zero is outside a proxy; -1 is an invalid proxy owner.
    [ThreadStatic] private static int _commandOwner;

    internal readonly struct OriginState
    {
        internal readonly int Previous;
        internal readonly bool Entered;
        internal OriginState(int previous) { Previous = previous; Entered = true; }
    }
    private static int _reuses;
    private static int _conflicts;

    internal static void Reset()
    {
        Pending.Clear();
        _map = null;
        _player = null;
        _reuses = _conflicts = 0;
    }

    private static bool InCampaign()
    {
        if (!VRSession.IsRunning || !FFSNetwork.IsOnline
            || AdventureState.MapState == null || !AdventureState.MapState.IsCampaign)
        {
            Reset();
            return false;
        }

        object map = AdventureState.MapState;
        object? player = PlayerRegistry.MyPlayer;
        if (player == null || Creating == null || PlayerId == null)
        {
            Reset();
            return false;
        }
        if (!ReferenceEquals(_map?.Target, map) || !ReferenceEquals(_player?.Target, player))
        {
            Reset();
            _map = new WeakReference(map);
            _player = new WeakReference(player);
        }
        return true;
    }

    private static bool Provisional(CMapCharacter character) => character.PersonalQuest == null
        && character.PossiblePersonalQuests != null && character.PossiblePersonalQuests.Count != 0;

    // ProxyCreateCharacter sets this original owner record before calling the
    // service. Observers must never cache a peer's object: that peer may leave,
    // causing native cleanup to remove it while our own player/save stay alive.
    private static bool LocalCreation(string characterName)
    {
        // NetworkPlayer is Bolt-derived. Keep it object-typed, like the existing
        // NetPlayerActors bridge; CreatingCharacter itself is a plain game DTO.
        object? player = PlayerRegistry.MyPlayer;
        var creation = player == null ? null : Creating?.GetValue(player, null) as CreatingCharacter;
        return LocalCommand(player) && creation != null
            && string.Equals(creation.CharacterName, characterName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LocalCommand(object? player) => _commandOwner > 0 && player != null
        && PlayerId?.GetValue(player, null) is int id && id == _commandOwner;

    internal static bool TryReuse(string classId, string characterName, out CMapCharacter? character)
    {
        character = null;
        if (!InCampaign() || !LocalCreation(characterName) || string.IsNullOrEmpty(characterName)
            || !Pending.TryGetValue(characterName, out CMapCharacter? prior))
            return false;

        if (!Provisional(prior))
        {
            Pending.Remove(characterName);
            return false;
        }
        if (!string.Equals(prior.CharacterID, classId, StringComparison.Ordinal))
            return false;

        character = prior;
        _reuses++;
        if (_reuses <= 4 || (_reuses & (_reuses - 1)) == 0)
            VRLog.Warn("Compat", $"CAMPAIGN CREATION REUSED: repeated provisional character '{classId}' "
                + $"keeps its original personal-quest choices; no second native roll ({_reuses} this session).");
        return true;
    }

    internal static void Remember(string classId, string characterName, CMapCharacter? character)
    {
        if (!InCampaign() || !LocalCreation(characterName) || character == null || !Provisional(character)
            || string.IsNullOrEmpty(characterName)
            || !string.Equals(character.CharacterID, classId, StringComparison.Ordinal)
            || !string.Equals(character.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
            return;

        // Never substitute a different class for an already reserved name.
        if (!Pending.TryGetValue(characterName, out CMapCharacter? prior)
            || !Provisional(prior) || ReferenceEquals(prior, character))
            Pending[characterName] = character;
    }

    internal static void NameShown(UICharacterCreatorNameStep window, string classId, bool rename)
    {
        NameClasses.Remove(window);
        if (!rename && !string.IsNullOrEmpty(classId))
            NameClasses.Add(window, new NameClass(classId));
    }

    internal static bool ReservedForOtherClass(UICharacterCreatorNameStep window, string characterName)
    {
        if (!InCampaign() || string.IsNullOrEmpty(characterName)
            // Original observer validation must not inherit the local creator's
            // provisional reservation or currently selected name-step class.
            || (_commandOwner != 0 && !LocalCommand(PlayerRegistry.MyPlayer))
            || !NameClasses.TryGetValue(window, out NameClass? selected)
            || !Pending.TryGetValue(characterName, out CMapCharacter? prior)
            || !Provisional(prior) || string.Equals(prior.CharacterID, selected.ClassId, StringComparison.Ordinal))
            return false;

        _conflicts++;
        if (_conflicts <= 4 || (_conflicts & (_conflicts - 1)) == 0)
            VRLog.Warn("Compat", "CAMPAIGN CREATION NAME RESERVED: a provisional character of another "
                + "class already owns this name; retaining the native repeated-name warning.");
        return true;
    }

    [HarmonyPatch(typeof(UICharacterCreatorWindow), nameof(UICharacterCreatorWindow.ProxyCreateCharacter))]
    internal static class OriginSeam
    {
        [HarmonyPrefix]
        internal static void Prefix(GameAction action, out OriginState __state)
        {
            __state = new OriginState(_commandOwner);
            _commandOwner = action != null && action.SupplementaryDataIDMax > 0
                ? action.SupplementaryDataIDMax : -1;
        }

        // A void finalizer leaves every native exception untouched. The entered
        // bit also preserves an outer scope if an earlier prefix threw before
        // our nested prefix could execute (Harmony's default __state).
        [HarmonyFinalizer]
        internal static void Finalizer(OriginState __state)
        {
            // Read the copied value through an ordinary local: Harmony's analyzer
            // otherwise misclassifies readonly __state field reads as writes to
            // a non-ref patch parameter. The finalizer never changes __state.
            OriginState scope = __state;
            if (scope.Entered) _commandOwner = scope.Previous;
        }
    }

    [HarmonyPatch(typeof(CharacterCreatorService), nameof(CharacterCreatorService.CreateCharacter))]
    internal static class CreateSeam
    {
        [HarmonyPrefix]
        internal static bool Prefix(string classId, string characterName, ref CMapCharacter __result)
        {
            try
            {
                if (!TryReuse(classId, characterName, out CMapCharacter? character))
                    return true;
                __result = character!;
                return false;
            }
            catch (Exception e)
            {
                VRLog.Warn("Compat", $"CAMPAIGN CREATION GUARD: lookup unavailable; native creation retained. {e}");
                return true;
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(string classId, string characterName, CMapCharacter __result)
        {
            try { Remember(classId, characterName, __result); }
            catch (Exception e)
            {
                VRLog.Warn("Compat", $"CAMPAIGN CREATION GUARD: provisional lookup was not retained. {e}");
            }
        }
    }

    [HarmonyPatch(typeof(UICharacterCreatorNameStep), nameof(UICharacterCreatorNameStep.Show))]
    internal static class NameClassSeam
    {
        [HarmonyPrefix]
        internal static void Prefix(UICharacterCreatorNameStep __instance, ICharacterCreatorClass character,
            string currentName)
        {
            try { NameShown(__instance, character.ID, currentName != null); }
            catch (Exception e)
            {
                VRLog.Warn("Compat", $"CAMPAIGN CREATION GUARD: original name validation retained. {e}");
            }
        }
    }

    [HarmonyPatch(typeof(UICharacterCreatorNameStep), nameof(UICharacterCreatorNameStep.IsNameRepeated))]
    internal static class ReservedNameSeam
    {
        [HarmonyPostfix]
        internal static void Postfix(UICharacterCreatorNameStep __instance, string characterName, ref bool __result)
        {
            try
            {
                if (!__result)
                    __result = ReservedForOtherClass(__instance, characterName);
            }
            catch (Exception e)
            {
                VRLog.Warn("Compat", $"CAMPAIGN CREATION GUARD: original repeated-name result retained. {e}");
            }
        }
    }
}
