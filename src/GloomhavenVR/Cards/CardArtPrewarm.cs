using System.Collections.Generic;
using System.Diagnostics;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// GREY CARDS ON THE FIRST FAN OPEN — the warm-up that moves the card-art load off the frame the
/// player is looking at.
///
/// <para>THE REPORT (user, verbatim, 2026-08-24, ModBuild 242): "Wenn man von einem Character das
/// erste Mal die Kartenhand (den Fächer) öffnet, sieht man wie die erste kurze Zeit die Karten noch
/// grau sind und dann nachladen. Kannst du sie bitte im Level so vorladen, dass der Spieler dieses
/// Nachladen nicht sieht?"</para>
///
/// <para>THE MECHANISM, every step read out of the shipped game code rather than inferred:</para>
/// <list type="number">
/// <item><description>A card's whole printed surface is streamed, not authored. The card BACKGROUND
/// is <c>FullAbilityCard.headerImage</c>, fed from <c>_skin.TitleSprite.SpriteReference</c>, and the
/// two action halves' backgrounds come from <c>_referenceForImageActionButton</c>
/// (decompiled/GH.Runtime/FullAbilityCard.cs:438-446, FullAbilityCardAction.cs:529-540). On this
/// party that is <c>AC_Berserker_Background</c> 1254x1916 plus <c>AC_Berserker_Top</c> /
/// <c>AC_Berserker_Bottom</c> 1085x651 — the hardware log names all three at
/// <c>.planning/debug/LogOutput.log:3520</c>.</description></item>
/// <item><description>While such a load is running the art is not merely low-res, it is ABSENT:
/// <c>ImageLoadingContext.LoadAsync</c> sets <c>image.enabled = false</c> before the await and
/// <c>= true</c> after it (ImageLoadingContext.cs:32/37), and <c>ImageAddressableLoader</c>
/// alpha-0s every <c>_objectsToHideWhileLoad</c> CanvasGroup for as long as its
/// <c>ReferenceCount</c> is non-zero (ImageAddressableLoader.cs:63-75). Frame, title and initiative
/// still draw. That is the grey card.</description></item>
/// <item><description>The loads are started by <c>FullAbilityCard.OnEnable → ShowCard()</c>
/// (FullAbilityCard.cs:430-433) and by nothing else — so they start the first frame the face
/// GameObject is ACTIVE IN THE HIERARCHY.</description></item>
/// <item><description>And that frame is exactly the frame the fan opens. During the card-selection
/// phase the game keeps every hand card's full face DEACTIVATED:
/// <c>AbilityCardUI.SetMode</c>'s <c>CardsSelection</c> arm runs <c>ToggleFullCard(active: false)</c>
/// → <c>fullAbilityCard.gameObject.SetActive(false)</c> (AbilityCardUI.cs:921-930 and :993-1010).
/// The mod adopts that already-inactive face (<see cref="CardFace.Adopt"/>) onto a VRCard parented
/// under <c>VRCardFactory.PoolRoot</c>, which is itself <c>SetActive(false)</c>
/// (VRCardFactory.cs:52-53), and a closed fan disables its own root as well (CardFan.Close). So the
/// face stays inactive, <c>OnEnable</c> never fires, and NOT ONE BYTE of card art has been asked
/// for. Then <c>CardFan.Open</c> does <c>_root.gameObject.SetActive(true)</c> (CardFan.cs:134) and
/// all N faces wake in a single frame: N x <c>ShowCard()</c>, 3N cold addressable sprite loads,
/// started in the frame the player is watching the fan bloom.</description></item>
/// <item><description>WHY ONLY "das erste Mal": <c>ImageLoadingContext.LoadAsync</c> has one
/// idempotent fast path — same context, same Image, same runtime key, and
/// <c>_lastState == FinishedSuccessfully</c> — which assigns the cached sprite and re-enables the
/// Image with NO await at all (ImageLoadingContext.cs:24-29); <c>ButtonLoadingContext.LoadAsync</c>
/// carries the twin (ButtonLoadingContext.cs:41-45). Once a widget has completed its loads every
/// later OnEnable is synchronous. The defect is precisely the FIRST enable of each widget, which is
/// what the user described.</description></item>
/// </list>
///
/// <para>WHAT THIS DOES. One call: <c>FullAbilityCard.ShowCard()</c> on the adopted face WHILE IT IS
/// STILL PARKED — issued from <see cref="CardFace.Adopt"/> via <see cref="NoteAdopted"/>, i.e. at
/// hand-bind time, which in a scenario is the start of the card-selection phase and seconds before a
/// palm can roll up. <c>ShowCard</c> does not require an active GameObject; it only starts the loads.
/// By the time <c>CardFan.Open</c> wakes the face, its <c>ImageLoadingContext</c> is already
/// <c>FinishedSuccessfully</c> and the OnEnable takes the fast path above — the fan's FIRST rendered
/// frame already carries the art. This is the literal reading of "im Level vorladen": the same
/// sprites, through the game's own loader, on the game's own lifecycle, just earlier.</para>
///
/// <para>COST. ZERO extra megabytes: every sprite this touches is one the fan would have loaded a
/// moment later anyway, held by the same widget's handle and released by the same
/// <c>UnloadAll</c>. Nothing is pinned, nothing is cached here, and there is no second copy. The
/// main-thread cost is one <c>ShowCard()</c> per card — the call itself only queues addressable
/// requests — and it is spread at <see cref="MaxWarmsPerFrame"/> per frame, so a ten-card hand is
/// warmed across five frames instead of one. A hitch moved onto the level-load frame would still be
/// a hitch; this one is not on any single frame.</para>
///
/// <para>NOT A POLL, NOT A SWEEP. <see cref="NoteAdopted"/> is an EDGE — one enqueue per
/// <see cref="CardFace.Adopt"/>, i.e. once per card per adoption — and <see cref="Pump"/> returns on
/// a queue-empty test when there is nothing to do. There is no <c>FindObjectsOfType</c>, no scene
/// walk and no per-frame scan of anything. A face that is already warm is skipped by the same
/// readiness test the falsifier uses, so re-enqueueing one costs a component check and nothing
/// else.</para>
///
/// <para>REJECTED ALTERNATIVES, and why each one is worse:</para>
/// <list type="number">
/// <item><description>WARM AT SCENARIO LOAD FOR EVERY CHARACTER IN THE PARTY. It is the most literal
/// reading of "im Level", and it is work done for characters the player may never open — and worse,
/// it cannot use the seam above at all: at scenario load the other characters' <c>AbilityCardUI</c>
/// widgets do not exist yet (the game builds one hand at a time), so there is nothing to call
/// <c>ShowCard</c> on. It would have to become a class-level Addressables pin instead — see (5).
/// The seam this class uses fires at hand-bind, which in a scenario is the start of the
/// card-selection phase, i.e. inside the level and seconds before any palm can roll up. The
/// character-SWITCH case gets the same treatment for free: a switch re-binds the hand, which
/// re-adopts, which re-enqueues.</description></item>
/// <item><description>WARM ON CHARACTER SELECT. Same window, less coverage: it would need a focus
/// edge from another lane (<c>Board.CharacterFocus</c> raises no event — it PUSHES
/// <c>CardsDriver.RequestRebuild()</c>), and that rebuild is exactly what calls
/// <see cref="CardFace.Adopt"/>. Subscribing to the focus would be subscribing to a strictly later
/// copy of the same edge.</description></item>
/// <item><description>HIDE THE FAN UNTIL THE ART IS READY. This changes WHEN he sees the fan
/// instead of removing the wait, and he asked for the opposite ("dass der Spieler dieses Nachladen
/// nicht sieht", not "dass der Fächer später kommt"). It would also fight the palm gate, whose
/// whole contract is that the fan follows the hand.</description></item>
/// <item><description>KEEP THE WARMED CARDS ALIVE INSTEAD OF RETURNING THEM TO THE POOL. It solves
/// a problem that does not exist: the second open is ALREADY instant, and for a reason that is in
/// the game's own source rather than in a cache of ours — the <c>_lastState ==
/// FinishedSuccessfully</c> fast path in point 5 above. The defect is a COLD WIDGET, not a cold
/// cache, so pinning objects would cost memory and change nothing.</description></item>
/// <item><description>PIN THE CLASS'S SPRITES WITH OUR OWN <c>Addressables.LoadAssetAsync</c>
/// HANDLES. It would cover the one case this does not (a widget whose loader has never awoken — see
/// <c>DrainWarmQueue</c>) and it would also warm the map-room and peer SNAPSHOT paths, which build
/// fresh clones with fresh loader contexts. It is not shipped because it costs real megabytes that
/// nothing here can measure without hardware: on this party the three keys are
/// <c>AC_*_Background</c> 1254x1916 plus two 1085x651 halves, i.e. roughly 4 MB per class
/// compressed and four times that uncompressed, times the party. The falsifier below counts exactly
/// the case that would justify it; if that count comes back non-zero from hardware, this is the
/// follow-up.</description></item>
/// </list>
///
/// <para>MULTIPLAYER. Purely local and purely presentational. No wire field, no channel, no card
/// identity leaves this client: the only thing that happens is that a widget this client already
/// owns asks the game's own loader for art it was going to ask for anyway. A PEER's fan
/// (<c>Net.RemoteHandFan</c>) and the map-room fan both print from an <c>Object.Instantiate</c>
/// SNAPSHOT of a pooled widget, so their clones carry fresh loader contexts and cannot use the fast
/// path above — they have the same grey moment, and it is NOT fixed here (see the report). What they
/// do get for free is a warm Addressables cache: this warm-up loads the class's sprites, the game
/// holds those handles for as long as the widget lives, and a clone made afterwards resolves the
/// same keys against an already-resident asset.</para>
/// </summary>
internal static class CardArtPrewarm
{
    private const string Scope = "Cards";

