// THE GAME'S OWN CARD PLUME, ON A MIRRORED CARD — the receiver half of wire id 236
// (NetProtocol.TuneGameCardParticlesOn, [Cards] GameCardParticles).
//
// THE OLD WIRE DEBT'S REASON WAS WRONG, and correcting it is why this file exists. It read: "a
// peer's mirrored cards are mod slabs with no game particle system to switch on", i.e. the receiver
// COULD not draw the plume. Every clause of that is false:
//
//   * The plume is a PREFAB, not a component sitting on a card. CardEffects.SpawnParticle
//     pool-spawns GlobalSettings.Instance.VisualEffects.CardSmoke onto whatever hosts the card
//     (decompiled CardEffects.cs:741-757). Nothing about it is bound to a game card object.
//   * CardSmoke is a PUBLIC field on a singleton loaded straight out of Resources
//     (GlobalSettings.cs:355 / :375-384). No scene object, no local player and no card on screen is
//     needed to reach it — every client that has the game installed has the prefab.
//   * The taming is a solved problem, though NOT the way the debt's own retirement note claimed.
//     It said BurnCardFx "already instantiates" this prefab; it does not, and has not since commit
//     71883140. Today's BurnCardFx BINDS the game's already-spawned instance and reparents it. The
//     method that did spawn one, SpawnConsumedPlume, was REMOVED for shipping the field-covering
//     fog — and its removal note is the more valuable text, because it states what a spawn path
//     owes that a reparent does not. See the class doc below, which pays all four items.
//
// So the receiver was never missing a capability. It was missing the owner's SAY-SO — which is now
// on the wire — and a host to hang the copy on, which is this file.

