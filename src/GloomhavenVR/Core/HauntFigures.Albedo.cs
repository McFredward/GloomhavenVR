using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT FIGURES — THE DARKENING LEVER, MOVED FROM A COLOUR TO THE ALBEDO TEXTURE.
// =================================================================================================

internal static partial class HauntFigures
{
    /// <summary>
    /// THE LEVER THAT ACTUALLY REACHES A PIXEL. It replaces each apparition material's albedo
    /// TEXTURE with a darkened copy of itself, produced on the GPU by a blit.
    ///
    /// <para><b>WHY A TEXTURE AND NOT A COLOUR, AND WHY THAT IS NOT A FIFTH GUESS.</b> Four builds
    /// darkened these figures by writing a colour property and the user photographed a fully-lit
    /// creature every time. ModBuild 152 measured why and left the conclusion in the log
    /// (LogOutput.log:1585, per renderer, live off the rendering material):</para>
    /// <code>
    /// mat 'MO_Cultist_MAT (Instance)' shader 'Amp_Char_Shader' queue=2450 lit=NO (no ForwardBase pass)
    ///   _Diffuse=MO_Cultist_d _Cutoff=0.5000 ... _MOD_TINT=(1.000,1.000,1.000,0.000)
    /// </code>
    /// <para>Two things follow. The shader has NO ForwardBase pass, so it samples no light probes and
    /// the entire per-renderer SH rig this feature binds reaches it with zero effect — it is
    /// effectively unlit and draws what its own textures say. And a black <c>_MOD_TINT</c> written
    /// live onto a figure that renders at full albedo is not a figure darkened too little; it is a
    /// property the shader ignores. <c>_Diffuse</c> is the one channel that is certainly consumed,
    /// because it is what the photograph shows. So the multiply moves onto it.</para>
    ///
    /// <para><b>THE ALPHA IS MULTIPLIED BY EXACTLY 1 AND THAT IS THE LOAD-BEARING DETAIL.</b> The
    /// material carries <c>_Cutoff = 0.5000</c>, i.e. the shader ALPHA-TESTS against the albedo's
    /// fourth channel: that is what cuts the cultist's skirt into cloth, the ribbons into ribbons and
    /// the skeleton's pelvis-cloth into a torn edge. Flattening alpha to 1 would turn every one of
    /// those into a solid slab — a worse picture than the one being fixed. The blit shader used here
    /// is <c>GloomhavenVR/HeadUnlit</c>, whose fragment is exactly
    /// <c>tex2D(_MainTex, i.uv) * _Color</c> with no blend command at all, so writing
    /// <c>_Color = (k, k, k, 1)</c> multiplies RGB and passes alpha through untouched. It was picked
    /// over the built-in candidates for a second reason as well: its vertex input is POSITION and
    /// TEXCOORD0 and nothing else, which is precisely what <see cref="Graphics.Blit"/>'s quad
    /// provides — <c>Sprites/Default</c>, <c>UI/Default</c> and the particle shaders all multiply by
    /// a vertex COLOUR that a blit quad does not supply, and <c>GloomhavenVR/MapUnlit</c> ends its
    /// fragment with a hard <c>return fixed4(col, 1.0)</c>.</para>
    ///
    /// <para><b>WHAT MOVING THE LEVER COSTS, stated plainly.</b> ModBuild 152's colour write also
    /// DESATURATED the albedo 0.85 of the way toward the room's own chromaticity, on the argument
    /// that a uniform multiply preserves saturation and the eye finds a saturated patch far more
    /// easily than a neutral one. A per-channel blit multiply cannot express that: desaturation needs
    /// a per-texel luma dot product, and no shader in this mod's bundle or in the built-in set does
    /// one. Measured off Figur_hell9.jpg, the cost is the figure's mean saturation staying at its
    /// authored 0.214 instead of falling to 0.191 (0.319 instead of 0.151 on Figur_hell7.jpg). It is
    /// accepted this round because the saturated population's luminance lands INSIDE the same band as
    /// the rest of the creature rather than above it (p99 0.049 against a whole-figure p90 of 0.045),
    /// so a coloured texel is no brighter than a bone texel. Tinting the blit toward the room colour
    /// was tried and REJECTED by measurement: a coloured multiplier ADDS saturation (0.214 -> 0.323),
    /// so the multiplier is deliberately neutral.</para>
    ///
    /// <para><b>THE MATERIALS DIE WITH THE APPARITION, so nothing is restored.</b> Every material
    /// here came out of <c>Renderer.materials</c>, which instantiates — the census prints them as
    /// <c>'MO_Cultist_MAT (Instance)'</c> — and <c>Clone.Release</c> already calls
    /// <c>Object.Destroy</c> on every one of them. A material instance therefore cannot outlive the
    /// apparition that owns it, so restoring <c>_Diffuse</c> would be writing to an object that is
    /// about to be destroyed. The SOURCE textures are shared game assets and are never written to,
    /// never destroyed and never even read on the CPU. What DOES outlive an apparition is the
    /// RenderTexture cache, and that is destroyed in <see cref="Shutdown"/>.</para>
    /// </summary>
    private static class Albedo
    {
        // ---- the blit ----------------------------------------------------------------------------