    /// <summary>
    /// Faces warmed per frame. The work per face is "start three addressable requests", not "decode
    /// three textures" — the decode lands on Addressables' own callbacks over the following frames
    /// either way — but issuing ten at once puts ten completions on one later frame, and a hitch
    /// moved onto the level-load frame is still a hitch. Two per frame drains a ten-card hand in
    /// five frames (~56 ms at 90 Hz) against the seconds the phase actually gives us.
    /// </summary>
    private const int MaxWarmsPerFrame = 2;

    /// <summary>Longest a measurement window may stay open before it is reported as NOT ACHIEVED.
    /// A warm addressable sprite load is milliseconds; anything past this is a stuck or failed
    /// handle, and the honest thing is to say so rather than to keep waiting.</summary>
    private const float ReadyTimeoutSeconds = 10f;

    /// <summary>Falsifier lines emitted before the PASSING ones go quiet. See
    /// <see cref="Report"/> — the line that spends the last one says so, so a reader is never left
    /// wondering whether the instrument stopped or the fan did.</summary>
    private const int PassLineBudget = 12;

    /// <summary>Faces waiting for their one early <c>ShowCard()</c>. Never grows past the hand
    /// size: <see cref="NoteAdopted"/> refuses a face that is already in it.</summary>
    private static readonly List<FullAbilityCard> Pending = new(16);

