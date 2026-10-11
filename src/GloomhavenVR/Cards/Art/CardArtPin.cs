using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using SpriteMemoryManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GloomhavenVR.Cards;

/// <summary>
/// GREY CARDS ON THE FIRST FAN OPEN, ROUND TWO — the class-level Addressables pin that
/// <see cref="CardArtPrewarm"/> named as its own follow-up and deliberately did not ship.
///
/// <para>THE REPORT (user, verbatim, 2026-08-24, against ModBuild 243): "Die zuerst grau-beigen
/// Karten bei erstmaligem Öffnen des Fächers ist immer noch sichtbar. Immer nur wenn der Fächer das
/// allererste Mal geöffnet wird. Ich möchte gerne, dass das nicht mehr vorkommt. Und die Karten am
/// besten schon so vorgeladen werden, als wäre sie schon einmal geöffnet worden."</para>
///
/// <para>WHY ModBuild 243's FIX DID NOTHING HERE, read out of the hardware log rather than guessed:
/// <c>HAND FAN ART WARM-UP</c> appears <b>zero</b> times in LogOutput.log. The instrument did not
/// fail — it was never reached. <see cref="CardArtPrewarm.NoteFanOpened"/> walks
/// <c>VRCard.GameCard</c>, and a MAP-ROOM card has none: <c>MapRoomHand</c> never calls
/// <c>VRCard.AttachGameCard</c> at all, which is the stated basis of that feature's
/// inspection-only guarantee. So every term of 243's fix — the adopt edge, the queue, the
/// measurement window — is structurally absent from the fan the user is opening. He is in the map
/// room; his screenshot in the same drop is the map room. 243 fixed the SCENARIO fan, which is real
/// and which he has not seen yet, and it could never have fixed this one.</para>
///
/// <para>AND THE MAP-ROOM FAN CANNOT BE FIXED THE 243 WAY, EITHER. Its front is not an adopted
/// widget but an <c>Object.Instantiate</c> CLONE built by <c>Net.RemoteCardArt</c>, and a
/// clone carries a FRESH <c>ImageLoadingContext</c>. That class's one fast path is
/// <c>_lastState == FinishedSuccessfully</c> ON THE SAME CONTEXT (ImageLoadingContext.cs:24-29), so
/// a clone can never inherit another widget's warmth. At ModBuild 244 the clone was also built
/// only when the fan opened. The later map-hand CPU prewarm builds that same future clone under an
/// inactive host across several frames, but its <c>OnEnable</c> and final <c>FitClone</c> still run
/// only after the slab enters the active hierarchy. Warming a different widget remains the wrong
/// lever for asynchronous artwork; this asset pin supplies the class sprites underneath every
/// fresh context. It does not duplicate a card texture or a rendered face.</para>
///
/// <para>WHAT THIS DOES, AND WHY IT IS SMALL. Every sprite behind the grey slab is a CLASS asset,
/// not a card asset — <c>FullAbilityCard.ShowCard</c> loads <c>_skin.TitleSprite</c> (twice: header
/// and unfocused mask) and each half's <c>Show()</c> loads
/// <c>skin.{Top,Bottom}ActionRegularSprite</c> (FullAbilityCard.cs:438-446,
/// FullAbilityCardAction.cs:511-540), and <c>_skin</c> is <c>CAbilityCard.ClassModel</c>, an
/// <c>AbilityCardUISkin</c> shared by every card of that class. So THREE addressable keys cover a
/// whole hand — the same three whatever the card, whoever the widget, however fresh its context.
/// This class holds an <c>Addressables.LoadAssetAsync&lt;Sprite&gt;</c> handle on each. Once the
/// asset is resident, the game's own per-widget load still runs, still through its own loader, and
/// still sets <c>image.enabled</c> around it — but it resolves from memory instead of from disk,
/// which is the difference between the visible grey period and a frame.</para>
///
/// <para>THE COST, MEASURED FROM THE GAME'S OWN ASSETS RATHER THAN ESTIMATED: on this party the
/// three keys are <c>AC_&lt;Class&gt;_Background</c> 1254x1916 and two 1085x651 halves, i.e. about
/// 4 MB compressed per class. One local hand needs three keys; a multiplayer map
/// can pin one such set per distinct displayed class because remote map fans pin
/// their public class before opening. The set is bounded by party classes rather
/// than by card count or frames. Nothing is copied; the handles below are
/// REFERENCES on assets the game will load, held a few seconds earlier and longer.</para>
///
/// <para>WHAT THE ORDINARY FIRST-FAN PIN DOES NOT LOAD: the HIGHLIGHT / SELECTED / DISABLED state sprites that
/// <c>SetSkin(selected: true)</c> swaps in. They are the same size again, four per half, and none of
/// them is on screen when the fan opens — the defect is the card's FIRST drawn frame. Pinning them
/// would triple the resident set to remove a hitch nobody has reported on a hover. If a state swap
/// ever reads grey, this is the line to change and the reason it was not. ScenarioCardPreparation
/// now additionally prepares those original states during the loading gate: the Build616 capture
/// identifies a 172ms later class switch, beyond the original first-drawn-frame defect.</para>
///
/// <para>MULTIPLAYER: nothing here goes near the wire, and no game state is written. It asks
/// Addressables for an asset by GUID and releases it again; the game's own loaders, contexts and
/// reference counts are untouched, and card identity still never leaves this machine. Remote
/// fan callers must still pass RevealGate's map-phase public-front predicate before pinning.</para>
/// </summary>
internal static class CardArtPin
{
    private const string Scope = "Cards";