        /// <summary>
        /// The shader whose fragment is <c>tex2D(_MainTex, i.uv) * _Color</c>.
        ///
        /// <para><b>THE PARAGRAPH THAT USED TO BE HERE WAS WRONG, AND IT COST ModBuild 153 ENTIRELY.</b>
        /// It argued that because the head-avatar masks are built as bundled MATERIALS on this
        /// shader (unity/.../Editor/BuildHeads.cs:33, <c>Mask_&lt;n&gt;.mat</c> under
        /// <c>Assets/Bundle/Head</c>), the shader is compiled into the bundle and
        /// <see cref="Shader.Find"/> therefore resolves it. The first half is true and the second
        /// does not follow. <c>Shader.Find</c> returns only shaders that are LOADED, and a bundled
        /// shader is loaded when something pulls it in — here, when a head-avatar MASK MATERIAL is
        /// instantiated. In a cellar scenario with no head avatars standing, nothing ever loads it.
        /// The ModBuild 153 log says so in one line (LogOutput.log:1244), the fail-dark branch
        /// engaged exactly as designed, the apparition kept the colour lever that same build proved
        /// inert — and the user photographed the ModBuild 152 picture for the sixth time.</para>
        ///
        /// <para><b>IT IS NOW RESOLVED THROUGH <see cref="BundleShaders"/></b>, which loads the
        /// shader ASSET out of whichever bundle holds it. That turns the precondition from "a head
        /// avatar happens to be up" into "the mod bundle is open" — and the bundle is necessarily
        /// open here, because an apparition only exists inside a mod-built environment room and the
        /// rooms come out of the same bundle. The coupling to the head subsystem is gone; what
        /// remains is a coupling to one asset PATH, which the wire test pins against the real file.</para>
        ///
        /// <para><b>WHY THE CHAIN STOPS AT THIS SHADER rather than falling back to another one.</b>
        /// Every other shader reachable at runtime would produce a WORSE picture than failing dark:
        /// <c>Sprites/Default</c>, <c>UI/Default</c>, <c>GloomhavenVR/Overlay</c> and the particle
        /// shaders all multiply by a vertex COLOUR that <see cref="Graphics.Blit"/>'s quad does not
        /// reliably supply (an unbound COLOR attribute reads as black on D3D11 — a silently BLACK
        /// creature); <c>GloomhavenVR/MapUnlit</c>, <c>EnvRoom</c> and <c>EnvGround</c> all end their
        /// fragment with a hard <c>return fixed4(col, 1.0)</c>, which flattens the alpha the
        /// material's <c>_Cutoff = 0.5</c> alpha-TESTS and would turn the cultist's skirt, the
        /// ribbons and the skeleton's pelvis-cloth into solid slabs; <c>GloomhavenVR/BoardLit</c> is
        /// LIT, so it would bake a studio rig into the albedo. The redundancy that IS worth having
        /// is redundancy of MECHANISM, not of shader, and that is what BundleShaders provides
        /// (Shader.Find, then every loaded bundle by asset path, then a live-object sweep).</para>
        ///
        /// <para><b>THE RECOMMENDATION THIS LANE COULD NOT IMPLEMENT.</b> Borrowing the head
        /// avatar's shader still couples this feature to an unrelated subsystem's ASSET PATH: rename
        /// <c>Assets/Bundle/Head/HeadUnlit.shader</c> and this feature breaks with no compile error.
        /// The right long-term answer is a dedicated blit shader in the bundle — and, while one is
        /// being added, one that also carries a per-texel LUMA DOT PRODUCT, which is the one thing
        /// the colour lever could do and a per-channel multiply cannot (see the desaturation
        /// paragraph in the class doc). Adding it means editing <c>unity/</c>, which another lane
        /// owns this round, so it is written down here and in the lane report instead of reached
        /// for.</para>
        /// </summary>
        private const string BlitShader = "GloomhavenVR/HeadUnlit";

        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int BlitColourId = Shader.PropertyToID("_Color");

        private static Material? _blit;

        /// <summary>Frame of the last blit-shader lookup attempt. A MISS is retried (a bundle can
        /// load later than the first apparition) but not every frame — <see cref="RetryFrames"/>
        /// apart, because the probe walks every loaded bundle.</summary>
        private static int _blitTriedFrame = int.MinValue;

        private const int RetryFrames = 120;

        private static string _blitWhy = string.Empty;

        /// <summary>
        /// THE PROPERTY NAMES THAT ARE AN ALBEDO, matched as lower-cased SUBSTRINGS so a creature on
        /// a shader nobody here has looked at is still caught. <see cref="AlbedoNever"/> is tested
        /// FIRST and wins.
        /// </summary>
        private static readonly string[] AlbedoLike =
        {
            "diffuse", "albedo", "basecolor", "basecolour", "basemap", "maintex", "_col",
        };