    /// <summary>Scratch for the loader walk — no steady-state allocation.</summary>
    private static readonly List<ImageAddressableLoader> LoaderScratch = new(4);

    /// <summary>The faces of the fan whose open is currently being measured (empty = no window).</summary>
    private static readonly List<FullAbilityCard> Watched = new(16);

    private static long s_windowStart;
    private static int s_windowStartFrame;
    private static string s_windowLabel = "?";
    private static int s_windowCards;
    private static int s_windowNotReady;
    private static bool s_windowOpen;

    private static int s_warmed;
    private static int s_warmSkippedActive;
    private static int s_warmSkippedInFlight;
    private static int s_warmSkippedReady;
    private static int s_warmSkippedNoLoader;
    private static int s_warmFailed;
    private static int s_opens;
    private static int s_linesSpent;
    private static bool s_budgetNoted;
    private static bool s_disabled;
    private static bool s_disableLogged;

    // ------------------------------------------------------------------- the edge --

    /// <summary>
    /// A live game face was just adopted onto a VR card (<see cref="CardFace.Adopt"/>). Queue its
    /// one early <c>ShowCard()</c>. Idempotent: a face already queued, already active (the game is
    /// driving its loads itself) or already carrying its art is not queued again.
    /// </summary>
    internal static void NoteAdopted(FullAbilityCard? face)
    {
        if (s_disabled || face == null)
            return;
        try
        {
            if (face.gameObject.activeInHierarchy)
            {
                s_warmSkippedActive++;
                return; // the game's own OnEnable already started (or finished) these loads
            }
            if (AnyLoadInFlight(face))
            {
                s_warmSkippedInFlight++;
                return; // already warming — a second ShowCard now is exactly the restart
                        // CardArtGuard exists to suppress (it nulls the sprite mid-load)
            }
            if (FaceReady(face))
            {
                s_warmSkippedReady++;
                return; // a pooled widget that has been through this before — nothing to load
            }
            for (int i = 0; i < Pending.Count; i++)
            {
                if (ReferenceEquals(Pending[i], face))
                    return;
            }
            Pending.Add(face);
        }
        catch (System.Exception ex)
        {
            Disable("queueing an adopted face", ex);
        }
    }

