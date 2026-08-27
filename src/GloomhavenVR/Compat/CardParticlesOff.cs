using System;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE GAME'S OWN CARD PARTICLES, OFF (user ruling 2026-08-03, second report: "Im zweiten Tutorial
/// nachdem man seinen Zug beendet, in dem Moment in dem die Karten abgeräumt werden, kam wieder
/// diese 'Funken'-Animation. Ich will dass sie verschwindet.").
///
/// WHY THE PREVIOUS ROUND DID NOT CATCH IT. That round switched off <see cref="Cards.CardDustFx"/>
/// — the MOD's own dust burst — and the hardware log proves it took effect (build 31 banner, no
/// dust emitted). The sparks the player still sees are the GAME's: <c>CardEffects.SpawnParticle</c>
/// pool-spawns the <c>CardSmoke</c> ParticleSystem as a child of the card
/// (<c>GlobalSettings.Instance.VisualEffects.CardSmoke</c>, CardEffects.cs:747). It is authored for
/// the game's full-size SCREEN-SPACE card, so in the diorama it sprays across the whole play field.
///
/// <see cref="Cards.BurnCardFx"/> already tames exactly ONE instance — the burning card it is
/// ticking — by reparenting it onto the small world card. That is why the burn plume looks right
/// and the end-of-turn CLEAR does not: when both played cards are swept into their piles the cards
/// are no longer hosted by a VR card, so nothing is ticking them, nothing binds their smoke, and it
/// plays at its authored screen scale. Chasing that with more binding would mean re-implementing
/// the game's own card lifecycle.
///
/// THE LEVER THE GAME ITSELF SHIPS. Both card-effect paths gate their particles on one flag:
/// <c>PlatformLayer.Setting.LowParticlesUse.NoCardsParticles</c> (CardEffects.cs:743 and
/// MiniCardEffects.cs:594 — "if not NoCardsParticles"). It is a low-spec platform switch, i.e. the
/// vendor's own supported way to run the game without card particles. Pinning it TRUE while VR runs
/// removes every card particle at the source: none is spawned, so none can be mis-scaled, and there
/// is no per-instance taming left to get wrong.
///
/// REFLECTION-GUARDED AND REVERSIBLE, in the <see cref="WallFadeDisable"/> spirit: the private
/// backing field is located once via reflection; if the type, the property chain or the field
/// cannot be found (renamed by a game update), this logs once and does nothing at all, leaving
/// card particles 100% vanilla. The original value is captured before the first write and restored
/// on <see cref="Uninstall"/> (module shutdown / hot reload).
///
/// RE-ASSERTED, NOT SET ONCE: the settings object is reloaded per platform-settings apply, so a
/// single write would silently lapse. The driver re-asserts at 1 Hz — a field compare and, in the
/// steady state, no write at all.
///
/// Purely visual, and it writes no game state. Live-gated by <c>[Cards] GameCardParticles</c>
/// (default OFF); turning it back on restores the vanilla value immediately.
///
/// THE DIAL IS NOT LOCAL-ONLY ANY MORE, and the distinction matters here. The dial also rides the
/// wire as a PERMISSION (<see cref="Net.NetProtocol.TuneGameCardParticlesOn"/>,
/// id 236): a peer's mirrored cards draw the game's plume on THIS owner's say-so, hosted by
/// <see cref="Net.RemoteCardPlume"/>. This class is still the LOCAL half and must stay that way —
/// it answers "do MY OWN cards spray the game's particles?" by pinning the game's own low-spec
/// switch, which gates <c>CardEffects.SpawnParticle</c> and nothing else. The mirror instantiates
/// the prefab itself, so the switch below cannot reach it, and that is deliberate rather than an
/// oversight: extending this suppression to cover a peer's board would re-create the exact defect
/// the card-DUST mirror shipped with, where a viewer's own dial silently withheld a picture its
/// owner had switched on. Do not "fix" the mirror from here.
/// </summary>
internal static class CardParticlesOff
{
    private const string Name = "CardParticlesOff";
    private const string DriverName = "GloomhavenVR.CardParticlesOff";