        /// <summary>
        /// Texture slots that are NOT an albedo, whatever else their name contains. Every one of
        /// these is a map whose channels mean something other than "what colour is this texel", and
        /// multiplying one of them down would change a NORMAL, a MASK or a CUTOUT rather than a
        /// brightness. <c>_EmissiveMap</c> is on the list twice over: it is handled by the emission
        /// side, which scales the STRENGTH beside it rather than the map itself.
        /// </summary>
        private static readonly string[] AlbedoNever =
        {
            "normal", "bump", "emissive", "emission", "mask", "opacity", "metal", "smooth", "rough",
            "occlusion", "_ao", "specular", "gloss", "detail", "noise", "ramp", "matcap", "cube",
            "lut", "height", "displace", "flow", "proxy", "dissolve", "cinder", "outline",
        };

        // ---- one material's albedo slot ------------------------------------------------------------

        /// <summary>One material's albedo texture slot and the asset it shipped with. The SOURCE is
        /// captured in <see cref="Register"/>, strictly before this side has written anything, so a
        /// second <see cref="Apply"/> at a different room level re-derives from the original rather
        /// than darkening an already-darkened copy.</summary>
        private readonly struct Slot
        {
            internal readonly Material Mat;
            internal readonly int Prop;
            internal readonly string PropName;
            internal readonly Texture Src;

            internal Slot(Material mat, int prop, string propName, Texture src)
            {
                Mat = mat; Prop = prop; PropName = propName; Src = src;
            }
        }

        private static readonly List<Slot> Slots = new(16);

        /// <summary>Instance ids of the materials that carry the texture lever. <c>Shade</c> reads it
        /// to decide whether that material's COLOUR write may still carry the room level or must
        /// carry the envelope alone — writing the level through both would darken by its square, and
        /// "I cannot find it at all" is the failure mode this round is most exposed to.</summary>
        private static readonly HashSet<int> Levered = new(16);

        // ---- the cache -------------------------------------------------------------------------

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int _tex;
            private readonly int _q;

            internal Key(int tex, int q) { _tex = tex; _q = q; }

            public bool Equals(Key o) => _tex == o._tex && _q == o._q;
            public override bool Equals(object? o) => o is Key k && Equals(k);
            public override int GetHashCode() => (_tex * 397) ^ _q;
        }

        /// <summary>Level quantisation for the cache key. 1/256 is far finer than any difference the
        /// eye could find at these levels and coarse enough that rig jitter cannot spawn a second
        /// copy of the same texture.</summary>
        private const int LevelSteps = 256;

        /// <summary>How many darkened copies may be alive at once. A creature's four materials share
        /// ONE <c>_Diffuse</c> (the census: all four of the Cultist's renderers name
        /// <c>MO_Cultist_d</c>), so the realistic occupancy is one entry per creature per room and
        /// this cap is generous. It exists so that a long scenario cannot turn a cache into a
        /// leak.</summary>
        private const int CacheCap = 4;

        private static readonly Dictionary<Key, RenderTexture> Cache = new(8);

        /// <summary>Insertion order, oldest first — the eviction order when <see cref="CacheCap"/> is
        /// reached.</summary>
        private static readonly List<Key> Order = new(8);

        // ---- diagnostics -------------------------------------------------------------------------

        /// <summary>One line per material, printed by the census. Says which texture was darkened, by
        /// how much, what the source and the result actually MEASURE, and whether reading the
        /// property back off the material returned the darkened copy.</summary>
        internal static readonly List<string> Verdicts = new(16);

        private static float _appliedLevel = -1f;
        private static int _appliedQ = -1;

        /// <summary>
        /// WHAT ACTUALLY HAPPENED TO THE ALBEDO, as opposed to what the design intends. Every
        /// diagnostic that mentions the darkening reads this, because the two standing rules this
        /// feature has broken are "a comment is not a guard" and "a log line must report an outcome,
        /// not an intention" — and the second is precisely how ModBuild 153 was lost: the
        /// <c>HAUNT FIGURES light level</c> line said "IT IS MULTIPLIED INTO THE ALBEDO TEXTURE"
        /// unconditionally, in a log in which the multiply had never run.
        /// </summary>
        internal enum State
        {
            /// <summary><see cref="Apply"/> has not run since the last <see cref="Forget"/>.</summary>
            NotAsked,

            /// <summary>No material on this creature declares an albedo-ish texture slot at all, so
            /// there is nothing for this lever to hold. The name matcher (<see cref="AlbedoLike"/>)
            /// is the suspect.</summary>
            NoSlots,

            /// <summary>The blit shader could not be resolved. THIS IS THE ModBuild 153 FAILURE.</summary>
            NoShader,

            /// <summary>The blit ran but reading the texture back off the material did not return the
            /// darkened copy on any material — the write did not land.</summary>
            DidNotLand,

            /// <summary>The darkened copy is on the material, verified by read-back.</summary>
            Ran,
        }

        private static State _state = State.NotAsked;