    /// <summary>
    /// The hand fan just opened (<c>CardFan.Open</c>). Start — or immediately close — the
    /// measurement window that says whether the warm-up did its job for this character.
    /// </summary>
    internal static void NoteFanOpened(IReadOnlyList<VRCard>? cards)
    {
        if (s_disabled || cards == null || cards.Count == 0)
            return;
        try
        {
            // A WINDOW THAT IS STILL OPEN IS REPORTED, NEVER DROPPED. The palm gate can close and
            // re-open the fan while art is still landing, and silently overwriting the previous
            // window would delete exactly the measurement that was failing — the one case this
            // instrument exists to catch. Close it out with what it knows now and say it was cut
            // short. A window that PASSED never reaches here: the pass path closes it at open.
            if (s_windowOpen)
                CloseWindow(cutShort: true);
            s_opens++;
            Watched.Clear();
            string label = "?";
            for (int i = 0; i < cards.Count; i++)
            {
                VRCard card = cards[i];
                AbilityCardUI? widget = card != null ? card.GameCard : null;
                FullAbilityCard? face = widget != null ? widget.fullAbilityCard : null;
                if (face == null)
                    continue;
                if (label == "?")
                    label = OwnerLabel(face);
                Watched.Add(face);
            }
            if (Watched.Count == 0)
                return;

            int notReady = 0;
            for (int i = 0; i < Watched.Count; i++)
            {
                if (!FaceReady(Watched[i]))
                    notReady++;
            }

            s_windowStart = Stopwatch.GetTimestamp();
            s_windowStartFrame = Time.frameCount;
            s_windowLabel = label;
            s_windowCards = Watched.Count;
            s_windowNotReady = notReady;

            if (notReady == 0)
            {
                s_windowOpen = false;
                Watched.Clear();
                Report(achieved: true, waitedMs: 0.0, waitedFrames: 0, stillMissing: 0);
                return;
            }
            s_windowOpen = true;
        }
        catch (System.Exception ex)
        {
            s_windowOpen = false;
            Watched.Clear();
            Disable("opening a measurement window", ex);
        }
    }

    // -------------------------------------------------------------------- the pump --

    /// <summary>
    /// One frame's worth of warm-up and measurement, called once per frame by
    /// <c>CardsDriver.UpdateBody</c> BEFORE its hands-down bail-out — the warm-up must run whether
    /// or not the player's hands are up, because that is the whole point of it. Costs a
    /// <c>Count == 0</c> test on both lists in the steady state.
    /// </summary>
    internal static void Pump()
    {
        if (s_disabled)
            return;
        if (Pending.Count > 0)
            DrainWarmQueue();
        if (s_windowOpen)
            TickWindow();
    }

