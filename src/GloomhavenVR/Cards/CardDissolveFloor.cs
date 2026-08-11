using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 11 (b) — THE DISSOLVE EPSILON FLOOR: the shader-side fix for the black card frame.
///
/// <para>WHY A FLOOR, AFTER TEN TEXTURE-SIDE ROUNDS. Nine rounds punched the printed frame to
/// alpha 0 on mod-owned sprite copies and the user saw an identical band every time; the tenth
/// (ModBuild 114) provably shrank the face rects and the report was still "unverändert". The one
/// mechanism that explains every round at once lives in the shader: each card-face Image renders
/// through a per-image CLONE of 'GUI_CardEffect_Mat' / 'GUI/AbilityCard_Shd'
/// (<c>CardEffects.Awake</c>, decompiled/GH.Runtime/CardEffects.cs:336 — and its siblings
/// <c>ItemCardEffects</c>/<c>MiniCardEffects</c> clone the same way), and the user's own
/// observation is the smoking gun: during the character-switch DISSOLVE animation the black band
/// briefly turns TRANSPARENT. That is the signature of a clip of the shape
/// <c>clip(f(tex.a) - _Dissolve·…)</c> — it discards NOTHING at <c>_Dissolve = 0</c> and paints
/// alpha-0 pixels as opaque black, while any positive dissolve starts discarding exactly the
/// lowest-alpha pixels first. <see cref="CardShaderProbe"/> proves or refutes that reading at
/// runtime; THIS class acts on it.</para>
///
/// <para>WHAT IT DOES: once per frame, for every card-FX material instance under a face the mod
/// manages, raise <c>_Dissolve</c> to a tiny epsilon when — and only when — it is BELOW that
/// epsilon. The game's own FX write ≥ 0.6-ish while animating and 0 at rest
/// (<c>CardEffects.RestoreCard</c>), so an animated value is never lowered and never fought: the
/// floor only replaces the rest-state 0, every frame, because <c>RestoreCard</c> and the FX
/// timelines rewrite it at will.</para>
///
/// <para>WHY THE EPSILON IS SAFE ON EVERY VERDICT: the punched pixels are BINARY alpha (0 or ~1 —
/// see CardFaceMipBake's punch), so a clip at 0.004 cannot carve visible edge artifacts out of
/// opaque art; 0.004 is far below any soft-edge fringe that matters. If the probe's verdict says
/// the shader honors alpha at rest, alpha-0 pixels are already invisible and the floor changes
/// nothing; if it says the epsilon does not discard either, the floor does nothing; only in the
/// middle case does it act — and there it removes the band. It is therefore UNCONDITIONAL (never
/// gated on the probe) and dial-controlled only: [Cards] DissolveFloorFraction, 0 disables.</para>
///
/// <para>SCOPE (user's standing requirement, verbatim: "Der schwarze Rand soll im gesamten Spiel
/// entfernt werden egal wo die Karte ist - ob in einem Fächer, auf der Hand oder auf dem
/// Controlboard liegend. … Weiterhin sollien Item-Karten genauso betroffen sein. UND auch alle
/// Karten genauso die remote angezeigt werden im Multiplayer bei anderen Spielern"): one instance
/// of this class rides every per-frame face seam the mod already owns — the adopted ability face
/// (<c>CardFace.Maintain</c>), the hosted item card (<c>ItemsPile.ItemChip.TickFaceMaintenance</c>)
/// and the remote peers' card clones (whose per-frame seam calls in from Net code that already
/// registers with the card system). Selection is by MATERIAL SIGNATURE, not component type: a
/// material that carries both <c>_Dissolve</c> and <c>_PosAndBounds</c> is the card-FX family
/// (CardEffects, ItemCardEffects and MiniCardEffects animate exactly that pair), which covers
/// item cards and any clone hierarchy without ever caring who built it.</para>
///
/// <para>GEOMETRY RULING (user, verbatim: "Ich möchte gerne an den aktuellen Proportionen
/// festehalten. Ich will es also so wie es jetzt ist und sich verhält - nur eben ohne die
/// schwarzen Ränder.") — the floor touches ONE float on materials and no transform, rect, sprite
/// or scale, and it works WITH the existing punch (without alpha-0 pixels there is nothing for
/// the clip to discard), so the punch/crop machinery stays exactly as shipped.</para>
///
/// <para>COST: the capture (the one allocating <c>GetComponentsInChildren</c> walk) rides the
/// owner's EXISTING capture seams — adoption/host plus the 1 s rescan cadence — and the per-frame
/// <see cref="Tick"/> is a plain indexed loop over cached material references: one
/// <c>GetFloat</c> per card-FX material per frame, no allocation, no scene queries.</para>
/// </summary>
internal sealed class CardDissolveFloor
{
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");

    /// <summary>Error latch: one warning per instance, then the instance stays inert until the
    /// next capture (a failing material must not warn at frame rate).</summary>
    private bool _errorLogged;

    /// <summary>The card-FX material instances under the captured root. References, not Images:
    /// the game reassigns SPRITES at will but the per-image material clone lives as long as the
    /// pooled widget, and the owner's rescan cadence re-captures whenever the hierarchy grows. A
    /// stale reference is harmlessly re-floored; a destroyed one is skipped by Unity's
    /// fake-null.</summary>
    private readonly List<Material> _mats = new(12);

