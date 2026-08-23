using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// THE FLICKER THAT APPEARS WHEN THE PER-PIXEL LIGHT CAP IS 0, and the instrument that will
/// name its mechanism.
///
/// <para>USER REPORT, ModBuild 227, verbatim: <i>"Die Performance scheint erheblich von den
/// Pixellichtern abzuhängen. Bereits wenige verschlechtern die Performance enorm. Warum sind die
/// so Performancehungrig? Ich bin daher gewillt '0' als Default-Einstellung zu machen später.
/// Allerdings bringt '0' an manchen Elementen ein komisches Flackern mit sich. Kannst du das
/// fixen?"</i> — and, pointing into <c>.planning/debug/pixellichter_aus_problem.mp4</c>:
/// <i>"Achte im Video auf den vordersten Torbogen, dort sieht man wie Lichter aufflackern."</i></para>
///
/// <para>WHAT THE VIDEO SETTLES, AND WHAT IT DOES NOT. Over the two stretches of that capture where
/// the head is still (frames 199–212 and 372–405 of 413 at 30 fps) the SCENE-WIDE mean luminance is
/// flat to within 0.07 % (76.86→76.90 and 44.00→44.06 of 255). So this is NOT a global brightness
/// pulse — not the ambient level, not a whole-scene shadow toggle, not exposure. It is LOCAL, and he
/// names the place. What the capture CANNOT settle is the mechanism: at 848x478, 30 fps and h264 a
/// one- or two-frame step on a torch-lit archway does not survive, and a per-pixel temporal-variance
/// map of both still stretches finds nothing above the sub-pixel edge noise of head tracking (the
/// only hot regions are the player's own hands). The mechanism therefore has to be settled in the
/// GAME'S state, which is what the census and the watcher below are for.</para>
///
/// <para>WHAT IS LOCAL AND CAP-DEPENDENT, from the decompiled sources. In the built-in FORWARD path
/// a renderer's lights are classified PER OBJECT and PER FRAME into three tiers: per-pixel (up to
/// <see cref="QualitySettings.pixelLightCount"/>, plus every light whose
/// <see cref="Light.renderMode"/> is <see cref="LightRenderMode.ForcePixel"/> — those are per-pixel
/// WHATEVER the cap says, including at 0), then at most FOUR per-vertex slots, then spherical
/// harmonics. The tier boundaries are hard steps, and the ranking is recomputed every frame from
/// intensity and distance. This dungeon runs 44 enabled lights (32 point, 12 spot — [Perf] GFX) over
/// a scene of ~8,570 renderers, so at cap 0 the tier a given wall's brightest torch lands in is
/// decided by a four-slot competition that nothing pins. Two things in the game move the inputs of
/// that competition every single frame:</para>
/// <list type="bullet">
/// <item><b><c>LightFlicker</c></b> (decompiled/ThirdParty/LightFlicker.cs, 46 enabled instances on
/// the [Perf] SIM line) writes <c>lightRef.intensity = initialValue + PerlinNoise(Time.time*speed,
/// seed) * amount</c> every frame, and optionally jitters <c>transform.position</c> by up to
/// <c>locationAdjustAmount</c> WORLD UNITS. Its shipped defaults are <c>amount = 0.01</c> and
/// <c>speed = 8</c> — a 1 % wobble, far too small to read as a flicker on its own — but both are
/// PUBLIC, per-instance, prefab-authored fields, and the authored values are exactly the numbers
/// that decide whether this is the driver. Nothing in managed code reads them back, so the only way
/// to know is to measure them on hardware. That is the first job of <see cref="LogCensus"/>.</item>
/// <item><b><c>DynamicAmbience.SetLightLevel</c></b> (decompiled/GH.Runtime/DynamicAmbience.cs:150-172,
/// driven by <c>ProceduralScenario.UpdateAmbience</c> over a 0.5 s cross-fade as the view moves
/// between map tiles) writes <c>instance.intensity = template.intensity * level</c> AND
/// <c>instance.gameObject.SetActive(level &gt; 0f)</c> on CLONED lights. A doorway is precisely
/// where two rooms' ambience levels cross, and a light leaving the set re-ranks every other light
/// for every renderer near it. That is the second job of <see cref="Watch"/>.</item>
/// </list>
///
/// <para>ALTERNATIVES RULED OUT BY READING THE GAME, not by hand-waving.
/// <c>ActivateWallFadeInGame</c> only writes the global shader int <c>ToggleWallFade</c> and
/// <c>TileAnimation</c> only scrolls a material's <c>mainTextureScale/Offset</c> — neither holds,
/// reads or toggles a Light (decompiled/GH.Runtime/ActivateWallFadeInGame.cs,
/// decompiled/ThirdParty/TileAnimation.cs). <c>LightShadowsModifierController</c> DOES flip
/// <c>Light.shadows</c> scene-wide on every room-visibility change
/// (decompiled/GH.Runtime/LightShadowsModifierController.cs:45-86), but a light that is not per-pixel
/// renders no shadow map at all, so at cap 0 that path cannot produce a visible change — it is a
/// candidate for a flicker at cap ≥1, which is the opposite of the report. And nothing anywhere in
/// the decompiled tree writes <see cref="Light.renderMode"/>, so this class owns that property
/// outright and there is no write war to lose.</para>
///
/// <para>WHAT THIS CLASS DOES ABOUT IT, and why in this order.</para>
/// <list type="number">
/// <item><b>PIN THE RANKING INSTEAD OF THE LIGHTS.</b> Unity's documented rule is that an
/// <see cref="LightRenderMode.ForcePixel"/> light is rendered per-pixel regardless of the cap. So at
/// cap 0 a small chosen set can be lifted OUT of the four-slot competition entirely: they get the
/// smooth per-pixel falloff back, they stop taking part in any per-frame re-rank, and the lights
/// left behind fight over the vertex slots at much lower influence, where a swap is a small step
/// rather than a whole facet changing. The set is chosen ONCE PER SCAN (not per frame — a per-frame
/// re-rank is the disease) by influence at the head, and lights that cast no shadow are preferred,
/// because a promoted shadow-caster would quietly buy back the shadow map the cap just removed.</item>
/// <item><b>DAMP WHAT ANIMATES THE RANKING.</b> <c>LightFlicker.amount</c> is scaled ONCE per
/// instance against a recorded original and restored on release — the field is only ever READ by
/// <c>LightFlicker.Update</c>, so this is a single write, not a per-frame fight. The atmosphere
/// survives at the shipped 0.25: the torches still breathe, they just stop crossing each other's
/// rank.</item>
/// <item><b>SAY WHAT IT DID.</b> Every engage logs the census — how many flickers, their authored
/// <c>amount</c>/<c>speed</c> spread, how many jitter their position and by how much, how many
/// lights are ALREADY <c>ForcePixel</c> (those are per-pixel at cap 0 and their shadow maps are
/// still on the bill), and which lights were pinned, from which mode to which. Every release logs
/// the restore. A remedy that cannot be seen in the log carries no information, and this project has
/// paid for that lesson twice.</item>
/// </list>
///
/// <para>AND THE WATCHER IS THE PART THAT SURVIVES BEING WRONG. Whatever the remedy does, the
/// per-frame <see cref="Watch"/> pass over the recorded lights (44 entries — a few microseconds)
/// counts what ACTUALLY happens to them: activations, enable/disable flips, <c>shadows</c> changes,
/// intensity swings past 5 % of the light's own observed floor, and localPosition drift. It reports
/// the top offenders by name every <see cref="WatchReportSeconds"/> s and ONLY when something
/// happened. If the next hardware round still flickers, that line names the light and the event,
/// and this stops being a hypothesis.</para>
///
/// <para>MULTIPLAYER: local presentation only. Every write here is to a local Light or to a local
/// <c>LightFlicker</c> field; nothing is networked, nothing changes game state, and two players on
/// different settings simply see different lighting quality — the same class of difference as the
/// MSAA row.</para>
/// </summary>
internal static class LightStabiliser
{
    /// <summary>Seconds between full re-scans while engaged (rooms open and spawn new torches).</summary>
    private const float RescanSeconds = 10f;