        /// <summary>The outcome the log has already stated. One line per DISTINCT outcome rather
        /// than one per apparition: a periodic apparition would otherwise repeat the same sentence
        /// every eight seconds, and a state CHANGE (a bundle that finished loading late) is the one
        /// event that must not be swallowed. The per-apparition repetition the brief asks for is on
        /// the <c>HAUNT FIGURES light level</c> line and in the census, both of which quote
        /// <see cref="Outcome"/>.</summary>
        private static State _announced = State.NotAsked;

        /// <summary>Measured mean-luma ratios (darkened / source) of every texture freshly blitted
        /// this apparition — the MEASUREMENT the decisive line reports. -1 until one is taken.</summary>
        private static float _ratioMin = -1f, _ratioMax = -1f;
        private static int _ratioCount;

        /// <summary>Materials that carry the lever, and materials that were offered it.</summary>
        private static int _landed, _offered;

        internal static bool Wears(Material m) => m != null && Levered.Contains(m.GetInstanceID());

        internal static float AppliedLevel => _appliedLevel;

        /// <summary>What actually happened. Read by the <c>HAUNT FIGURES light level</c> line and by
        /// the material census, so neither can claim a multiply that did not occur.</summary>
        internal static State Status => _state;

        /// <summary>
        /// THE ONE SENTENCE ANY OTHER DIAGNOSTIC MUST QUOTE INSTEAD OF DESCRIBING THE DESIGN. It
        /// names the outcome first, in the words a reader will search for, and only then the
        /// numbers.
        /// </summary>
        internal static string Outcome => _state switch
        {
            State.Ran =>
                $"ALBEDO DARKENING RAN — the multiply is on the albedo TEXTURE of {_landed} of "
                + $"{_offered} material slot(s), confirmed by reading the texture reference back off "
                + $"the material. Intended level {_appliedLevel:F4}; MEASURED mean-luma ratio "
                + $"{Ratio()} over {_ratioCount} freshly blitted texture(s), taken off the render "
                + $"targets. Blit shader '{BlitShader}' via {BundleShaders.How(BlitShader)}",
            State.NoShader =>
                "ALBEDO DARKENING DID NOT RUN — THE FIGURE IS DRAWN AT ITS FULL AUTHORED BRIGHTNESS "
                + "AND NOTHING ABOUT DarkFloor/LightGain/MaxLevel/the cinder share HAS BEEN TESTED BY "
                + $"THIS BUILD. Cause: {_blitWhy}",
            State.DidNotLand =>
                $"ALBEDO DARKENING DID NOT LAND — the blit produced a darkened copy (ratio {Ratio()}) "
                + $"but NONE of the {_offered} material slot(s) returned it on read-back, so the "
                + "figure is drawn at its full authored brightness and nothing about the fitted "
                + "constants has been tested by this build",
            State.NoSlots =>
                "ALBEDO DARKENING HAD NOTHING TO HOLD — not one material on this creature declares a "
                + "texture slot that reads as an albedo, so the figure is drawn at its full authored "
                + "brightness. The suspect is the name matcher (Albedo.AlbedoLike), not the fitted "
                + "constants; the per-material ALBEDO TEXTURE lines name every slot each shader has",
            _ => "ALBEDO DARKENING NOT YET ASKED — no apparition has reached Shade in this room",
        };

        private static string Ratio() =>
            _ratioCount == 0 ? "<none measured>"
            : _ratioCount == 1 ? $"{_ratioMin:F4}"
            : $"{_ratioMin:F4}..{_ratioMax:F4}";

        // ---- binding -----------------------------------------------------------------------------

