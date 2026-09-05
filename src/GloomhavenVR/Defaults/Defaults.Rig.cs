// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Rig;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Rig/ComfortSettings.cs ----------------------------------------------------
    internal const bool WorldGrabEnabled = true;                    // => [Comfort] WorldGrabEnabled
    internal const bool FreeMovement = true;                        // => [Comfort] FreeMovement
    internal const bool VerticalDrag = false;                       // => [Comfort] VerticalDrag
    internal const bool RotateEnabled = true;                       // => [Comfort] RotateEnabled
    internal const bool ScaleEnabled = true;                        // => [Comfort] ScaleEnabled
    internal const float ScaleMin = 0.5f;                           // => [Comfort] ScaleMin
    internal const float ScaleMax = 4f;                             // => [Comfort] ScaleMax
    internal const TurnMode Comfort_TurnMode = TurnMode.Smooth;     // => [Comfort] TurnMode
    internal const float SnapTurnDegrees = 45f;                     // => [Comfort] SnapTurnDegrees
    internal const float SmoothTurnSpeed = 90f;                     // => [Comfort] SmoothTurnSpeed
    internal const TurnHandChoice TurnHand = TurnHandChoice.Right;  // => [Comfort] TurnHand
    internal const bool FlightEnabled = true;                       // => [Comfort] FlightEnabled
    internal const FlightDirectionSource FlightDirection = FlightDirectionSource.Head;  // => [Comfort] FlightDirection
    internal const float FlightMaxSpeed = 1.27367f;                 // => [Comfort] FlightMaxSpeed
    internal const TurnHandChoice FlightHand = TurnHandChoice.Left;   // => [Comfort] FlightHand
    internal const bool TurnStickVertical = false;                  // => [Comfort] TurnStickVertical  (pinned: the user asked for it as an OPTION, and its off state is the pre-feature behaviour)
    // ---- the laser-carry reel (user request 2026-08-23) — it OVERRULES the two rows above ----
    //
    // ON, and that is the request rather than an opinion: "Falls sie noch nicht existiert,
    // implementiere sie" asks for the behaviour, and "als Option an/ausschaltbar" asks only that
    // the switch exist. Contrast TurnStickVertical directly above, which is pinned OFF because
    // there the user asked to be able to SET it and its off state is the pre-feature behaviour.
    // While a laser carry holds a hand, this takes that hand's stick Y and BOTH flight rows stand
    // down for the duration — see Flight.cs, where each stand-down names itself in the log rather
    // than going quiet, because a silent suppression is indistinguishable from a broken stick.
    internal const bool LaserCarryReel = true;                       // => [Comfort] LaserCarryReel
    // 2.0 apparent m/s at full deflection: a window grabbed at a typical 3-4 m reaches arm's
    // length in under two seconds, and the linear (not squared) response means half a stick is
    // half that. APPARENT metres, converted through the live rig scale at use time, so it feels
    // the same at the 198x map room and the 4.4x scenario — a bound written in world units would
    // be 45x wrong in one of the two, which this project has shipped before. Untuned: the first
    // hardware round is expected to move it, which is what a dial with a declared range is for.
    internal const float LaserCarryReelSpeed = 2f;                   // => [Comfort] LaserCarryReelSpeed
    // [Comfort] TableHeightOffset is GONE (user ruling 2026-08: free locomotion replaced it).
    // No line here on purpose — a tuned cfg that still carries the key is reported as UNMAPPED
    // by scripts/rebase-defaults.py, which is exactly right for a retired key.
    internal const float RecenterHoldSeconds = 1.0f;                // => [Comfort] RecenterHoldSeconds
    internal const float SavedScaleMultiplier = 1.6092f;            // => [Comfort] SavedScaleMultiplier
    internal const bool DebugGizmos = false;                        // => [Comfort] DebugGizmos
    internal const bool KeepPlaceOnReorigin = true;                 // => [Comfort] KeepPlaceOnReorigin
    internal const bool TableScaleDefault25Applied = false;         // => [Comfort] TableScaleDefault25Applied  (pinned: one-shot migration marker — a fresh install must start false)

    // ---- Rig/RenderQuality.cs ------------------------------------------------------
    internal const int MsaaLevel = 4;                    // => [RenderQuality] MsaaLevel
    internal const bool ForceAnisotropic = true;         // => [RenderQuality] ForceAnisotropic
    internal const bool ForceFullTextureResolution = true; // => [RenderQuality] ForceFullTextureResolution
    // ON, and it is the answer to "the HIGHER game preset looks worse" (user, 2026-08-23: "Die
    // matschigen Texturen verschwinden, wenn ich in den Spiel-Grafik-Einstellungen 'Schön' statt
    // 'Fantastisch' einstelle"). The ModBuild 228 log correlates the game's own SetQualityLeve line
    // with [Perf] TEX three times: Fantastic ⇒ streamingMipmaps=True (900MB, maxLevelReduction=2),
    // Beautiful ⇒ False — and while it was True, EVERY streamed texture in view read below its
    // desired mip level (47 of 47 in one window). Non-worsening for the same reason the mip-drop
    // force above is: it costs VRAM and never frame time. See RenderQuality.ApplyTextureStreaming.
    internal const bool ForceTextureStreamingOff = true;  // => [RenderQuality] ForceTextureStreamingOff
    // Only consulted while the row above is FALSE, i.e. when a player hands the decision back to the
    // game. 4096 MB against the game's authored 900: enough headroom that the streaming system stops
    // being the constraint on a card that has the memory (this is reported from a 24 GB one), and
    // still a raise-only — the game keeps any larger budget it asks for itself.
    internal const int TextureStreamingBudgetMB = 4096;   // => [RenderQuality] TextureStreamingBudgetMB
    internal const float EyeResolutionScale = 1.0f;      // => [RenderQuality] EyeResolutionScale
    // [RenderQuality] ViewportScaleFallback and RebuildRigOnMsaaChange had their lines here. Both
    // were UNBOUND by the 2026-08-22 settings audit — the fallback is now the constant
    // RenderQuality.ViewportScaleFallback, the rebuild path is deleted. See RenderQuality for why
    // neither was ever a choice a player could hold.
    // 0 SINCE 2026-08-23, and this line used to say "-1 STAYS THE SHIPPED VALUE". WHAT CHANGED IS A
    // USER RULING, verbatim: "Die Pixellichter option ist zu gefährlich für normale Nutzer, sie
    // sollte in Erweitert verschwinden und per default auch in allen Graphik-Voreinstellungen auf 0
    // geschaltet sein." So this is now an ACTIVE cap on every install rather than the passive "leave
    // the game alone" it was — the mod caps per-pixel lights at 0 out of the box, all four presets
    // in RenderQuality.Presets carry the same 0, and the row itself is off the curated Bild page
    // (VROptionsTab.4.Curated) and reachable only through Erweitert.
    //
    // The argument the old comment made against moving it — that flatter point-light falloff is a
    // visible change a default may not make silently — is not refuted, it is OVERRULED: it is the
    // strongest single performance lever the mod has (forward-path draw multiplication AND the whole
    // shadow pipeline, see RenderQuality.ApplyPixelLights), the user has reported its effect from the
    // headset, and the look it costs is covered by [Lights] StabiliseAtZeroCap, which is on by
    // default and is gated on exactly this value being 0. -1 remains available as the escape hatch.
    // PINNED, and this is the one thing about the line below that is machine-readable: the tuned cfg
    // snapshot still carries the OLD -1, because it was dumped from a session that ran the old
    // default — so an unmarked line would make scripts/rebase-defaults.py rebase the ruling straight
    // back out of the source at the next drop. The marker says "the cfg is the older statement here".
    internal const int PixelLightCount = 0;              // => [RenderQuality] PixelLightCount  (pinned: user ruling 2026-08-23 — 0 in every preset and as the shipped default; the cfg snapshot predates it)
    // THE VALUE IS UNCHANGED AND MUST STAY 4, and the reason it is 4 has outlived the thing it
    // described. It meant "Eigene": the honest reading of the three rows above after the 2026-08-23
    // cfg rebase took the user's own tuned values ("Übernehme bitte die in debug/default liegenden
    // Default werte von mir"). They spell MSAA 4x, eye 1.00x and a per-pixel light cap of 0, which
    // matched NO preset — "Ausgewogen" was 4x but at eye 0.90x — so the derived index was the
    // custom one.
    //
    // THE PRESETS ARE GONE (2026-09-05, user ruling: "Entferne die Graphik-Profile wieder in den
    // VR-Einstellungen, die mag ich nicht."). [RenderQuality] QualityPreset is RETIRED at its bind
    // and INERT: the preset table, the derived index, the apply path and the per-tick mirror that
    // used to overwrite this number are all deleted (Rig/RenderQuality.cs), and no code reads the
    // entry. The key is still bound, so this default is still what a fresh cfg gets — which is the
    // ONLY reason the number is still here, and why it must not be "tidied" to 0: changing it would
    // rewrite a value in every new install's config file for a setting that does nothing, and this
    // round moves rows, it does not tune. There is nothing left to derive it from either, so if the
    // key is ever unbound, delete this line rather than re-deriving it.
    internal const int QualityPreset = 4;                // => [RenderQuality] QualityPreset (RETIRED, inert)

    // ---- Rig/LightStabiliser.cs ----------------------------------------------------
    // ON by default, and SINCE 2026-08-23 IT IS ALSO ACTIVE BY DEFAULT: the whole class is gated on
    // the effective per-pixel cap being exactly 0, and [RenderQuality] PixelLightCount now ships at
    // 0 for every install (see its own note above for the ruling). Until this build the gate was
    // only opened by the "Leistung"/"Schwache Hardware" presets or a hand-set row, so this entry
    // cost nothing on a fresh install; it is now on the everyday path and its cost is the cost of
    // the cap being usable. It exists so that 0 is a usable choice — the user's report is that 0 is
    // the setting he WANTS and the flicker is what stops him.
    internal const bool StabiliseAtZeroCap = true;       // => [Lights] StabiliseAtZeroCap
    // 1, not 0: pinning ONE light keeps a smooth per-pixel falloff exactly where the player is
    // looking for the price of one extra forward pass over the renderers that one light touches —
    // a rounding error against the 40-odd passes the cap just removed. 0 is the honest "pin
    // nothing" if even that is too much; the row goes to 4.
    internal const int PinnedPixelLights = 0;            // => [Lights] PinnedPixelLights
    // 0.0 — PERFECTLY STEADY, and the reason is a census plus a user report. ModBuild 228 shipped
    // 0.25 as "a quarter of the wobble survives"; the user's verdict was "hat schon richtig viel
    // gebracht, das meiste Flackern ist nun weg. An manchen Stellen ist es immer noch." What is left
    // is the four-slot re-rank, and any surviving wobble is what crosses the tie. The atmosphere
    // does not pay for it: that build's own census counted 46 LightFlicker components of which 34
    // carry NO Light at all and animate only a MESH, so the fire keeps moving with every light held
    // absolutely still. And this row's meaning changed with the mechanism — it no longer scales one
    // component's amplitude field, it is the fraction of each frame's deviation from a rolling
    // average that survives into the rendered value, so a genuine slow change still arrives (see
    // StabiliserResponseSeconds) and only the shake is removed. It has to be the safe value because
    // [RenderQuality] PixelLightCount is 0 by default in this same build: this path is now what
    // every player sees.
    internal const float FlickerDamping = 0f;            // => [Lights] FlickerDamping
    // 0.75 s: comfortably longer than any per-frame flicker (LightFlicker runs at speed 0.5..5.0 and
    // FireLight at Time.time, i.e. periods well under 0.5 s) and comfortably shorter than the 0.5 s
    // room cross-fade DynamicAmbience.SetLightLevel has to pass through — a genuine lighting change
    // arrives about one time constant late and smoothly, a shake never arrives at all. Bounds
    // 0.1..5.0. NOT VERIFIED ON HARDWARE: this is the value to move if the next round says lighting
    // changes feel sluggish (lower) or a slow change still steps (raise).
    internal const float StabiliserResponseSeconds = 0.75f; // => [Lights] StabiliserResponseSeconds
}