using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Hosts a TAMED copy of the game's <c>CardSmoke</c> plume on one MIRRORED card, for the receiver
/// side of <see cref="NetProtocol.TuneGameCardParticlesOn"/>. NO PICTURE TRAVELS: the wire carries
/// one permission bit and this client instantiates the game's own prefab out of its own
/// <c>Resources</c>.
///
/// <para>WHY IT MUST BE TAMED, and why a SPAWN path needs MORE taming than
/// <see cref="Cards.BurnCardFx"/>'s reparent. The plume is authored for the game's full-size
/// SCREEN-SPACE card. Left alone in the diorama it sprays across the whole play field — the exact
/// symptom that got these particles switched off in the first place (user, 2026-08-03: "kam während
/// dessen so eine sehr große Funken/Partikel Animation über das gesamte Spielfeld … Geh da rein,
/// das soll deaktiviert werden!"). BurnCardFx bounds the game's ONE managed instance by REPARENTING
/// it onto the small world card, which shrinks the whole child hierarchy by the screen-card scale
/// it leaves behind — every sub-emitter comes down to card size for free. A spawn path has no such
/// scale to leave behind, and BurnCardFx's own removal note (the deleted <c>SpawnConsumedPlume</c>,
/// which shipped the field-covering fog once already) states the price of getting that wrong and
/// the checklist any revival owes. All four items are paid here:</para>
/// <list type="number">
///   <item><description>EVERY emitter is clamped, not just the first — <c>GetComponentsInChildren</c>
///   rather than <c>GetComponentInChildren</c>. That single word is what left the earlier attempt's
///   other emitters at World simulation and authored screen scale.</description></item>
///   <item><description><c>simulationSpace = Local</c> + <c>scalingMode = Hierarchy</c> on each, so
///   particles ride the card and inherit its world scale instead of being emitted into world space
///   at authored size — BurnCardFx's two module writes, term for term.</description></item>
///   <item><description>The same extra <see cref="StartSizeMultiplier"/>/<see cref="StartSpeedMultiplier"/>
///   shrink BurnCardFx applies on top, and a hard cap on start LIFETIME so nothing outlives the
///   card it belongs to.</description></item>
///   <item><description>The plume ROOT's scale is PINNED to the card's: the host is parented to the
///   slab at identity, so its world scale IS the slab's and the authored screen-card root scale the
///   prefab ships with is discarded rather than multiplied in.</description></item>
/// </list>
///
/// <para>THE TINT IS NOT CARRIED, deliberately. <c>CardEffects.fx_Smoke_color</c> is a PRIVATE field
/// assigned from a per-call-site literal — dark grey <c>(0.149, 0.149, 0.149, 0.5)</c> for the burn
/// (CardEffects.cs:527), cold blue-grey <c>(0.302, 0.302, 0.388, 0.4)</c> for the ghost/lost sweep
/// (CardEffects.cs:641) — and a receiver has no live effect object to read it off. The two honest
/// options were "duplicate both constants and guess which one applies" and "keep the prefab's own
/// authored <c>startColor</c>". This keeps the AUTHORED colour, for three reasons: the receiver
/// cannot tell burn from ghost without a second wire field it has not been given, so a guess is
/// wrong roughly half the time; a duplicated literal is the exact shape
/// <c>Cards.CardDustFx.DefaultTone</c> exists to prevent (a mirror holding its own copy of a number
/// diverges the day someone retunes one of the two); and the authored value is the one both game
/// call sites START from, so a game patch that retunes the plume moves this copy with it. If a
/// hardware round says the mirrored plume reads too pale, that is ONE more wire field for the
/// colour, not a redesign.</para>
///
/// <para>DEGRADES TO NOTHING, never throws. A missing <c>GlobalSettings</c> (headless/CI — its
/// getter dereferences a <c>Resources.Load</c> result and NREs when the load fails, so it is not
/// enough to null-check the result), a missing <c>VisualEffects</c> or a missing <c>CardSmoke</c>
/// (a future game patch) all end in "no plume drawn" with one log line. Nothing here runs per frame
/// in the steady state: <see cref="Spawn"/> is called on an EDGE.</para>
/// </summary>
internal static class RemoteCardPlume
{
    /// <summary>
    /// The extra shrink on top of the Hierarchy scaling, <see cref="Cards.BurnCardFx"/>'s numbers
    /// verbatim: the game authors the plume for its full-size screen card, so even sized to the
    /// small world card the sparks read big. Shared with that class by VALUE and not by reference
    /// because the two live on opposite sides of the presentation boundary — if the local clamp is
    /// ever retuned, this is the line to re-seed, and the sentence you are reading is the pointer.
    /// </summary>
    private const float StartSizeMultiplier = 0.35f;

    /// <summary>Companion of <see cref="StartSizeMultiplier"/> on <c>main.startSpeedMultiplier</c>.</summary>
    private const float StartSpeedMultiplier = 0.35f;

    /// <summary>
    /// Hard ceiling on every emitter's start lifetime, in seconds. The GAME does not need one — its
    /// instance is owned by a running coroutine that recycles it (<c>CardEffects.HideParticle</c>)
    /// — but this copy has no coroutine to end it, so the cap is what makes it self-terminating.
    /// 1.4 s is the bound the removed <c>SpawnConsumedPlume</c> was written to and the only number
    /// on this path that has ever been looked at on hardware.
    /// </summary>
    private const float MaxLifetimeSeconds = 1.4f;

    /// <summary>Slack between the last particle dying and the host being destroyed.</summary>
    private const float HostGraceSeconds = 0.25f;

    /// <summary>
    /// Absolute ceiling on how long a host may live, whatever the prefab's own duration says. A
    /// game patch that turns CardSmoke into a long looping ambience must not be able to leave
    /// hosts parked on a peer's hand.
    /// </summary>
    private const float MaxHostSeconds = 4f;

    /// <summary>
    /// How many plume hosts may exist across ALL peers at once. Not a performance budget — a fuse.
    /// A hand holds at most ~10 cards and a burn is a single card, so reaching this means something
    /// upstream is edge-triggering every frame, and the right failure is "stops drawing" rather
    /// than "fills the scene". The count is a fuse and NOT a verdict: it never latches, so the very
    /// next release re-arms it (a churn limiter that latches counts the PLAYER as the abuser).
    /// </summary>
    private const int MaxLive = 12;