        /// <summary>
        /// Record every albedo-ish texture this material declares. Called once per material from
        /// <c>Clone.Collect</c>, after <c>Donate</c> — because a donated shader is a different
        /// property set and the slots must describe the shader the material will actually be drawn
        /// with.
        ///
        /// <para>THE SHADER IS ENUMERATED RATHER THAN ASSUMED. The brief for this round said "its
        /// <c>_Diffuse</c> (and any other albedo-ish texture the shader declares — enumerate them; do
        /// not assume there is exactly one)". On <c>Amp_Char_Shader</c> the answer happens to be
        /// exactly one, and the verdict line says so with the count, so the next reader does not have
        /// to take this comment's word for it.</para>
        /// </summary>
        internal static void Register(Material m)
        {
            if (m == null)
                return;
            Shader? sh = m.shader;
            if (sh == null)
            {
                Verdicts.Add($"'{m.name}' [<null shader>] ALBEDO TEXTURE: no shader, nothing to darken");
                return;
            }

            var found = new StringBuilder(96);
            int taken = 0, textures = 0;
            int count = sh.GetPropertyCount();
            for (int p = 0; p < count; p++)
            {
                if (sh.GetPropertyType(p) != ShaderPropertyType.Texture)
                    continue;
                textures++;
                string name = sh.GetPropertyName(p);
                string low = name.ToLowerInvariant();
                if (Any_(low, AlbedoNever) || !Any_(low, AlbedoLike))
                    continue;

                Texture? src = m.GetTexture(name);
                if (src == null)
                {
                    if (found.Length > 0) found.Append(", ");
                    found.Append($"{name}=<none, skipped>");
                    continue;
                }

                // NOTE the material is NOT added to Levered here. Levered means "the darkening
                // LANDED on this material", not "it was a candidate", and it is written in Apply
                // from the read-back. Registering it here would be fail-OPEN: if the blit shader
                // could not be found, Shade would drop the room level from the colour lever on the
                // strength of a texture write that never happened, and the figure would come out
                // BRIGHTER than it was in ModBuild 152. See Apply.
                Slots.Add(new Slot(m, Shader.PropertyToID(name), name, src));
                taken++;
                if (found.Length > 0) found.Append(", ");
                found.Append($"{name}={src.name} {src.width}x{src.height} {(src is Texture2D t2 ? t2.format.ToString() : src.dimension.ToString())}");
            }

            Verdicts.Add(taken > 0
                ? $"'{m.name}' [{sh.name}] ALBEDO TEXTURE: {taken} of {textures} texture slot(s) are "
                  + "CANDIDATES for the blit (this line is a registration, NOT an outcome — ModBuild "
                  + "153 printed exactly this and the blit never ran; the OUTCOME line above the "
                  + $"census says whether it did) — {found}"
                : $"'{m.name}' [{sh.name}] ALBEDO TEXTURE: NONE of its {textures} texture slot(s) reads "
                  + $"as an albedo{(found.Length > 0 ? " (" + found + ")" : string.Empty)}. This material "
                  + "keeps the COLOUR lever alone, and on a shader that ignores it that means it is "
                  + "drawn at full authored brightness — which is the ModBuild 149-152 picture. If a "
                  + "creature reads as too bright and its line is this one, the name matcher "
                  + "(Albedo.AlbedoLike) is what missed it.");
        }

        private static bool Any_(string haystack, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (haystack.Contains(needles[i]))
                    return true;
            return false;
        }

        // ---- the write ---------------------------------------------------------------------------

        /// <summary>
        /// Put a darkened copy of every registered albedo onto its material. Idempotent and latched
        /// on the QUANTISED level, so the steady state costs one integer compare per frame and a
        /// room whose rig changes re-derives from the ORIGINAL texture rather than from the last
        /// copy.
        /// </summary>
        internal static void Apply(float level)
        {
            if (Slots.Count == 0)
            {
                _state = State.NoSlots;
                Announce();
                return;
            }
            int q = Mathf.Clamp(Mathf.RoundToInt(level * LevelSteps), 0, LevelSteps);
            if (q == _appliedQ)
                return;

            Material? blit = Blit();
            if (blit == null)
            {
                // FAIL SAFE IS FAIL DARK. Levered stays empty, so Shade keeps writing
                // level x envelope through the colour lever exactly as ModBuild 152 did. That is a
                // lever this shader ignores, i.e. no worse than the build being replaced — whereas
                // dropping the level on the strength of a texture write that did not happen would be
                // strictly BRIGHTER than 152 and would make this round a regression.
                Levered.Clear();
                _appliedQ = q;                     // retried on the next level change, not per frame
                _appliedLevel = -1f;
                _state = State.NoShader;
                Announce();
                return;
            }

            _appliedQ = q;
            _appliedLevel = (float)q / LevelSteps;

            // Rebuilt from the READ-BACK below, never from the candidate list. See Register.
            Levered.Clear();
            _offered = Slots.Count;
            // The measured range describes THIS level, so it is re-derived whenever the level moves
            // rather than accumulated across the apparition.
            _ratioMin = _ratioMax = -1f;
            _ratioCount = 0;

            for (int i = 0; i < Slots.Count; i++)
            {
                Slot s = Slots[i];
                if (s.Mat == null || s.Src == null)
                    continue;

                RenderTexture? dark = Get(s.Src, q, blit, out float srcMean, out float dstMean,
                                          out bool fresh);
                if (dark == null)
                    continue;

                s.Mat.SetTexture(s.Prop, dark);

                // ---- THE READ-BACK, and it is here because a SILENTLY UNAPPLIED WRITE is exactly
                // the failure mode that cost the last four rounds. ModBuild 151 dumped a black
                // _MOD_TINT off the live material and the figure still rendered lit; nothing in that
                // log said whether the value the shader CONSUMED was the one that had been written.
                // A texture reference can be compared by identity, so this one can be settled.
                Texture? readBack = s.Mat.GetTexture(s.Prop);
                bool landed = readBack != null && readBack.GetInstanceID() == dark.GetInstanceID();

                // ...and the read-back is what promotes the material, so the colour lever's decision
                // in Shade is made on evidence rather than on intent.
                if (landed)
                    Levered.Add(s.Mat.GetInstanceID());

                if (!fresh)
                    continue;                      // one verdict per (texture, level), not per frame

                // THE MEASUREMENT, kept for the decisive line as well as for the per-material one.
                // A ratio is only meaningful where the source is not black.
                if (srcMean > 1e-6f)
                {
                    float ratio = dstMean / srcMean;
                    _ratioMin = _ratioCount == 0 ? ratio : Mathf.Min(_ratioMin, ratio);
                    _ratioMax = _ratioCount == 0 ? ratio : Mathf.Max(_ratioMax, ratio);
                    _ratioCount++;
                }

                Verdicts.Add(
                    $"'{s.Mat.name}' [{(s.Mat.shader != null ? s.Mat.shader.name : "<null>")}] "
                    + $"{s.PropName}: '{s.Src.name}' {s.Src.width}x{s.Src.height} -> darkened copy at "
                    + $"level {_appliedLevel:F4}. MEASURED OFF THE RENDER TARGETS, not intended: mean "
                    + $"luma source {srcMean:F5} -> result {dstMean:F5}, ratio {(srcMean > 1e-6f ? dstMean / srcMean : -1f):F4} "
                    + $"against an intended {_appliedLevel:F4}. Both means are 256-tap samples taken "
                    + "through the SAME blit at identity, so the RATIO is the measurement and the two "
                    + "absolute means are only indicative. READ-BACK: the material's own "
                    + $"{s.PropName} is {(landed ? "NOW THE DARKENED COPY" : "NOT the darkened copy — THE WRITE DID NOT LAND")}. "
                    + $"colourSpace={QualitySettings.activeColorSpace}, RT.sRGB={dark.sRGB}, "
                    + "alpha multiplied by exactly 1 because _Cutoff alpha-tests this channel. "
                    + "IF THE RATIO IS NOT THE INTENDED LEVEL TO WITHIN A FEW PERCENT, the multiply "
                    + "did not land and no amount of re-fitting DarkFloor/LightGain can help; if it "
                    + "matches AND the figure still photographs bright, then _Diffuse is not what "
                    + "this shader draws either and the next lever is replacing the SHADER with a "
                    + "flat dark cutout shader — which would cost the dissolve, because the dissolve "
                    + "belongs to the game shader.");
            }

            _landed = Levered.Count;
            _state = _landed > 0 ? State.Ran : State.DidNotLand;
            Announce();
        }