    /// <summary>Seconds between watcher summaries. Only emitted when the window saw something.</summary>
    private const float WatchReportSeconds = 15f;

    /// <summary>An intensity move past this fraction of the light's observed floor is an EVENT.</summary>
    private const float IntensityEventFraction = 0.05f;

    /// <summary>Upper bound on <c>[Lights] PinnedPixelLights</c> — see the entry's own description.</summary>
    private const int MaxPinned = 4;

    internal static ConfigEntry<bool>? Enabled;
    internal static ConfigEntry<int>? PinnedPixelLights;
    internal static ConfigEntry<float>? FlickerDamping;

    private static bool _bound;

    /// <summary>One recorded <c>LightFlicker</c>: what we changed and what it was.</summary>
    private readonly struct FlickerRecord
    {
        internal readonly LightFlicker Flicker;
        internal readonly float OriginalAmount;

        internal FlickerRecord(LightFlicker flicker, float originalAmount)
        {
            Flicker = flicker;
            OriginalAmount = originalAmount;
        }
    }

    /// <summary>One recorded <see cref="Light"/>: what we changed, plus the watcher's state.</summary>
    private sealed class LightRecord
    {
        internal Light Light = null!;
        internal string Name = "";
        internal bool Pinned;
        internal LightRenderMode OriginalMode;