    /// <summary>The epsilon this instance last wrote — so <see cref="Release"/> can tell "our
    /// floor" from a game-written animation value when handing the face back.</summary>
    private float _appliedEpsilon;

    /// <summary>The dial, resolved defensively (config binds after plugin init).</summary>
    private static float Epsilon =>
        CardsConfig.DissolveFloorFraction != null ? CardsConfig.DissolveFloorFraction.Value : 0f;

    /// <summary>Is <paramref name="m"/> in the card-FX material family? Signature match on the
    /// two properties every family member animates (<c>_Dissolve</c> + <c>_PosAndBounds</c>) —
    /// shader-identity based, never component-type based, so item-card and clone hierarchies
    /// qualify by what they RENDER with.</summary>
    private static bool IsCardFxMaterial(Material? m) =>
        m != null && m.HasProperty(DissolveId) && m.HasProperty(PosAndBoundsId);

    /// <summary>
    /// (Re)collect the card-FX materials under <paramref name="root"/>. Call where the owner
    /// already re-captures its art watch / re-runs its mip rescan (adoption, host, the 1 s
    /// cadence) — this is the only allocating step, and a hierarchy the game grew after the last
    /// capture is picked up by the same cadence that already covers that case for sprites.
    /// </summary>
    internal void Capture(Component? root)
    {
        if (root == null)
        {
            _mats.Clear();
            _errorLogged = false;
            return;
        }
        try
        {
            Capture(root.GetComponentsInChildren<Image>(includeInactive: true));
        }
        catch (System.Exception ex)
        {
            _mats.Clear();
            VRLog.Warn("Cards", "Dissolve-floor capture failed " +
                                $"({ex.GetType().Name}: {ex.Message}) — this face keeps the " +
                                "game's rest-state dissolve until the next capture cadence.");
        }
    }

    /// <summary>
    /// The non-allocating half of <see cref="Capture(Component?)"/>, for an owner that already
    /// holds the face's <c>Image</c> array (<see cref="CardArtWatch"/> captures one anyway — the
    /// floor must not walk the same hierarchy twice on the same cadence).
    /// </summary>
    internal void Capture(Image[]? images)
    {
        _mats.Clear();
        _errorLogged = false;
        if (images == null)
            return;
        try
        {
            foreach (Image img in images)
            {
                if (img == null)
                    continue;
                // img.material is the assigned material — for a card-FX image that is the
                // per-image clone CardEffects/ItemCardEffects created in Awake, i.e. the very
                // object the game's own FX timelines SetFloat on. Writing the same object is
                // what makes the floor and the animations compose instead of fight.
                Material? m = img.material;
                if (!IsCardFxMaterial(m) || _mats.Contains(m!))
                    continue;
                _mats.Add(m!);
            }
        }
        catch (System.Exception ex)
        {
            _mats.Clear();
            VRLog.Warn("Cards", "Dissolve-floor capture failed " +
                                $"({ex.GetType().Name}: {ex.Message}) — this face keeps the " +
                                "game's rest-state dissolve until the next capture cadence.");
        }
    }

    /// <summary>
    /// One frame's worth of flooring: raise every captured material's <c>_Dissolve</c> to the
    /// epsilon when it sits below it. Never lowers — a game FX timeline writing ≥ epsilon is left
    /// untouched, and the moment it rests back to 0 the next frame re-floors it. O(captured
    /// materials), allocation-free.
    /// </summary>
    internal void Tick()
    {
        float eps = Epsilon;
        if (eps <= 0f || _mats.Count == 0)
            return;
        try
        {
            for (int i = 0; i < _mats.Count; i++)
            {
                Material m = _mats[i];
                if (m == null)
                    continue; // destroyed with its pooled widget — dropped on the next capture
                if (m.GetFloat(DissolveId) < eps)
                    m.SetFloat(DissolveId, eps);
            }
            _appliedEpsilon = eps;
        }
        catch (System.Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                VRLog.Warn("Cards", "Dissolve-floor tick failed " +
                                    $"({ex.GetType().Name}: {ex.Message}) — this face keeps the " +
                                    "game's rest-state dissolve until the next capture.");
            }
            _mats.Clear();
        }
    }

    /// <summary>
    /// Full-restore contract (the same one the crop and the blackout honor): when the face stops
    /// being ours — yielded to a game dialog, returned to the object pool — hand back the game's
    /// own rest state. Only a value that IS our floor (≤ the last epsilon we wrote, and positive)
    /// is zeroed; an in-flight FX value is never touched. Then forget the materials.
    /// </summary>
    internal void Release()
    {
        if (_appliedEpsilon > 0f)
        {
            try
            {
                for (int i = 0; i < _mats.Count; i++)
                {
                    Material m = _mats[i];
                    if (m == null)
                        continue;
                    float d = m.GetFloat(DissolveId);
                    if (d > 0f && d <= _appliedEpsilon)
                        m.SetFloat(DissolveId, 0f);
                }
            }
            catch (System.Exception)
            {
                // Releasing a dying hierarchy — nothing to restore on destroyed materials.
            }
        }
        _mats.Clear();
        _errorLogged = false;
    }
}