    private static void DrainWarmQueue()
    {
        int started = 0;
        // Drains from the FRONT: an entry that needs no work is removed and costs nothing, so a
        // frame may discard many of those and still start at most MaxWarmsPerFrame real loads.
        while (Pending.Count > 0 && started < MaxWarmsPerFrame)
        {
            FullAbilityCard face = Pending[0];
            Pending.RemoveAt(0);
            if (face == null)
                continue;
            try
            {
                if (face.gameObject.activeInHierarchy)
                {
                    s_warmSkippedActive++;
                    continue;
                }
                if (AnyLoadInFlight(face))
                {
                    s_warmSkippedInFlight++;
                    continue;
                }
                if (FaceReady(face))
                {
                    s_warmSkippedReady++;
                    continue;
                }
                // THE ONE HAZARD, AND IT IS REAL. ImageAddressableLoader builds its per-Image
                // context dictionary in Awake (ImageAddressableLoader.cs:33-38), and Awake does not
                // run under an inactive ancestor. A widget whose full face has NEVER been active
                // therefore has a null dictionary and ShowCard would throw inside the game. Calling
                // Awake ourselves would be worse than skipping: Unity would call it again on the
                // first real activation and RESET the dictionary, throwing the warm state away and
                // restoring the very defect this class exists to remove. So such a face is skipped,
                // counted, and named in the falsifier line — if that count is ever non-zero on
                // hardware, the follow-up is a class-level Addressables pin, not a poke at Awake.
                ImageAddressableLoader? loader = face._imageLoader;
                if (loader == null || loader._loadingContexts == null)
                {
                    s_warmSkippedNoLoader++;
                    continue;
                }
                face.ShowCard();
                s_warmed++;
                started++;
            }
            catch (System.Exception ex)
            {
                s_warmFailed++;
                VRLog.Debug(Scope, $"Card-art warm-up skipped one face ({ex.GetType().Name}: {ex.Message}) — "
                                   + "that card keeps the game's own load timing.");
            }
        }
    }

    private static void TickWindow()
    {
        int missing = MissingNow();
        double ms = (Stopwatch.GetTimestamp() - s_windowStart) * 1000.0 / Stopwatch.Frequency;
        if (missing > 0 && ms < ReadyTimeoutSeconds * 1000.0)
            return;
        CloseWindow(cutShort: false);
    }

    /// <summary>Finish the open measurement window and emit its line.</summary>
    private static void CloseWindow(bool cutShort)
    {
        int missing = MissingNow();
        double ms = (Stopwatch.GetTimestamp() - s_windowStart) * 1000.0 / Stopwatch.Frequency;
        int frames = Time.frameCount - s_windowStartFrame;
        s_windowOpen = false;
        Watched.Clear();
        Report(achieved: false, waitedMs: ms, waitedFrames: frames, stillMissing: missing,
               cutShort: cutShort);
    }

    /// <summary>How many watched faces still have no art on this frame.</summary>
    private static int MissingNow()
    {
        int missing = 0;
        for (int i = 0; i < Watched.Count; i++)
        {
            FullAbilityCard face = Watched[i];
            if (face != null && !FaceReady(face))
                missing++;
        }
        return missing;
    }

    // ---------------------------------------------------------------- the falsifier --