        // Watcher state — last-seen values and the running floor the 5 % test is measured against.
        internal bool LastActive;
        internal bool LastEnabled;
        internal LightShadows LastShadows;
        internal float LastIntensity;
        internal float IntensityFloor = float.MaxValue;
        internal Vector3 FirstLocalPosition;
        internal float MaxLocalDrift;

        // Watcher counters for the current report window.
        internal int ActivationEvents;
        internal int EnableEvents;
        internal int ShadowEvents;
        internal int IntensityEvents;
        internal float WorstIntensityJump;
    }

    private static readonly List<FlickerRecord> Flickers = new(64);
    private static readonly List<LightRecord> Lights = new(64);

    private static bool _engaged;
    private static float _nextRescanTime;
    private static float _nextWatchReportTime;
    private static int _watchWindows;

    /// <summary>
    /// Ride-along bind onto the rig module's own config file — the same pattern (and the same file)
    /// as <c>SkyAlternative</c>, <c>ElementMood</c>, <c>Haunt</c> and <c>EnvSound</c>. It rides
    /// <c>RenderQuality</c> because it is a property OF the per-pixel light cap: it exists to make
    /// that row's 0 usable, and a player looking for it on disk will look where the cap is.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;

        Enabled = file.Bind("Lights", "StabiliseAtZeroCap", Defaults.StabiliseAtZeroCap,
            "Stop lights STEPPING between quality tiers while the per-pixel light cap "
            + "([RenderQuality] PixelLightCount) is 0. At 0 every light in the room competes for the "
            + "same four per-vertex slots per renderer, the ranking is recomputed EVERY FRAME from "
            + "intensity and distance, and this dungeon animates 46 light intensities per frame "
            + "(the game's own LightFlicker) — so two nearly-tied torches swap rank and a whole "
            + "facet of an archway changes brightness in one frame. That is the 'komisches Flackern' "
            + "the cap's 0 brings with it. This pins a small number of lights to the per-pixel path "
            + "(see PinnedPixelLights) so they leave the competition, and damps the intensity "
            + "animation of the rest (see FlickerDamping) so the remaining ranking stops moving. "
            + "PRESENTATION ONLY and fully reversible: it writes Light.renderMode — which NOTHING in "
            + "the game ever writes, so there is no fight over it — and one public field on the "
            + "game's own flicker component, both recorded and restored. It does nothing at all "
            + "while the cap is -1 or above 0, because the boundary is quiet there. Whatever it "
            + "does, it says so once in the log ([Rig] LIGHT STABILISER), and the watcher line that "
            + "follows counts what actually happened to those lights.");

        PinnedPixelLights = file.Bind("Lights", "PinnedPixelLights", Defaults.PinnedPixelLights,
            new ConfigDescription(
                "How many lights keep the smooth PER-PIXEL falloff while the cap is 0 (0 = none, "
                + "pin nothing and rely on damping alone). Unity's forward path renders a light whose "
                + "renderMode is ForcePixel per-pixel WHATEVER the cap says, so this is the way to "
                + "spend a little of what the cap saved exactly where it is seen: the strongest "
                + "light near your head keeps its soft round falloff and stops taking part in the "
                + "per-frame ranking, while the other 40-odd stay per-vertex and free. The set is "
                + "chosen once every 10 s, never per frame — a per-frame choice is the very thing "
                + "this row exists to remove — and lights that cast no shadow are preferred, because "
                + "promoting a shadow-caster buys back a shadow map the cap had just removed. COSTS: "
                + "each pinned light is one additional forward pass for every renderer it touches, "
                + "which is the same coin the cap saves; 1 is a small, visible amount of it.",
                new AcceptableValueRange<int>(0, MaxPinned)));