    /// <summary>One handle per addressable GUID. Keyed by GUID rather than by skin so two classes
    /// that share a background pay for it once, and so a re-pin of the same class is free.</summary>
    private static readonly Dictionary<string, AsyncOperationHandle<Sprite>> Held = new(8);

    /// <summary>Skins already walked this session — the walk is three field reads, but saying so in
    /// the log once per class is what makes the falsifier readable.</summary>
    private static readonly HashSet<int> SeenSkins = new();

    private static bool s_disabled;
    private static int s_pinned;
    private static int s_failed;

    /// <summary>
    /// Pin the three class assets behind <paramref name="card"/>'s printed face. Idempotent and
    /// cheap: a GUID already held is a dictionary hit and nothing else.
    /// </summary>
    internal static void PinForCard(CAbilityCard? card)
    {
        if (s_disabled || card == null)
            return;
        try
        {
            // THE SAME RESOLUTION THE GAME ITSELF USES, and no other: AbilityCardUI.Init calls
            // SetSkin(abilityCard.ClassModel, abilityCard.ClassCharacterConfig) (AbilityCardUI.cs:554),
            // whose string overload is UIInfoTools.Instance.GetCardSkin(playerName, custom)
            // (:728-731). Going through the same lookup is what guarantees we pin the assets the
            // widget will actually ask for, rather than a skin that merely looks related
            // [[containment-is-not-identity]].
            if (UIInfoTools.Instance == null)
                return;
            PinForClass(UIInfoTools.Instance.GetCardSkin(card.ClassModel, card.ClassCharacterConfig));
        }
        catch (System.Exception ex)
        {
            Disable("reading a card's class skin", ex);
        }
    }

    /// <summary>Pin the three class assets of one <c>AbilityCardUISkin</c>.</summary>
    internal static void PinForClass(AbilityCardUISkin? skin)
    {
        if (s_disabled || skin == null)
            return;
        try
        {
            int id = skin.GetHashCode();
            bool firstTimeForThisSkin = SeenSkins.Add(id);

            int before = Held.Count;
            PinReference(skin.TitleSprite);
            PinReference(skin.TopActionRegularSprite);
            PinReference(skin.BottomActionRegularSprite);

            if (firstTimeForThisSkin)
            {
                VRLog.Info(Scope,
                    $"CARD ART PIN: class skin pinned — {Held.Count - before} new addressable "
                    + $"key(s), {Held.Count} held in total ({s_failed} refused). THESE ARE THE THREE "
                    + "ASSETS BEHIND THE GREY SLAB: the card background (FullAbilityCard.headerImage "
                    + "and unfocusedMask both take _skin.TitleSprite) and the two action halves' "
                    + "backgrounds (skin.Top/BottomActionRegularSprite). They belong to the CLASS, "
                    + "not to a card, so this covers the whole hand. WHY A PIN AND NOT A WARM-UP: a "
                    + "map-room card's face is an Object.Instantiate CLONE with a FRESH "
                    + "ImageLoadingContext, and that class's only fast path needs the SAME context "
                    + "to have finished — so no amount of warming another widget can help it. An "
                    + "asset that is already resident can. GREP 'CARD ART PIN' for the release line "
                    + "and its count; a session that pins and never releases is the leak this "
                    + "instrument exists to show.");
            }
        }
        catch (System.Exception ex)
        {
            Disable("pinning a class skin", ex);
        }
    }