    /// <summary>
    /// THE FALSIFIER, one line per measured fan open. It names the character, how many cards the fan
    /// held, HOW MANY OF THEM HAD NO ART AT THE MOMENT THE FAN OPENED — the pass condition is ZERO
    /// and it is measured, never assumed — how long the player then had to wait for the last one,
    /// and what the warm-up itself managed and refused. A run in which the warm-up did nothing looks
    /// nothing like a run in which it worked, which is the point.
    /// </summary>
    private static void Report(bool achieved, double waitedMs, int waitedFrames, int stillMissing,
                               bool cutShort = false)
    {
        // The budget counts PASSING lines only. A NOT ACHIEVED open is always logged and never
        // spends budget: it is the outcome this instrument exists to report, and letting a run of
        // failures silence the passes would make a recovered session look like a stopped tick.
        if (achieved && s_linesSpent >= PassLineBudget)
            return;
        bool lastPassLine = false;
        if (achieved)
        {
            s_linesSpent++;
            lastPassLine = s_linesSpent == PassLineBudget && !s_budgetNoted;
            if (lastPassLine)
                s_budgetNoted = true;
        }

        VRLog.Info(Scope,
            "HAND FAN ART WARM-UP: " + (achieved ? "CONFIRMED" : "NOT ACHIEVED")
            + $" — fan open #{s_opens} for '{s_windowLabel}', {s_windowCards} card face(s); "
            + $"NOT READY AT OPEN: {s_windowNotReady} (the pass condition is 0 and it is MEASURED — "
            + "a face counts as ready when no ImageAddressableLoader under it still holds a "
            + "reference and its header and both action-half backgrounds carry a sprite on an "
            + "enabled Image); "
            + (achieved
                ? "waited 0.0 ms / 0 frame(s) — the fan's FIRST rendered frame already carried the art"
                : $"waited {waitedMs:F1} ms / {waitedFrames} frame(s) for the last one, "
                  + $"{stillMissing} face(s) still without art at the end of the window "
                  + $"({(cutShort
                         ? "CUT SHORT — the fan re-opened before this window finished, so the wait "
                           + "above is a LOWER BOUND"
                         : stillMissing > 0
                             ? "TIMED OUT at " + ReadyTimeoutSeconds.ToString("F0") + " s"
                             : "they all landed")})")
            + $"; warm-up so far: {s_warmed} face(s) pre-loaded while parked, "
            + $"{s_warmSkippedReady} already had their art, {s_warmSkippedActive} were already active "
            + $"(the game was driving their loads), {s_warmSkippedInFlight} were already warming, "
            + $"{s_warmSkippedNoLoader} could NOT be warmed because their ImageAddressableLoader had "
            + "never awoken (see DrainWarmQueue — a non-zero count here is the ONE case this fix "
            + "does not cover, and the follow-up is a class-level Addressables pin), "
            + $"{s_warmFailed} threw; {Pending.Count} still queued. "
            + "WHAT A FAILING LINE MEANS: a non-zero 'NOT READY AT OPEN' with a large 'pre-loaded' "
            + "count means the warm-up ran but did not beat the palm roll — the hand bound too late, "
            + "not that the seam is wrong; a non-zero count with 'pre-loaded' 0 means the edge never "
            + "fired and CardFace.Adopt is not reaching NoteAdopted."
            + (lastPassLine
                ? " THIS IS THE LAST PASSING LINE THIS SESSION WILL PRINT (budget "
                  + PassLineBudget + "); from here on only a NOT ACHIEVED open is logged, so silence "
                  + "below means every fan opened with its art already on it."
                : string.Empty));
    }

    // ------------------------------------------------------------------- readiness --

    /// <summary>
    /// Does this face have its streamed art ON SCREEN — i.e. would it draw as a card and not as the
    /// grey slab the report describes? Three terms, one per addressable load
    /// <c>ShowCard()</c> starts, plus the loader's own in-flight counter (which is also what
    /// alpha-0s the hide-while-loading groups, so a face with a live load is never "ready" even if
    /// its sprites happen to be assigned).
    /// </summary>
    private static bool FaceReady(FullAbilityCard face)
    {
        if (AnyLoadInFlight(face))
            return false;
        if (!HeaderReady(face))
            return false;
        return HalfReady(face.topActionButton) && HalfReady(face.bottomActionButton);
    }

    /// <summary>The card BACKGROUND — <c>headerImage</c>, fed from the skin's TitleSprite. A face
    /// with no skin never loads one (<c>ShowCard</c> skips the whole block, FullAbilityCard.cs:440),
    /// so there is nothing to wait for and nothing to blame.</summary>
    private static bool HeaderReady(FullAbilityCard face)
    {
        if (face._skin == null)
            return true;
        Image? header = face.headerImage;
        if (header == null)
            return true;
        return header.sprite != null && header.enabled;
    }

    /// <summary>
    /// One action half's background. A half whose reference was initialised with a SPECIAL sprite
    /// (the long-rest card's) never enters the addressable path at all —
    /// <c>ButtonLoadingContext.LoadAsync</c> skips <c>LoadBackgroundAsync</c> for it
    /// (ButtonLoadingContext.cs:51-53) — so its Image is never assigned by a loader and testing it
    /// would make every long-rest card permanently "not ready".
    /// </summary>
    private static bool HalfReady(FullAbilityCardAction? half)
    {
        if (half == null)
            return true;
        SpriteMemoryManagement.ReferenceToSprite? reference = half._referenceForImageActionButton;
        if (reference == null || reference.InitializedWithSpecialSprite)
            return true;
        Button? button = half.actionButton;
        Image? image = button != null ? button.image : null;
        if (image == null)
            return true;
        return image.sprite != null && image.enabled;
    }

    /// <summary>True while any <c>ImageAddressableLoader</c> under the face still holds an
    /// outstanding load — its public <c>ReferenceCount</c>, the same counter that alpha-0s the
    /// hide-while-loading groups (ImageAddressableLoader.cs:63-75).</summary>
    private static bool AnyLoadInFlight(FullAbilityCard face)
    {
        LoaderScratch.Clear();
        face.GetComponentsInChildren(includeInactive: true, LoaderScratch);
        for (int i = 0; i < LoaderScratch.Count; i++)
        {
            ImageAddressableLoader loader = LoaderScratch[i];
            if (loader != null && loader.ReferenceCount > 0)
                return true;
        }
        return false;
    }

    /// <summary>Character label for the falsifier line — the owner of the card, or the card's own
    /// class model when the widget has no player actor (a map-phase / preview face). Diagnostic
    /// only; it never drives a decision.</summary>
    private static string OwnerLabel(FullAbilityCard face)
    {
        try
        {
            ScenarioRuleLibrary.CPlayerActor? owner = face.playerActor;
            if (owner != null && !string.IsNullOrEmpty(owner.CharacterName))
                return owner.CharacterName;
            ScenarioRuleLibrary.CAbilityCard? card = face.AbilityCard;
            if (card != null && !string.IsNullOrEmpty(card.ClassModel))
                return card.ClassModel;
        }
        catch
        {
            // a label is never worth a throw
        }
        return "?";
    }

    // ------------------------------------------------------------------- lifecycle --

    /// <summary>Stop everything after an unexpected throw, once and loudly. The cards keep the
    /// game's own load timing — i.e. exactly the behaviour that shipped before this class.</summary>
    private static void Disable(string what, System.Exception ex)
    {
        s_disabled = true;
        Pending.Clear();
        Watched.Clear();
        s_windowOpen = false;
        if (s_disableLogged)
            return;
        s_disableLogged = true;
        VRLog.Warn(Scope, $"CARD ART WARM-UP disabled — {what} threw ({ex.GetType().Name}: {ex.Message}). "
                          + "Hand cards fall back to the game's own load timing, i.e. the art starts "
                          + "streaming when the fan opens (the grey first moment returns).");
    }

    /// <summary>Drop all state (module shutdown / hot reload). Nothing is held, so there is nothing
    /// to release — the sprites belong to the game's widgets and to its own loader lifecycle.</summary>
    internal static void Reset()
    {
        Pending.Clear();
        Watched.Clear();
        LoaderScratch.Clear();
        s_windowOpen = false;
        s_warmed = 0;
        s_warmSkippedActive = 0;
        s_warmSkippedInFlight = 0;
        s_warmSkippedReady = 0;
        s_warmSkippedNoLoader = 0;
        s_warmFailed = 0;
        s_opens = 0;
        s_linesSpent = 0;
        s_budgetNoted = false;
        s_disabled = false;
        s_disableLogged = false;
    }
}