        /// <summary>
        /// THE LINE THIS ROUND EXISTS TO ADD. One statement of the OUTCOME per apparition, at the
        /// severity the outcome deserves, phrased so that the words a reader searches for —
        /// "ALBEDO DARKENING DID NOT RUN" — are in it.
        ///
        /// <para><b>WHY IT IS NOT ENOUGH THAT THE OLD LINE EXISTED.</b> ModBuild 153 DID log its
        /// failure: one Warning, at LogOutput.log:1244 of a 3600-line log, phrased as a shader
        /// lookup rather than as a feature outcome. It took a human reading the whole log to connect
        /// it to "the fix did nothing". So this line is an ERROR when the lever did not reach a
        /// pixel (the log's error list is short and is read first), it names the FEATURE and not the
        /// lookup, and the same sentence is repeated on the <c>HAUNT FIGURES light level</c> line and
        /// in the material census — which between them are the three places anybody looks.</para>
        /// </summary>
        private static void Announce()
        {
            if (_state == _announced)
                return;
            _announced = _state;
            string line = "HAUNT FIGURES " + Outcome + ".";
            if (_state == State.Ran)
            {
                VRLog.Info("Core", line
                    + $" colourSpace={QualitySettings.activeColorSpace}. THE THREE READINGS THIS LINE "
                    + "DISTINGUISHES, and the next hardware round is exactly one of them: (a) this "
                    + "line says DID NOT RUN — the mechanism was bypassed and nothing was tested; "
                    + "(b) it says RAN but the measured ratio disagrees with the intended level by "
                    + "more than a few percent — the multiply landed in the wrong colour space and "
                    + "the lever is the RenderTexture's sRGB flag, NOT DarkFloor/LightGain (a ratio "
                    + "near level^2.2 or level^(1/2.2) is that signature exactly); (c) it says RAN, "
                    + "the ratio matches, and the figure still photographs fully lit — then _Diffuse "
                    + "is not what Amp_Char_Shader draws either, every property-level lever on this "
                    + "shader has now been tried, and the only remaining one is REPLACING THE SHADER "
                    + "with a flat dark cutout shader, which costs the dissolve. Per-texture detail "
                    + "is in the PER-MATERIAL ALBEDO TEXTURE block of the material census.");
            }
            else
            {
                VRLog.Error("Core", line
                    + " THIS IS READING (a) OF THE THREE THE 'light level' LINE LISTS: the mechanism "
                    + "was bypassed, so the photograph that follows this build says NOTHING about "
                    + "DarkFloor/LightGain/MaxLevel and they must not be re-tuned on the strength of "
                    + "it. The apparition keeps the COLOUR lever alone, which ModBuild 153 measured "
                    + "to be INERT on Amp_Char_Shader (no ForwardBase pass, _MOD_TINT ignored), so "
                    + "the figure renders exactly as it did in ModBuild 152. "
                    + BundleShaders.Inventory() + ".");
            }
        }