        FlickerDamping = file.Bind("Lights", "FlickerDamping", Defaults.FlickerDamping,
            new ConfigDescription(
                "How much of the game's own torch-flicker amplitude survives while the stabiliser is "
                + "engaged (1 = untouched, 0 = perfectly still light). The flicker is what re-ranks "
                + "two nearly-tied lights from frame to frame, so damping it is what stops the step; "
                + "the light still breathes, it just stops crossing its neighbour. Written ONCE per "
                + "flicker component against a recorded original and restored on release — the "
                + "component only ever reads this field, so this is a single write and not a "
                + "per-frame fight. Raise it toward 1 if the torches feel dead; lower it toward 0 if "
                + "any stepping is left.",
                new AcceptableValueRange<float>(0f, 1f)));
    }

    /// <summary>
    /// Per-frame step, called from <see cref="RenderQuality.Tick"/> immediately after the cap is
    /// asserted — the stabiliser is a FUNCTION of that cap, so it must read the value the same tick
    /// wrote rather than last frame's. (It is deliberately not a seventh entry in
    /// <c>VRRigDriver._tailSteps</c>: that array is the mod's most order-sensitive list and is
    /// locked by name in <c>.planning/refactor/FRAME-ORDER.lock</c>; nesting here expresses the real
    /// dependency and leaves the locked order untouched.)
    /// </summary>
    internal static void Tick(int effectiveCap)
    {
        bool want = Enabled != null && Enabled.Value && effectiveCap == 0;

        if (!want)
        {
            if (_engaged)
                Release(effectiveCap < 0
                    ? "the per-pixel cap was released back to the game's own value"
                    : $"the per-pixel cap moved to {effectiveCap}, where the tier boundary is quiet");
            return;
        }

        if (!_engaged)
        {
            _engaged = true;
            _nextRescanTime = 0f;             // scan immediately
            _nextWatchReportTime = Time.unscaledTime + WatchReportSeconds;
            _watchWindows = 0;
        }

        if (Time.unscaledTime >= _nextRescanTime)
        {
            _nextRescanTime = Time.unscaledTime + RescanSeconds;
            using (PerfMonitor.Scope("Rig.LightStabiliser.Scan"))
                Scan();
        }

        Watch();
    }

    /// <summary>
    /// The whole-scene sweep, and the ONLY place one runs. It is on a 10 s cadence rather than a
    /// transition edge because the dungeon SPAWNS torches as rooms open
    /// (<c>ProceduralMapTile.ShowContent</c> flips whole "Generated Content" subtrees active), so a
    /// scan-once design would stabilise the entrance room and nothing after it. It carries its own
    /// PerfMonitor scope: if <c>Rig.LightStabiliser.Scan</c> ever ranks near the mod's real work on
    /// the [Perf] STEPS line, the instrument has become the thing it measures — that is the check
    /// this project learned to ship after one FindObjectOfType owned a whole frame.
    /// </summary>
    private static void Scan()
    {
        LightFlicker[] flickers;
        Light[] lights;
        try
        {
            flickers = Object.FindObjectsOfType<LightFlicker>();
            lights = Object.FindObjectsOfType<Light>();
        }
        catch (System.Exception e)
        {
            VRLog.Info("Rig", $"LIGHT STABILISER: the scene sweep threw '{e.Message}' — nothing was "
                              + "changed this cycle; it retries on the next cadence.");
            return;
        }

        int newFlickers = AdoptFlickers(flickers, out float minAmount, out float maxAmount,
            out float sumAmount, out int jitterCount, out float maxJitter, out float minSpeed,
            out float maxSpeed, out int withoutLight);
        int newLights = AdoptLights(lights, out int alreadyForcePixel, out int forcedVertex,
            out int baked, out int shadowCasters, out int forcePixelShadowCasters);
        int pinned = ApplyPinning();

        if (newFlickers == 0 && newLights == 0 && pinned == 0)
            return; // nothing moved this cycle — say nothing rather than repeat a line every 10 s

        LogCensus(flickers.Length, lights.Length, newFlickers, newLights, pinned,
            minAmount, maxAmount, sumAmount, jitterCount, maxJitter, minSpeed, maxSpeed,
            withoutLight, alreadyForcePixel, forcedVertex, baked, shadowCasters,
            forcePixelShadowCasters);
    }

    /// <summary>
    /// Record and damp every <c>LightFlicker</c> not already held, and measure the authored values
    /// on the way past. The measurement is the point: the shipped defaults are
    /// <c>amount = 0.01</c> / <c>speed = 8</c>, which is a 1 % wobble and could not read as a
    /// flicker — so if this dungeon's torches ARE the driver, it is because the prefabs authored
    /// something much larger, and that number has never been in a log.
    /// </summary>
    private static int AdoptFlickers(LightFlicker[] found, out float minAmount, out float maxAmount,
        out float sumAmount, out int jitterCount, out float maxJitter, out float minSpeed,
        out float maxSpeed, out int withoutLight)
    {
        minAmount = float.MaxValue; maxAmount = 0f; sumAmount = 0f;
        jitterCount = 0; maxJitter = 0f; withoutLight = 0;
        minSpeed = float.MaxValue; maxSpeed = 0f;

        float damping = Mathf.Clamp01(FlickerDamping!.Value);
        int adopted = 0;

        for (int i = 0; i < found.Length; i++)
        {
            LightFlicker f = found[i];
            if (f == null)
                continue;

            if (f.GetComponent<Light>() == null)
                withoutLight++;
            if (f.adjustLocation)
            {
                jitterCount++;
                maxJitter = Mathf.Max(maxJitter, f.locationAdjustAmount);
            }
            minSpeed = Mathf.Min(minSpeed, f.speed);
            maxSpeed = Mathf.Max(maxSpeed, f.speed);

            int at = IndexOfFlicker(f);
            if (at >= 0)
            {
                // Already held: its recorded amount is the AUTHORED one (that is what the census
                // reports), and the live field is re-derived from it every scan so that moving
                // [Lights] FlickerDamping is a live row rather than one that only takes effect on
                // the next engage. Re-deriving from the record — never from the live field — is
                // what stops repeated scans compounding the damping toward zero.
                float held = Flickers[at].OriginalAmount;
                f.amount = held * damping;
                minAmount = Mathf.Min(minAmount, held);
                maxAmount = Mathf.Max(maxAmount, held);
                sumAmount += held;
                continue;
            }

            float original = f.amount;
            minAmount = Mathf.Min(minAmount, original);
            maxAmount = Mathf.Max(maxAmount, original);
            sumAmount += original;
            Flickers.Add(new FlickerRecord(f, original));
            f.amount = original * damping;
            adopted++;
        }

        if (minAmount == float.MaxValue) minAmount = 0f;
        if (minSpeed == float.MaxValue) minSpeed = 0f;
        return adopted;
    }

    private static int IndexOfFlicker(LightFlicker f)
    {
        for (int i = 0; i < Flickers.Count; i++)
            if (ReferenceEquals(Flickers[i].Flicker, f))
                return i;
        return -1;
    }

    /// <summary>
    /// Record every Light not already held and count the state the [Perf] GFX census cannot see.
    ///
    /// <para>THE READING THAT MATTERS FOR PART 1: <c>alreadyForcePixel</c>. A light authored
    /// <see cref="LightRenderMode.ForcePixel"/> is per-pixel EVEN AT CAP 0 — Unity's forward rules
    /// promote Important lights before the cap is consulted — and if it also casts a shadow it still
    /// renders a shadow map. So a non-zero count here means "cap 0" is not the zero it looks like,
    /// and the [Perf] GFX line's flat "REAL-TIME SHADOW-CASTING LIGHTS: 12" (identical at every cap
    /// in the ModBuild 227 log, because it reads the authored <c>shadows</c> flag and knows nothing
    /// about the cap) would be reporting a cost that IS on the bill. Nothing else in this mod or in
    /// the game reads that distinction; this line is where it enters the record.</para>
    /// </summary>
    private static int AdoptLights(Light[] found, out int alreadyForcePixel, out int forcedVertex,
        out int baked, out int shadowCasters, out int forcePixelShadowCasters)
    {
        alreadyForcePixel = 0; forcedVertex = 0; baked = 0;
        shadowCasters = 0; forcePixelShadowCasters = 0;
        int adopted = 0;

        for (int i = 0; i < found.Length; i++)
        {
            Light l = found[i];
            if (l == null)
                continue;

            bool isBaked = l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked;
            if (isBaked) baked++;
            else if (l.renderMode == LightRenderMode.ForceVertex) forcedVertex++;
            else if (l.renderMode == LightRenderMode.ForcePixel) alreadyForcePixel++;

            if (!isBaked && l.shadows != LightShadows.None)
            {
                shadowCasters++;
                if (l.renderMode == LightRenderMode.ForcePixel)
                    forcePixelShadowCasters++;
            }

            if (IndexOfLight(l) >= 0)
                continue;

            Transform t = l.transform;
            Lights.Add(new LightRecord
            {
                Light = l,
                Name = l.name,
                Pinned = false,
                OriginalMode = l.renderMode,
                LastActive = l.gameObject.activeInHierarchy,
                LastEnabled = l.enabled,
                LastShadows = l.shadows,
                LastIntensity = l.intensity,
                IntensityFloor = l.intensity,
                FirstLocalPosition = t.localPosition,
                MaxLocalDrift = 0f,
            });
            adopted++;
        }

        // Drop destroyed entries so the watcher's per-frame walk stays the size of the scene.
        for (int i = Lights.Count - 1; i >= 0; i--)
            if (Lights[i].Light == null)
                Lights.RemoveAt(i);
        for (int i = Flickers.Count - 1; i >= 0; i--)
            if (Flickers[i].Flicker == null)
                Flickers.RemoveAt(i);

        return adopted;
    }

    private static int IndexOfLight(Light l)
    {
        for (int i = 0; i < Lights.Count; i++)
            if (ReferenceEquals(Lights[i].Light, l))
                return i;
        return -1;
    }

    /// <summary>
    /// Choose the pinned set and write <see cref="LightRenderMode.ForcePixel"/> onto it.
    ///
    /// <para>THE RANK IS COMPUTED HERE AND NOWHERE ELSE — once per 10 s scan. Recomputing it per
    /// frame would reintroduce exactly the instability this class removes, only with the mod's name
    /// on it. The proxy is Unity's own shape (intensity x range², attenuated by the squared distance
    /// to the head) rather than the raw intensity, because a bright light two rooms away is not the
    /// one whose falloff the player is looking at; a light with no head to measure against falls
    /// back to <c>intensity x range</c>, which is stable and view-independent.</para>
    ///
    /// <para>SHADOW-CASTERS ARE DEPRIORITISED, not excluded. Promoting one gives its shadow map back
    /// — the second of the two multiplications the cap removes — so it is chosen last and the log
    /// says when it happened. Excluding them outright would be worse: in a room lit only by
    /// shadow-casting spots it would silently pin nothing at all, which is the "gated remedy never
    /// ran" shape.</para>
    /// </summary>
    private static int ApplyPinning()
    {
        int want = Mathf.Clamp(PinnedPixelLights!.Value, 0, MaxPinned);

        Vector3 head = Vector3.zero;
        bool haveHead = false;
        Camera? cam = VRRigDriver.HeadCamera;
        if (cam != null)
        {
            head = cam.transform.position;
            haveHead = true;
        }

        // Score every candidate. Baked and ForceVertex lights are not candidates: the first is
        // already in the lightmap, the second opted out of the pixel path itself.
        var scored = new List<(float score, LightRecord rec)>(Lights.Count);
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            Light l = r.Light;
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy)
                continue;
            if (l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                continue;
            if (r.OriginalMode == LightRenderMode.ForceVertex)
                continue;

            float reach = Mathf.Max(l.range, 0.001f);
            float score = haveHead
                ? l.intensity * reach * reach
                  / Mathf.Max((l.transform.position - head).sqrMagnitude, 0.001f)
                : l.intensity * reach;
            if (l.shadows != LightShadows.None)
                score *= 0.25f;   // deprioritised, not excluded — see the doc above
            scored.Add((score, r));
        }
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        int changed = 0;
        int pinnedSoFar = 0;
        for (int i = 0; i < scored.Count; i++)
        {
            LightRecord r = scored[i].rec;
            bool shouldPin = pinnedSoFar < want;
            if (shouldPin)
                pinnedSoFar++;

            if (shouldPin && !r.Pinned)
            {
                r.Pinned = true;
                r.Light.renderMode = LightRenderMode.ForcePixel;
                changed++;
            }
            else if (!shouldPin && r.Pinned)
            {
                r.Pinned = false;
                r.Light.renderMode = r.OriginalMode;
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// The one line that makes the whole thing attributable. Everything in it is a number nobody has
    /// had before: the authored flicker amplitudes, the positional jitter, and how many lights are
    /// per-pixel at cap 0 whatever the cap says.
    /// </summary>
    private static void LogCensus(int flickerCount, int lightCount, int newFlickers, int newLights,
        int pinned, float minAmount, float maxAmount, float sumAmount, int jitterCount,
        float maxJitter, float minSpeed, float maxSpeed, int withoutLight, int alreadyForcePixel,
        int forcedVertex, int baked, int shadowCasters, int forcePixelShadowCasters)
    {
        float damping = Mathf.Clamp01(FlickerDamping!.Value);
        float meanAmount = flickerCount > 0 ? sumAmount / flickerCount : 0f;

        var sb = new StringBuilder(1400);
        sb.Append("LIGHT STABILISER engaged at per-pixel cap 0 — ")
          .Append(newFlickers).Append(" newly damped flicker(s) (holding ").Append(Flickers.Count)
          .Append(" of ").Append(flickerCount).Append(" in the scene) and ").Append(newLights)
          .Append(" newly recorded light(s) (holding ").Append(Lights.Count).Append(" of ")
          .Append(lightCount).Append("); ").Append(pinned).Append(" pin change(s) this cycle. ");

        sb.Append("FLICKER CENSUS — the numbers that decide whether LightFlicker is the driver at "
                  + "all: authored amount min ").Append(minAmount.ToString("F4"))
          .Append(" / mean ").Append(meanAmount.ToString("F4"))
          .Append(" / max ").Append(maxAmount.ToString("F4"))
          .Append(", speed ").Append(minSpeed.ToString("F1")).Append("..").Append(maxSpeed.ToString("F1"))
          .Append(". The component's SHIPPED defaults are amount 0.0100 and speed 8.0, which is a 1% "
                  + "wobble and could not read as a step — so read the max above: if it is near 0.01 "
                  + "this component is NOT what is flickering and the watcher line below is the "
                  + "place to look instead. ")
          .Append(jitterCount).Append(" of them also JITTER THE LIGHT'S WORLD POSITION (adjustLocation), "
                  + "worst locationAdjustAmount ").Append(maxJitter.ToString("F3"))
          .Append(" WORLD UNITS — the component writes transform.position = a position captured in "
                  + "Start() plus up to that much noise, so at this diorama's scale a non-zero figure "
                  + "here moves a torch by a visible fraction of a wall every frame and re-ranks it "
                  + "against its neighbours by DISTANCE as well as by intensity. ")
          .Append(withoutLight).Append(" flicker(s) carry no Light at all and only animate a mesh. ");

        sb.Append("LIGHT CENSUS at cap 0: ").Append(lightCount).Append(" enabled light(s); ")
          .Append(baked).Append(" baked, ").Append(forcedVertex).Append(" ForceVertex, ")
          .Append(alreadyForcePixel).Append(" ForcePixel. ");
        if (alreadyForcePixel > 0)
        {
            sb.Append("THAT ForcePixel COUNT IS NOT ZERO, AND IT MATTERS: Unity's forward path "
                      + "promotes an Important light per-pixel BEFORE it consults pixelLightCount, "
                      + "so those ").Append(alreadyForcePixel)
              .Append(" are still per-pixel at cap 0 and still cost one extra pass per renderer they "
                      + "touch — ").Append(forcePixelShadowCasters)
              .Append(" of them also still render a shadow map. 'Cap 0' is therefore not the zero it "
                      + "looks like, and the [Perf] GFX line cannot see this because it reads the "
                      + "authored shadows flag and knows nothing about the cap. ");
        }
        else
        {
            sb.Append("No light is authored ForcePixel, so cap 0 really is zero per-pixel lights and "
                      + "zero shadow maps: the ").Append(shadowCasters)
              .Append(" light(s) the [Perf] GFX line calls REAL-TIME SHADOW-CASTING are counted from "
                      + "their authored shadows flag and are NOT on the bill at this cap. ");
        }

        sb.Append("REMEDY: flicker amplitude scaled to ").Append((damping * 100f).ToString("F0"))
          .Append("% of authored on ").Append(Flickers.Count).Append(" component(s)");
        int pinnedNow = 0;
        string pinnedNames = "";
        for (int i = 0; i < Lights.Count; i++)
        {
            if (!Lights[i].Pinned) continue;
            pinnedNow++;
            pinnedNames += (pinnedNames.Length == 0 ? "" : ", ") + "'" + Lights[i].Name + "' "
                           + Lights[i].OriginalMode + "→ForcePixel"
                           + (Lights[i].Light != null && Lights[i].Light.shadows != LightShadows.None
                               ? " (CASTS SHADOWS — this one buys a shadow map back)" : "");
        }
        sb.Append("; ").Append(pinnedNow).Append(" light(s) pinned to the per-pixel path")
          .Append(pinnedNow > 0 ? ": " + pinnedNames : " (PinnedPixelLights is 0)")
          .Append(". Everything here is recorded and restored when the cap leaves 0 or the rig tears "
                  + "down — grep '[Rig] LIGHT STABILISER released'.");

        VRLog.Info("Rig", sb.ToString());
    }

    /// <summary>
    /// Per-frame pass over the recorded lights (tens of entries — a few microseconds). It changes
    /// NOTHING; it only counts the events that could produce a local step, so that the next hardware
    /// round can name one instead of guessing. This is deliberately the part of the class that keeps
    /// working if the remedy above is aimed at the wrong mechanism.
    /// </summary>
    private static void Watch()
    {
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            Light l = r.Light;
            if (l == null)
                continue;

            bool active = l.gameObject.activeInHierarchy;
            if (active != r.LastActive) { r.ActivationEvents++; r.LastActive = active; }

            bool on = l.enabled;
            if (on != r.LastEnabled) { r.EnableEvents++; r.LastEnabled = on; }

            LightShadows sh = l.shadows;
            if (sh != r.LastShadows) { r.ShadowEvents++; r.LastShadows = sh; }

            float intensity = l.intensity;
            if (intensity < r.IntensityFloor)
                r.IntensityFloor = intensity;
            float jump = Mathf.Abs(intensity - r.LastIntensity);
            float reference = Mathf.Max(r.IntensityFloor, 0.001f);
            if (jump > reference * IntensityEventFraction)
            {
                r.IntensityEvents++;
                r.WorstIntensityJump = Mathf.Max(r.WorstIntensityJump, jump / reference);
            }
            r.LastIntensity = intensity;

            float drift = (l.transform.localPosition - r.FirstLocalPosition).magnitude;
            if (drift > r.MaxLocalDrift)
                r.MaxLocalDrift = drift;
        }

        if (Time.unscaledTime < _nextWatchReportTime)
            return;
        _nextWatchReportTime = Time.unscaledTime + WatchReportSeconds;
        ReportWatch();
    }

    private static void ReportWatch()
    {
        int totalActivation = 0, totalEnable = 0, totalShadow = 0, totalIntensity = 0;
        float worstJump = 0f, worstDrift = 0f;
        LightRecord? worstJumper = null;
        LightRecord? worstDrifter = null;

        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            totalActivation += r.ActivationEvents;
            totalEnable += r.EnableEvents;
            totalShadow += r.ShadowEvents;
            totalIntensity += r.IntensityEvents;
            if (r.WorstIntensityJump > worstJump) { worstJump = r.WorstIntensityJump; worstJumper = r; }
            if (r.MaxLocalDrift > worstDrift) { worstDrift = r.MaxLocalDrift; worstDrifter = r; }
        }

        _watchWindows++;
        bool quiet = totalActivation == 0 && totalEnable == 0 && totalShadow == 0
                     && totalIntensity == 0 && worstDrift < 0.0005f;

        if (!quiet)
        {
            VRLog.Info("Rig", $"LIGHT WATCH ({WatchReportSeconds:F0}s window {_watchWindows}, "
                              + $"{Lights.Count} light(s) watched at per-pixel cap 0): "
                              + $"{totalActivation} GameObject activation flip(s), {totalEnable} "
                              + $"Light.enabled flip(s), {totalShadow} shadows-flag change(s), "
                              + $"{totalIntensity} intensity move(s) past {IntensityEventFraction:P0} "
                              + $"of the light's own observed floor (worst "
                              + $"{worstJump:P0} on '{worstJumper?.Name ?? "-"}'), worst localPosition "
                              + $"drift {worstDrift:F4} world units on '{worstDrifter?.Name ?? "-"}'. "
                              + "READ IT LIKE THIS: activation or enabled flips are DynamicAmbience's "
                              + "room cross-fade (it calls SetActive(level>0) on cloned lights) or "
                              + "ProceduralMapTile.ShowContent turning a tile's props on and off — "
                              + "those step the ranking of every renderer near them and are the "
                              + "prime suspect for a doorway. A big intensity figure with no flips "
                              + "is LightFlicker, and the census line above says whether its "
                              + "amplitude is large enough to matter. A growing drift means a light "
                              + "is being written an absolute WORLD position that no longer matches "
                              + "its prop. If EVERY number here is small and the archway still "
                              + "flickers, the cause is not the lights' state at all and the next "
                              + "round should read the RENDERER side instead.");
        }
        else if (_watchWindows == 1)
        {
            VRLog.Info("Rig", $"LIGHT WATCH ({WatchReportSeconds:F0}s window 1, {Lights.Count} "
                              + "light(s) watched at per-pixel cap 0): NOTHING HAPPENED — no "
                              + "activation, enable or shadow flips, no intensity move past "
                              + $"{IntensityEventFraction:P0} of any light's own floor, no position "
                              + "drift. This line is printed for the first quiet window ONLY, so its "
                              + "absence later means 'still quiet', not 'not running'. A quiet "
                              + "watcher plus a live flicker report is itself a finding: it "
                              + "falsifies every light-state mechanism and points at the renderer "
                              + "side (per-object light-list churn that leaves no trace on the Light "
                              + "itself, or geometry/material work) instead.");
        }

        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            r.ActivationEvents = 0; r.EnableEvents = 0; r.ShadowEvents = 0; r.IntensityEvents = 0;
            r.WorstIntensityJump = 0f;
        }
    }

    /// <summary>
    /// Hand everything back. Called when the cap leaves 0, when the config row is switched off, on
    /// scene change and from <c>VRRigDriver.TearDownRig</c>/<c>OnDestroy</c>. Every mutation this
    /// class makes has to be reversible — the same rule that made <c>ApplyPixelLights</c>'s -1
    /// RESTORE rather than merely stop.
    /// </summary>
    internal static void Release(string why)
    {
        if (!_engaged && Flickers.Count == 0 && Lights.Count == 0)
            return;

        int restoredFlickers = 0;
        for (int i = 0; i < Flickers.Count; i++)
        {
            LightFlicker f = Flickers[i].Flicker;
            if (f == null)
                continue;
            f.amount = Flickers[i].OriginalAmount;
            restoredFlickers++;
        }

        int unpinned = 0;
        for (int i = 0; i < Lights.Count; i++)
        {
            LightRecord r = Lights[i];
            if (!r.Pinned || r.Light == null)
                continue;
            r.Light.renderMode = r.OriginalMode;
            unpinned++;
        }

        int held = Lights.Count;
        Flickers.Clear();
        Lights.Clear();
        _engaged = false;

        VRLog.Info("Rig", $"LIGHT STABILISER released — {why}. Restored the authored flicker "
                          + $"amplitude on {restoredFlickers} component(s) and the original "
                          + $"renderMode on {unpinned} pinned light(s); dropped {held} watched "
                          + "light(s). Nothing of this class's is left on the scene.");
    }
}
