using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Issue 2 dust for VRCards (user: "crumble to dust / emerge from dust instead of shrink/grow — more
/// EPIC"). ONE pooled world-space <see cref="ParticleSystem"/> serves EVERY card (Emit with per-
/// particle position/velocity/colour — no per-card systems, no per-frame allocations; the only
/// allocation is the one-time pool build). A deliberate sibling of <see cref="WorldUI.ButtonDissolveFx"/>
/// — same look (a few dozen small quads, short life ~0.3 s, sideways/settling drift + alpha fade) —
/// spread over the CARD FACE rectangle instead of a cap disc. Purely local visuals (never synced); the
/// logical hide/show is always immediate (the caller drops input first). Lazily rebuilt if a scene
/// unload destroyed the pool object; degrades to no dust in a shader-less environment.
/// </summary>
internal static class CardDustFx
{
    /// <summary>
    /// The dust's parchment warmth, and THE one source of it. A built card overwrites its own tone
    /// with its placeholder image's colour; everything that has no image to read — an un-themed
    /// local card, and every MIRRORED card on a peer's board — starts here.
    ///
    /// <para>It lives on the emitter rather than on <c>VRCard</c> because the mirror has no VRCard
    /// to ask. Making it a second literal in <c>Net/</c> was the first thing tried and is exactly
    /// the shape the 2026-08-27 1:1 audit had just finished pinning down elsewhere: a mirror that
    /// holds its OWN copy of a number diverges the day someone retunes one of the two.</para>
    /// </summary>
    internal static readonly Color DefaultTone = new(0.80f, 0.72f, 0.55f);

    /// <summary>
    /// WHOSE ANSWER the caller already holds — and therefore WHICH question is still open when a
    /// puff is asked for. The two are different questions with different owners, and folding them
    /// into one boolean is what made the MIRRORED dust inert for its whole shipped life
    /// (ModBuild 302 onwards).
    ///
    /// <para>THE DEFECT, stated so it cannot come back: <c>Net.RemoteHandFan.EmitMirroredCardDust</c>
    /// correctly gated on <c>_cardDustOn</c> — the OWNER's bit off wire id
    /// <see cref="Net.NetProtocol.TuneCardDustOn"/> — and then landed in a method whose first line
    /// asked the VIEWER's own <c>[Cards] CardDust</c> dial as well. That dial ships OFF, so at the
    /// shipped defaults a peer drew the owner's dust only if the peer had ALSO turned their own
    /// cards' dust on: an AND of two permissions, where the sender's own contract
    /// (<c>Net/Board/BoardTuning.cs</c>) states "a peer may not draw the owner's dust unless the
    /// owner has it on, AND MAY NOT WITHHOLD IT WHEN THEY DO".</para>
    /// </summary>
    internal enum Permission
    {
        /// <summary>
        /// "Do MY OWN cards emit dust?" — the local <c>[Cards] CardDust</c> dial is the whole
        /// answer, and this class asks it. Every local card path (<c>Cards.VRCard</c>) means this.
        ///
        /// <para>It is the DEFAULT deliberately: an omitted answer resolves to the STRICTER of the
        /// two gates, so a future emit path that forgets to say which question it is asking can
        /// only fail to draw — never draw a puff whose owner switched it off.</para>
        /// </summary>
        AskMyOwnDial = 0,

        /// <summary>
        /// "Does the OWNER of the board this card is drawn on have dust on?" — already answered
        /// YES by the caller, off that owner's wire bit, before it got here. The viewer's own dial
        /// is not consulted and MUST not be: it answers a question about the viewer's OWN cards.
        /// The only caller is the remote mirror.
        /// </summary>
        OwnerAlreadySaidYes,
    }

    /// <summary>
    /// THE SINGLE GATE (user ruling 2026-08-03: "kam während dessen so eine sehr große
    /// Funken/Partikel Animation über das gesamte Spielfeld (wahrscheinlich ausgehend von der
    /// abgeworfenen Karte). Geh da rein, das soll deaktiviert werden!"). Answered here rather than
    /// at the call sites so no future emit path can miss it — but the dial it reads is the LOCAL
    /// viewer's, so it may only be applied to a LOCAL card, which is what
    /// <see cref="Permission"/> makes the call site say out loud. Default OFF; the code stays so
    /// the effect can be revived once the world-scale sizing is reworked.
    /// </summary>
    private static bool Allowed(Permission permission) =>
        permission == Permission.OwnerAlreadySaidYes
        || (CardsConfig.CardDust != null && CardsConfig.CardDust.Value);

    private const int VanishCount = 30; // crumble puff — a bit denser than a button (a card is bigger)
    private const int AppearCount = 18; // fewer, converging — a quick "assembling" shimmer

    private static ParticleSystem? _ps;

    /// <summary>
    /// Crumble burst: motes spawn across the card face rectangle (<paramref name="right"/>/
    /// <paramref name="up"/> half-extents <paramref name="halfW"/>/<paramref name="halfH"/> in world
    /// meters, centred on <paramref name="center"/>) and drift sideways + settle down + puff toward
    /// the viewer along <paramref name="outNormal"/>, fading over their lifetime, in <paramref name="color"/>.
    /// </summary>
    internal static void EmitVanish(Vector3 center, Vector3 right, Vector3 up, Vector3 outNormal,
        float halfW, float halfH, Color color,
        Permission permission = Permission.AskMyOwnDial)
    {
        if (!Allowed(permission))
            return;
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — vanish degrades to the fade alone
        color.a = 1f;
        float span = Mathf.Clamp(Mathf.Max(halfW, halfH) * 2f, 0.01f, 0.6f); // scales drift/size
        // A common sideways sweep so the powder reads as "swept away", plus per-particle jitter.
        Vector3 sweep = Vector3.Cross(outNormal, Vector3.up);
        if (sweep.sqrMagnitude < 1e-4f)
            sweep = right;
        sweep.Normalize();
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < VanishCount; i++)
        {
            float rx = (UnityEngine.Random.value - 0.5f) * 2f;
            float ry = (UnityEngine.Random.value - 0.5f) * 2f;
            ep.position = center + right * (rx * halfW) + up * (ry * halfH)
                          + outNormal * (span * 0.04f * UnityEngine.Random.value);
            ep.velocity = (sweep * (0.6f + 0.5f * UnityEngine.Random.value)
                           + Vector3.down * (0.35f * UnityEngine.Random.value)      // settling
                           + outNormal * (0.2f + 0.25f * UnityEngine.Random.value)) // puff toward the viewer
                          * span;
            ep.startLifetime = 0.26f + 0.18f * UnityEngine.Random.value;
            ep.startSize = span * (0.05f + 0.05f * UnityEngine.Random.value);
            ep.startColor = color;
            _ps.Emit(ep, 1);
        }
    }

    /// <summary>
    /// Materialize shimmer (the crumble in reverse): motes spawn on a ring OUTSIDE the card face and
    /// drift INWARD toward <paramref name="center"/>, fading as they arrive, so the card reads as
    /// coalescing into place under the fade-in. Same pooled system / args as <see cref="EmitVanish"/>.
    /// </summary>
    internal static void EmitAppear(Vector3 center, Vector3 right, Vector3 up, Vector3 outNormal,
        float halfW, float halfH, Color color,
        Permission permission = Permission.AskMyOwnDial)
    {
        if (!Allowed(permission))
            return;
        if (_ps == null)
            BuildPool();
        if (_ps == null)
            return; // shader-less environment — appear degrades to the fade alone
        color.a = 1f;
        float span = Mathf.Clamp(Mathf.Max(halfW, halfH) * 2f, 0.01f, 0.6f);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < AppearCount; i++)
        {
            float rx = (UnityEngine.Random.value - 0.5f) * 2f;
            float ry = (UnityEngine.Random.value - 0.5f) * 2f;
            // Start OUTSIDE the card rectangle (×1.4) so the inward drift is clearly a gathering.
            Vector3 offset = right * (rx * halfW * 1.4f) + up * (ry * halfH * 1.4f);
            ep.position = center + offset + outNormal * (span * 0.12f * UnityEngine.Random.value);
            Vector3 inward = offset.sqrMagnitude > 1e-8f ? -offset.normalized : -right;
            ep.velocity = (inward * (0.9f + 0.4f * UnityEngine.Random.value)
                           + outNormal * 0.12f) * span; // slight lift toward the face so motes settle ONTO it
            ep.startLifetime = 0.14f + 0.10f * UnityEngine.Random.value;
            ep.startSize = span * (0.045f + 0.045f * UnityEngine.Random.value);
            ep.startColor = color;
            _ps.Emit(ep, 1);
        }
    }

    private static void BuildPool()
    {
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;
        var go = new GameObject("GloomhavenVR.CardDustFx");
        _ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = _ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true; // keeps the system simulating; idle cost ~zero with no live particles
        main.maxParticles = 512;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = _ps.emission;
        emission.enabled = false; // burst-only via Emit
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new UnityEngine.Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        ParticleSystem.SizeOverLifetimeModule size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = new Material(shader);
        renderer.sortingOrder = 2;
        Core.VRLayers.Apply(go);
        _ps.Play(); // armed — particles only exist after Emit
    }
}
