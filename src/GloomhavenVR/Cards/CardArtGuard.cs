using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHITE CARD FACES IN A DECISION PHASE (MP hardware test 2026-08-07,
/// <c>mp_handkartem_entscheidungsphase.png</c>): during "Wähle 1 Karte(n) zum Verlieren"
/// the raised hand fan renders frame / title / initiative correctly while both ACTION
/// HALVES are blank WHITE with only a couple of stray icons on them.
///
/// ROOT CAUSE — read from the game's own decompiled source, not inferred:
/// <list type="number">
/// <item>The action half's background is <c>FullAbilityCardAction.actionButton.image</c>.
///   Its sprite is NOT authored on the prefab: it is streamed by
///   <c>ButtonSpritesAddressableLoader.AddReferenceToSprites</c>, driven from
///   <c>FullAbilityCardAction.Show() → ApplyImage()</c>.</item>
/// <item><c>FullAbilityCard.ShowCard()</c> — the only caller of <c>Show()</c> — runs from
///   <c>FullAbilityCard.OnEnable()</c>. So EVERY disable→enable cycle of the face
///   GameObject re-enters the loader.</item>
/// <item><c>ButtonLoadingContext.LoadAsync</c> has exactly one idempotent fast path
///   (<c>_lastState == FinishedSuccessfully</c> and the same button/refs). Every OTHER
///   entry — i.e. every re-entry while the previous load is still IN FLIGHT — starts with
///   <c>Unload()</c>, and <c>ImageAddressableLoader.Unload(image)</c> executes
///   <c>image.sprite = null</c>. A uGUI Image with a null sprite draws Unity's built-in
///   WHITE texture. While any load is in flight <c>ImageAddressableLoader</c> additionally
///   alpha-0s its <c>_objectsToHideWhileLoad</c> CanvasGroups — which is why the action
///   CONTENT (text + most icons) disappears at the same time as the background turns
///   white, while the frame/title/initiative outside those groups keep rendering.</item>
/// <item>The disable→enable cycles are ours. In the PICK modes — <c>LoseCard</c>,
///   <c>DiscardCard</c>, <c>RecoverLostCard</c>, <c>RecoverDiscardedCard</c>,
///   <c>IncreaseCardLimit</c>, <c>CardsSelection</c>, <c>DeckSelection</c> —
///   <c>AbilityCardUI.UpdateView</c> calls <c>ToggleFullCard(active: false)</c>, which does
///   <c>fullAbilityCard.gameObject.SetActive(false)</c> on the very face we adopted;
///   <see cref="CardFace.Maintain"/> puts it back ACTIVE the same frame (it must — that is
///   the card the player is looking at). One hand refresh = one full loader restart. A
///   decision phase refreshes the hand constantly (the hand↔discard preview thrash the
///   take-damage row is already hardened against), so the loads are cancelled and
///   restarted faster than they can finish and the halves never get a sprite back.</item>
/// </list>
/// In <c>ActionSelection</c> the game calls <c>ToggleFullCard(active: true)</c> instead, so
/// there is no fight and no storm — which is exactly why only the decision-phase fan is white.
///
/// FIX, in two halves, both scoped strictly to faces the mod has ADOPTED (a card the game
/// still owns is never touched):
/// <list type="bullet">
/// <item>SUPPRESS (<see cref="SuppressShowCard"/>, driven by
///   <c>Patches/CardArtPatches.cs</c>): while a card-art load is in flight on an adopted
///   face, a fresh <c>ShowCard()</c> is skipped — restarting an in-flight load is precisely
///   the operation that nulls the sprite, and it can never converge under a storm. Bounded
///   by <see cref="InFlightGraceSeconds"/> so a genuinely stuck loader is never suppressed
///   forever.</item>
/// <item>REPLAY + HEAL (<see cref="Tick"/>, from <see cref="CardFace.Maintain"/> on a
///   <see cref="TickIntervalSeconds"/> cadence): every suppressed <c>ShowCard()</c> is
///   REPLAYED once the loads are quiet, so nothing is lost — a card whose skin changed
///   during the storm still reloads its new art. The same pass heals the already-white
///   state (an action button with a null sprite and no load in flight) by re-running
///   <c>ShowCard()</c> itself, so a fan that went white before this build's guard existed
///   still repairs instead of staying white until the widget is pooled.</item>
/// </list>
/// Everything is guarded: any surprise degrades to "the game's own behaviour", never to a
/// broken adoption. Nothing here changes what the game shows on the flat screen — the
/// suppression only ever skips a call that would have destroyed art the player is looking at.
/// </summary>
internal static class CardArtGuard
{
    /// <summary>Longest a load may be "in flight" before <see cref="SuppressShowCard"/> stops
    /// protecting it. A real addressable sprite load is milliseconds once warm; anything past
    /// this is a stuck/failed handle, and then the reload IS the repair.</summary>
    private const float InFlightGraceSeconds = 6f;

    /// <summary>Replay/heal cadence per adopted face (cheap component walks, not per frame).</summary>
    internal const float TickIntervalSeconds = 0.25f;

    /// <summary>Instance ids of the <c>FullAbilityCard</c>s currently adopted onto a VR card.</summary>
    private static readonly HashSet<int> s_adopted = new(16);

    /// <summary>Adopted faces whose <c>ShowCard()</c> we skipped and still owe a replay.</summary>
    private static readonly HashSet<int> s_deferred = new(8);

    /// <summary>
    /// Heal attempts already spent per face id. BOUNDED on purpose: a null background sprite is
    /// normally a transient the reload fixes, but the game is also free to leave an action half
    /// deliberately sprite-less, and an unbounded "sprite is null → ShowCard()" retry at the
    /// maintenance cadence would then be a loader storm of the mod's own making — exactly the
    /// failure this class exists to end. A replay of a SUPPRESSED call never spends budget (it is
    /// owed work, not a guess).
    /// </summary>
    private static readonly Dictionary<int, int> s_healsSpent = new(8);

    /// <summary>Heal attempts allowed per adoption (reset by <see cref="NoteAdopted"/>).</summary>
    private const int MaxHealsPerAdoption = 3;

    /// <summary>Face id → unscaled time its current in-flight window was first observed.</summary>
    private static readonly Dictionary<int, float> s_inFlightSince = new(8);

    /// <summary>Re-entrancy latch: the <c>ShowCard()</c> WE call must never be suppressed.</summary>
    private static bool s_replaying;

    private static bool s_suppressLogged;
    private static bool s_healLogged;
    private static bool s_errorLogged;

    /// <summary>Scratch for the loader walk (no steady-state allocation).</summary>
    private static readonly List<ImageAddressableLoader> LoaderScratch = new(4);

    /// <summary>Register a face as adopted (called from <see cref="CardFace.Adopt"/>).</summary>
    internal static void NoteAdopted(FullAbilityCard? card)
    {
        if (card == null)
            return;
        int id = card.GetInstanceID();
        s_adopted.Add(id);
        s_healsSpent.Remove(id); // a fresh adoption gets a fresh heal budget
    }

    /// <summary>Drop a face from the registry (Restore/Yield). Idempotent.</summary>
    internal static void NoteReleased(FullAbilityCard? card)
    {
        if (card == null)
            return;
        int id = card.GetInstanceID();
        s_adopted.Remove(id);
        s_deferred.Remove(id);
        s_inFlightSince.Remove(id);
        s_healsSpent.Remove(id);
    }

    /// <summary>
    /// Harmony seam (<c>Patches/CardArtPatches.cs</c>): true when this <c>ShowCard()</c> must be
    /// SKIPPED because it would restart — and therefore null out — card art that is still
    /// loading on a face the player is currently looking at. Only ever true for an ADOPTED face
    /// with a live in-flight load inside <see cref="InFlightGraceSeconds"/>; every skip is
    /// recorded for replay in <see cref="Tick"/>.
    /// </summary>
    internal static bool SuppressShowCard(FullAbilityCard? card)
    {
        if (s_replaying || card == null)
            return false;
        try
        {
            int id = card.GetInstanceID();
            if (!s_adopted.Contains(id))
                return false;
            if (!AnyLoadInFlight(card))
            {
                s_inFlightSince.Remove(id);
                return false;
            }
            float now = Time.unscaledTime;
            if (!s_inFlightSince.TryGetValue(id, out float since))
            {
                since = now;
                s_inFlightSince[id] = now;
            }
            if (now - since > InFlightGraceSeconds)
                return false; // stuck loader — let the reload through, it is the repair now

            s_deferred.Add(id);
            if (!s_suppressLogged)
            {
                s_suppressLogged = true;
                VRLog.Info("Cards", "CARD ART GUARD: skipped a FullAbilityCard.ShowCard() on an ADOPTED " +
                                    "face while its addressable card art was still loading. Re-entering " +
                                    "ButtonSpritesAddressableLoader mid-load runs Unload() first, which " +
                                    "sets Image.sprite = null (the WHITE action halves of the decision-" +
                                    "phase fan) and alpha-0s the loader's hide-while-loading groups. The " +
                                    "call is REPLAYED as soon as the loads are quiet, so nothing is lost.");
            }
            return true;
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("suppression check", ex);
            return false;
        }
    }

    /// <summary>
    /// Replay + heal for one adopted face, on the <see cref="TickIntervalSeconds"/> cadence from
    /// <see cref="CardFace.Maintain"/>. Runs <c>ShowCard()</c> when (a) we owe a replay for a
    /// suppressed call, or (b) an action half is sitting on a null sprite (the white state) —
    /// both only once the loader is quiet, so the repair cannot itself become the storm.
    /// </summary>
    internal static void Tick(FullAbilityCard? card)
    {
        if (card == null)
            return;
        try
        {
            int id = card.GetInstanceID();
            if (AnyLoadInFlight(card))
                return; // let it land — see SuppressShowCard
            s_inFlightSince.Remove(id);

            bool owed = s_deferred.Remove(id);
            bool white = ArtMissing(card);
            if (white)
            {
                s_healsSpent.TryGetValue(id, out int spent);
                if (spent >= MaxHealsPerAdoption)
                    white = false; // budget spent — the game means it; stop guessing (see s_healsSpent)
                else if (!owed)
                    s_healsSpent[id] = spent + 1;
            }
            if (!owed && !white)
                return;

            s_replaying = true;
            try
            {
                card.ShowCard();
            }
            finally
            {
                s_replaying = false;
            }

            if (white && !s_healLogged)
            {
                s_healLogged = true;
                VRLog.Info("Cards", "CARD ART GUARD: an adopted card face had an action half with a NULL " +
                                    "background sprite and no load in flight (the white-card state) — " +
                                    "re-ran FullAbilityCard.ShowCard() to restart the addressable load. " +
                                    "Self-healing, throttled to the face maintenance cadence.");
            }
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("art heal", ex);
        }
    }

    /// <summary>True while any <c>ImageAddressableLoader</c> under the face still holds an
    /// outstanding load (its public <c>ReferenceCount</c> — the very counter that alpha-0s the
    /// hide-while-loading groups).</summary>
    private static bool AnyLoadInFlight(FullAbilityCard card)
    {
        LoaderScratch.Clear();
        card.GetComponentsInChildren(includeInactive: true, LoaderScratch);
        for (int i = 0; i < LoaderScratch.Count; i++)
        {
            ImageAddressableLoader loader = LoaderScratch[i];
            if (loader != null && loader.ReferenceCount > 0)
                return true;
        }
        return false;
    }

    /// <summary>True when an ACTIVE action half has no background sprite — the exact pixel state
    /// the report shows (uGUI substitutes its built-in WHITE texture for a null sprite). The
    /// Image's own <c>enabled</c> flag is deliberately NOT part of the test: the loader disables
    /// the Image for the duration of a load and re-enables it after, so a cancelled load can leave
    /// it disabled AND sprite-less — equally broken, equally worth one bounded reload.</summary>
    private static bool ArtMissing(FullAbilityCard card) =>
        HalfMissing(card.topActionButton) || HalfMissing(card.bottomActionButton);

    private static bool HalfMissing(FullAbilityCardAction? half)
    {
        if (half == null || !half.gameObject.activeInHierarchy)
            return false;
        Button? button = half.actionButton;
        Image? image = button != null ? button.image : null;
        return image != null && image.sprite == null;
    }

    private static void LogErrorOnce(string what, System.Exception ex)
    {
        if (s_errorLogged)
            return;
        s_errorLogged = true;
        VRLog.Warn("Cards", $"CARD ART GUARD {what} failed ({ex.GetType().Name}: {ex.Message}) — " +
                            "card faces keep the game's own load behaviour.");
    }

    /// <summary>Drop all state (scene teardown / hot reload).</summary>
    internal static void Reset()
    {
        s_adopted.Clear();
        s_deferred.Clear();
        s_inFlightSince.Clear();
        s_healsSpent.Clear();
        s_replaying = false;
    }
}