    private static Driver? _driver;
    private static FieldInfo? _field;
    private static bool _resolved;
    private static bool _resolveFailedLogged;
    private static bool _originalCaptured;
    private static bool _originalValue;
    private static bool _appliedLogged;

    /// <summary>Install the re-assert driver (idempotent). No-op when VR isn't running.</summary>
    internal static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<Driver>();
        VRLog.Info(Name, "installed — the game's own card particles (CardEffects/MiniCardEffects " +
                         "CardSmoke) are pinned off through its low-spec switch " +
                         "PlatformLayer.Setting.LowParticlesUse.NoCardsParticles while " +
                         "[Cards] GameCardParticles is off. They are authored for the full-size " +
                         "screen card and spray across the diorama when a card is swept to a pile.");
    }

    /// <summary>Restore the vanilla value and drop the driver (hot-reload safe).</summary>
    internal static void Uninstall()
    {
        Restore();
        if (_driver == null)
            return;
        try { UnityEngine.Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    /// <summary>Put the game's own value back exactly as found (no-op if never written).</summary>
    private static void Restore()
    {
        if (!_originalCaptured)
            return;
        object? data = SettingsData();
        if (data != null && _field != null)
        {
            try { _field.SetValue(data, _originalValue); }
            catch { /* the settings object died with the scene */ }
        }
        _originalCaptured = false;
        _appliedLogged = false;
    }

    /// <summary>The live <c>LowParticlesUseData</c> instance, or null when it cannot be reached.</summary>
    private static object? SettingsData()
    {
        try
        {
            PlatformSetting? setting = PlatformLayer.Setting;
            return setting != null ? setting.LowParticlesUse : null;
        }
        catch
        {
            return null; // the platform layer is not up yet (menu boot) — the next tick retries
        }
    }

    /// <summary>Locate the private backing field once. Null = leave the game alone forever.</summary>
    private static FieldInfo? Field(object data)
    {
        if (_resolved)
            return _field;
        _resolved = true;
        try
        {
            _field = data.GetType().GetField("_noCardsParticles",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"could not reflect the card-particle flag ({e.GetType().Name}) — " +
                             "the game's card particles are left completely vanilla.");
            _field = null;
        }
        if (_field == null && !_resolveFailedLogged)
        {
            _resolveFailedLogged = true;
            VRLog.Warn(Name, "LowParticlesUseData has no '_noCardsParticles' field on this game " +
                             "build — the card-particle suppression is INERT (vanilla particles). " +
                             "Nothing else is affected.");
        }
        return _field;
    }

    private sealed class Driver : MonoBehaviour
    {
        private const float IntervalSeconds = 1f;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + IntervalSeconds;
            try { Assert(); }
            catch { /* never starve the game loop over a cosmetic flag */ }
        }

        private static void Assert()
        {
            object? data = SettingsData();
            if (data == null)
                return;
            FieldInfo? field = Field(data);
            if (field == null)
                return;

            // Live gate: [Cards] GameCardParticles ON means "leave the game alone" — and if we had
            // already written the flag, put the vanilla value back the same tick.
            bool wantSuppressed = Cards.CardsConfig.GameCardParticles == null
                                  || !Cards.CardsConfig.GameCardParticles.Value;
            if (!wantSuppressed)
            {
                Restore();
                return;
            }

            var current = (bool)(field.GetValue(data) ?? false);
            if (!_originalCaptured)
            {
                _originalCaptured = true;
                _originalValue = current;
            }
            if (current)
                return; // already suppressed (by us last tick, or by the platform profile)
            field.SetValue(data, true);
            if (!_appliedLogged)
            {
                _appliedLogged = true;
                VRLog.Info(Name, $"card particles suppressed (NoCardsParticles {_originalValue} → " +
                                 "true). CardEffects.SpawnParticle and MiniCardEffects both skip " +
                                 "their CardSmoke spawn now, so no card particle exists to be " +
                                 "mis-scaled — this is the game's own low-spec switch, restored " +
                                 "verbatim on shutdown or when [Cards] GameCardParticles is turned on.");
            }
        }
    }
}