    /// <summary>The game's plume prefab, cached on SUCCESS ONLY. A failure is never cached: the
    /// singleton loads out of <c>Resources</c> and can be unreachable during boot and fine two
    /// seconds later, which is the same trap <c>Core.BundleShaders</c> documents for
    /// <c>Shader.Find</c>.</summary>
    private static GameObject? _prefab;

    /// <summary>One-shot latch for the "no prefab" line — the resolve is retried forever, the log
    /// is not.</summary>
    private static bool _missingLogged;

    /// <summary>One-shot latch for the "first plume drawn" evidence line.</summary>
    private static bool _spawnLogged;

    /// <summary>Live host count (see <see cref="MaxLive"/>), decremented by <see cref="Reaper"/>.</summary>
    private static int _live;

    /// <summary>
    /// Instantiate one tamed plume on <paramref name="card"/> — a MIRRORED card slab — and let it
    /// destroy itself when the last particle has died. Call on an EDGE, once per effect episode.
    ///
    /// <para>The caller owns the PERMISSION: this is drawn only because the card's owner has
    /// <c>[Cards] GameCardParticles</c> ON, off wire id
    /// <see cref="NetProtocol.TuneGameCardParticlesOn"/>. The VIEWER's own copy of that dial is
    /// deliberately NOT consulted anywhere in this file — it answers a different question ("do MY
    /// OWN cards spray the game's particles?", which <c>Compat.CardParticlesOff</c> answers by
    /// pinning the game's low-spec switch), and ANDing the two is exactly the defect that left the
    /// mirrored card DUST inert from the day it shipped. See <c>Cards.CardDustFx.Permission</c>.</para>
    ///
    /// <para><paramref name="playerId"/> and <paramref name="slot"/> are for the log only — no card
    /// identity is involved, and a slot INDEX is the same thing the fan's highlight already
    /// carries.</para>
    /// </summary>
    internal static void Spawn(Transform? card, int playerId, int slot)
    {
        if (card == null || _live >= MaxLive)
            return;
        GameObject? prefab = Prefab();
        if (prefab == null)
            return;

        try
        {
            // worldPositionStays:false, then identity — the prefab's authored root transform is
            // the game's SCREEN card's, and carrying any of it over is what makes a spawned plume
            // diorama-sized. The slab root's own uniform scale (the owner's [Cards] CardWidth over
            // the nominal one) is therefore the plume's world scale, which is precisely the frame
            // BurnCardFx reaches by reparenting the game's instance onto the world card.
            GameObject host = Object.Instantiate(prefab!, card, worldPositionStays: false);
            host.name = "GloomhavenVR.RemoteCardPlume";
            Transform ht = host.transform;
            ht.localPosition = Vector3.zero;
            ht.localRotation = Quaternion.identity;
            ht.localScale = Vector3.one;

            ParticleSystem[] systems = host.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            if (systems.Length == 0)
            {
                // A prefab with no emitter at all draws nothing and would just sit there.
                Object.Destroy(host);
                return;
            }

            float ttl = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                // ONE-SHOT. The game keeps its instance alive for the whole burn coroutine and
                // recycles it afterwards; this copy has no coroutine, so a looping emitter would
                // never stop and the timed destroy below would cut it off mid-plume instead.
                main.loop = false;
                main.startSizeMultiplier *= StartSizeMultiplier;
                main.startSpeedMultiplier *= StartSpeedMultiplier;
                if (main.startLifetimeMultiplier > MaxLifetimeSeconds)
                    main.startLifetimeMultiplier = MaxLifetimeSeconds;
                // The last particle of this emitter can die no later than duration + its lifetime.
                ttl = Mathf.Max(ttl, main.duration + main.startLifetimeMultiplier);
            }

            // The mod's owned head camera renders the mod layer only — the same step CardDustFx's
            // pool takes, and skipping it is how an effect ends up visible to no camera at all.
            VRLayers.Apply(host);

            // The prefab ships playOnAwake (the game only ever Spawns it and sets a colour), so it
            // is already running by the time the modules above were written — which is fine,
            // emission happens in the particle update later this same frame. The Play is the
            // fallback for a future prefab that does not auto-play.
            ParticleSystem? rootPs = host.GetComponent<ParticleSystem>();
            if (rootPs != null && !rootPs.isPlaying)
                rootPs.Play(withChildren: true);

            _live++;
            host.AddComponent<Reaper>();
            Object.Destroy(host, Mathf.Clamp(ttl + HostGraceSeconds, 0.5f, MaxHostSeconds));

            if (!_spawnLogged)
            {
                _spawnLogged = true;
                VRLog.Info("Net", $"Remote card plume [player {playerId}]: the GAME's own CardSmoke " +
                                  $"prefab is now hosted on mirrored slot {slot}, tamed to the card " +
                                  $"(every emitter Local+Hierarchy, size/speed x{StartSizeMultiplier:F2}, " +
                                  $"lifetime <= {MaxLifetimeSeconds:F1} s, root scale pinned to the slab). " +
                                  "Wire id 236 carried ONE permission bit; the picture is this client's " +
                                  "own copy of the prefab. The VIEWER's [Cards] GameCardParticles is not " +
                                  "consulted — it governs the viewer's OWN cards.");
            }
            else
            {
                VRLog.Debug("Net", $"Remote card plume [player {playerId}] slot {slot}: spawned " +
                                   $"({systems.Length} emitter(s), host ttl {ttl + HostGraceSeconds:F2} s, " +
                                   $"{_live} live).");
            }
        }
        catch (System.Exception e)
        {
            // A cosmetic mirror may never take the frame down with it.
            VRLog.Warn("Net", $"Remote card plume [player {playerId}] slot {slot} failed " +
                              $"({e.GetType().Name}: {e.Message}) — no plume is drawn for this card. " +
                              "Everything else on the peer's board is unaffected.");
        }
    }

    /// <summary>Balance for the <c>_live</c> increment in <see cref="Spawn"/>.</summary>
    private static void Release()
    {
        if (_live > 0)
            _live--;
    }

    /// <summary>
    /// The game's <c>CardSmoke</c> prefab, or null when this environment has no game settings to
    /// read (headless/CI) or a game patch has renamed the field.
    ///
    /// <para><c>GlobalSettings.Instance</c> is not merely nullable — its getter is
    /// <c>(Resources.Load(…) as GameObject).GetComponent&lt;GlobalSettings&gt;()</c>, which THROWS
    /// when the load returns null. That is why the whole chain sits inside the try rather than
    /// behind null checks.</para>
    /// </summary>
    private static GameObject? Prefab()
    {
        if (_prefab != null)
            return _prefab;
        try
        {
            GlobalSettings settings = GlobalSettings.Instance;
            GlobalSettings.VisualEffectPrefabs? fx = settings != null ? settings.VisualEffects : null;
            _prefab = fx != null ? fx.CardSmoke : null;
        }
        catch (System.Exception)
        {
            _prefab = null;
        }
        if (_prefab == null && !_missingLogged)
        {
            _missingLogged = true;
            VRLog.Info("Net", "Remote card plume: the game's CardSmoke prefab could not be reached " +
                              "(GlobalSettings.Instance / VisualEffects / CardSmoke) — a peer's cards " +
                              "simply draw no game plume. Nothing else changes, and the resolve is " +
                              "retried on the next card that asks: the settings singleton loads out " +
                              "of Resources and can be unreachable during boot and fine afterwards.");
        }
        return _prefab;
    }

    /// <summary>
    /// Decrements the live-host count when its host dies — by the timed <c>Destroy</c> above, or
    /// because the slab, the fan or the scene it hangs under went away first. Attached rather than
    /// counted down on a timer for exactly that reason: the host has three ways to die and only one
    /// of them is ours.
    /// </summary>
    private sealed class Reaper : MonoBehaviour
    {
        private void OnDestroy() => Release();
    }
}