        private static Material? Blit()
        {
            if (_blit != null)
                return _blit;
            // A MISS IS RETRIED. ModBuild 153 latched the first miss for the life of the process,
            // so a bundle that finished loading one frame later could never be picked up. It is not
            // retried every frame either: the probe walks every loaded bundle.
            int now = Time.frameCount;
            if (_blitTriedFrame != int.MinValue && now - _blitTriedFrame < RetryFrames)
                return null;
            _blitTriedFrame = now;

            // THE LOOKUP THAT COST ModBuild 153 ITS ENTIRE ROUND. It used to be a bare
            // Shader.Find, which sees only shaders something else has already LOADED — see the
            // BlitShader doc. BundleShaders loads the asset out of the bundle itself.
            Shader? sh = BundleShaders.Resolve(
                BlitShader, "Core",
                "the haunt apparition's albedo texture can be darkened by a GPU blit (the only lever "
                + "on Amp_Char_Shader that reaches a pixel).",
                "THE HAUNT APPARITION CANNOT BE DARKENED AT ALL and will render at its full authored "
                + "brightness — this is the ModBuild 149-153 picture.");
            if (sh == null)
            {
                _blitWhy = $"the blit shader '{BlitShader}' could not be resolved by any of "
                           + "BundleShaders' three mechanisms (Shader.Find, AssetBundle.LoadAsset "
                           + $"\"{BlitShader}\" by asset path across every loaded bundle, live-object "
                           + "sweep), so no darkened copy could be produced. The apparition keeps the "
                           + "COLOUR lever alone, and ModBuild 153 measured that lever to be inert on "
                           + "Amp_Char_Shader";
                return null;
            }
            _blitWhy = $"'{BlitShader}' resolved via {BundleShaders.How(BlitShader)}";
            _blit = new Material(sh) { name = "GHVR_HauntAlbedoDarken", hideFlags = HideFlags.HideAndDontSave };
            // Identity TRANSFORM_TEX: the copy must be texel-for-texel, because the GAME shader
            // applies its own tiling/offset when it samples the result.
            _blit.SetVector(MainTexStId, new Vector4(1f, 1f, 0f, 0f));
            return _blit;
        }

        private static RenderTexture? Get(Texture src, int q, Material blit,
                                          out float srcMean, out float dstMean, out bool fresh)
        {
            srcMean = dstMean = -1f;
            fresh = false;
            var key = new Key(src.GetInstanceID(), q);
            if (Cache.TryGetValue(key, out RenderTexture cached) && cached != null)
                return cached;

            // NOTHING IS EVICTED HERE, and that is deliberate. An entry evicted mid-apparition would
            // be a RenderTexture destroyed while a live material still names it as its albedo, i.e. a
            // creature drawn with a dead texture — a worse bug than the one the cap exists to
            // prevent. The cap is enforced in Forget instead, which runs between apparitions when no
            // material this class ever touched is still alive. See Trim.
            float k = (float)q / LevelSteps;

            // COLOUR SPACE: RenderTextureReadWrite.Default is the engine's own "match the project"
            // mode — no sRGB conversion in a Gamma project, sRGB in a Linear one. That is exactly
            // right here and it is right WITHOUT this file having to know which project it is in: the
            // source is an ordinary albedo texture whose sampling is governed by the same setting, so
            // matching the project reproduces the original pipeline end to end. The log prints
            // activeColorSpace and the RT's own sRGB flag beside the measured ratio so the claim is
            // checkable rather than asserted.
            var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.Default)
            {
                name = $"GHVR_HauntAlbedo_{src.name}_{q}",
                useMipMap = true,
                autoGenerateMips = false,
                wrapMode = src.wrapMode,
                filterMode = src.filterMode,
                anisoLevel = src.anisoLevel,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!rt.Create())
            {
                Object.Destroy(rt);
                VRLog.Warn("Core", $"HAUNT FIGURES albedo darkening: RenderTexture.Create failed for "
                                   + $"'{src.name}' {src.width}x{src.height}. The apparition keeps the "
                                   + "COLOUR lever alone for this material.");
                return null;
            }

            // ALPHA IS MULTIPLIED BY 1. See the class doc: _Cutoff = 0.5 means this shader
            // alpha-TESTS the albedo's fourth channel, so flattening it would turn cloth and ribbons
            // into solid slabs. The multiplier is also NEUTRAL rather than tinted toward the room
            // colour: measured on Figur_hell9.jpg a coloured multiplier RAISES figure saturation
            // (0.214 -> 0.323), which is the opposite of what darkening is for.
            blit.SetColor(BlitColourId, new Color(k, k, k, 1f));
            Graphics.Blit(src, rt, blit);
            rt.GenerateMips();

            srcMean = MeanLuma(src, blit);
            dstMean = MeanLuma(rt, blit);
            fresh = true;