    /// <summary>
    /// Release every handle. Called on the edges that end a hand's life — leaving the map room,
    /// leaving a scenario, the module teardown — so the pin never outlives the fan it was taken for.
    /// </summary>
    internal static void ReleaseAll(string why)
    {
        if (Held.Count == 0)
        {
            SeenSkins.Clear();
            return;
        }
        int n = Held.Count;
        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> kv in Held)
        {
            try
            {
                if (kv.Value.IsValid())
                    Addressables.Release(kv.Value);
            }
            catch (System.Exception ex)
            {
                VRLog.Warn(Scope, $"CARD ART PIN: releasing '{kv.Key}' threw ({ex.GetType().Name}: "
                                  + $"{ex.Message}). The handle is dropped either way; at worst one "
                                  + "asset stays resident until the game's own scene unload.");
            }
        }
        Held.Clear();
        SeenSkins.Clear();
        VRLog.Info(Scope, $"CARD ART PIN: released {n} addressable key(s) — {why}. The assets go "
                          + "back to the game's own reference counting; nothing here held a copy.");
    }

    internal static void PinReference(ReferenceToSprite? reference)
    {
        if (s_disabled || reference == null)
            return;
        // A skin field can carry a plain Sprite instead of an addressable (ReferenceToSprite's
        // SetSpriteInsteadAddressable path, which the long-rest card uses). Already in memory by
        // definition — there is nothing to pin and nothing to report.
        if (reference.InitializedWithSpecialSprite)
            return;
        AssetReferenceSprite assetRef = reference.SpriteReference;
        if (assetRef == null)
            return;
        string guid = assetRef.AssetGUID;
        if (string.IsNullOrEmpty(guid) || Held.ContainsKey(guid))
            return;

        // BY GUID, NOT BY THE AssetReference OBJECT. An AssetReference keeps its OWN operation
        // handle, and loading through the game's instance would put our lifetime on a field the
        // game owns — the exact "never write game state" line. A GUID is a plain catalogue key and
        // the handle that comes back is ours to release.
        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(guid);
        if (!handle.IsValid())
        {
            s_failed++;
            return;
        }
        Held[guid] = handle;
        s_pinned++;
    }

    /// <summary>Observe only our own GUID handle or the native direct-sprite field. Never
    /// call ReferenceToSprite.GetAsyncSprite: that mutates the game's loading request.</summary>
    internal static Sprite? PreparedSprite(ReferenceToSprite reference, out bool pending)
    {
        pending = false;
        if (reference.InitializedWithSpecialSprite) return reference.SpecialSprite;
        AssetReferenceSprite assetRef = reference.SpriteReference;
        if (assetRef == null || string.IsNullOrEmpty(assetRef.AssetGUID)
            || !Held.TryGetValue(assetRef.AssetGUID, out AsyncOperationHandle<Sprite> handle)
            || !handle.IsValid()) return null;
        pending = !handle.IsDone;
        return handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
    }

    private static void Disable(string doing, System.Exception ex)
    {
        if (s_disabled)
            return;
        s_disabled = true;
        VRLog.Warn(Scope, $"CARD ART PIN DISABLED while {doing} ({ex.GetType().Name}: {ex.Message}). "
                          + "CONSEQUENCE, stated so nobody hunts for a second cause: the cards go "
                          + "back to loading their class art on the frame the fan opens, i.e. the "
                          + "grey-slab behaviour of ModBuild 243 and earlier. Nothing else changes — "
                          + "this class only ever holds references, so a failure here cannot leave a "
                          + "card wrong, only late.");
        try { ReleaseAll("the pin disabled itself after an exception"); }
        catch { /* the warning above is the report; a second throw here would bury it */ }
    }
}