            Cache[key] = rt;
            Order.Add(key);
            return rt;
        }

        /// <summary>
        /// Mean Rec.709 luma of a texture, sampled through the SAME blit at identity so that the
        /// source and the darkened result are measured by one instrument and their RATIO is exact
        /// whatever the colour-space plumbing does to both.
        ///
        /// <para>It reads back a 16x16 probe and NOT the texture itself: the game's albedos are not
        /// CPU-readable, and <c>GetPixels</c> on one would fail outright. 256 taps is a sample rather
        /// than a true mean, which is why the verdict line calls the absolute values indicative and
        /// the ratio the measurement.</para>
        /// </summary>
        private static float MeanLuma(Texture t, Material blit)
        {
            const int P = 16;
            RenderTexture probe = RenderTexture.GetTemporary(P, P, 0, RenderTextureFormat.ARGB32,
                                                             RenderTextureReadWrite.Default);
            RenderTexture? prev = RenderTexture.active;
            Texture2D? cpu = null;
            try
            {
                blit.SetColor(BlitColourId, Color.white);
                Graphics.Blit(t, probe, blit);
                RenderTexture.active = probe;
                cpu = new Texture2D(P, P, TextureFormat.RGBA32, false, false)
                          { hideFlags = HideFlags.HideAndDontSave };
                cpu.ReadPixels(new Rect(0f, 0f, P, P), 0, 0, false);
                cpu.Apply(false, false);
                Color32[] px = cpu.GetPixels32();
                double sum = 0d;
                for (int i = 0; i < px.Length; i++)
                    sum += (0.2126d * px[i].r + 0.7152d * px[i].g + 0.0722d * px[i].b) / 255d;
                return px.Length > 0 ? (float)(sum / px.Length) : -1f;
            }
            catch (Exception e)
            {
                VRLog.Warn("Core", "HAUNT FIGURES albedo probe failed (the darkening itself is "
                                   + "unaffected; only the measurement in the census line is): " + e.Message);
                return -1f;
            }
            finally
            {
                RenderTexture.active = prev;
                if (cpu != null)
                    Object.Destroy(cpu);
                RenderTexture.ReleaseTemporary(probe);
            }
        }

        // ---- teardown ----------------------------------------------------------------------------

        /// <summary>End of ONE apparition. The material references go — <c>Clone.Release</c> destroys
        /// those materials on the next statement — but the darkened copies stay, because the point of
        /// caching them is that a creature which appears a second time in the same room does not
        /// re-blit.</summary>
        internal static void Forget()
        {
            Slots.Clear();
            Levered.Clear();
            Verdicts.Clear();
            _appliedQ = -1;
            _appliedLevel = -1f;
            // The OUTCOME is per apparition and so is reset here; _announced deliberately is NOT,
            // so a periodic apparition does not restate the same sentence every eight seconds.
            _state = State.NotAsked;
            _ratioMin = _ratioMax = -1f;
            _ratioCount = 0;
            _landed = _offered = 0;
            Trim();
        }

        /// <summary>Bring the cache back under <see cref="CacheCap"/>, oldest first. Called ONLY from
        /// <see cref="Forget"/> — i.e. from the one moment at which no material this class has
        /// written to is still alive, so a destroyed copy can never be a live material's albedo. A
        /// cache that is quietly growing is the one way this class could become the leak it exists to
        /// avoid, and this is where that is stopped rather than in the hot path.</summary>
        private static void Trim()
        {
            while (Order.Count > CacheCap)
            {
                Key old = Order[0];
                Order.RemoveAt(0);
                if (!Cache.TryGetValue(old, out RenderTexture dead))
                    continue;
                Cache.Remove(old);
                if (dead == null)
                    continue;
                dead.Release();
                Object.Destroy(dead);
            }
        }

        /// <summary>End of the FEATURE. Everything this class ever created is destroyed here: every
        /// cached RenderTexture (released first, so the GPU allocation goes with it and not merely the
        /// managed wrapper) and the blit material.</summary>
        internal static void Shutdown()
        {
            Forget();
            foreach (KeyValuePair<Key, RenderTexture> kv in Cache)
            {
                if (kv.Value == null)
                    continue;
                kv.Value.Release();
                Object.Destroy(kv.Value);
            }
            Cache.Clear();
            Order.Clear();
            if (_blit != null)
                Object.Destroy(_blit);
            _blit = null;
            _blitTriedFrame = int.MinValue;
            _blitWhy = string.Empty;
            _announced = State.NotAsked;
        }

        /// <summary>The blit shader's own story — how it resolved, or why it did not. Distinct from
        /// <see cref="Outcome"/>, which is the FEATURE's story; the census prints both, because
        /// "the shader resolved" and "a pixel got darker" are different claims and ModBuild 153
        /// conflated them.</summary>
        internal static string Why => _blitWhy.Length > 0
            ? _blitWhy
            : $"'{BlitShader}' has not been looked up yet";

        /// <summary>Live copies and the VRAM they hold, for the census. A cache that is quietly
        /// growing is the one way this class could become the leak it exists to avoid.</summary>
        internal static string CacheReport()
        {
            long bytes = 0;
            foreach (KeyValuePair<Key, RenderTexture> kv in Cache)
                if (kv.Value != null)
                    bytes += (long)kv.Value.width * kv.Value.height * 4L * 4L / 3L;   // ARGB32 + mips
            return $"{Cache.Count}/{CacheCap} darkened copie(s) alive, about {bytes / (1024L * 1024L)} MB";
        }
    }
}
