using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE PER-BOARD KEYCAP ATLAS — one texture per board style, a grid of cells, one cell per
/// <see cref="CapRole"/>.
///
/// <para><b>WHAT IT REPLACES.</b> Every keycap on every board shared ONE material
/// (<c>KeycapGrain_albedo.png</c>), which is exactly why an oak board, a steel board and a bronze
/// board wore identical keys — the user's report, verbatim: <i>"Pro Board soll es auch ein anderes
/// passendes Aussehen der buttons sein, das zu dem board und seinem Aussehen passt."</i> And the
/// caps carried text and nothing else, where he asked for <i>"ein Symbol (und Text dazu)"</i>.</para>
///
/// <para><b>WHY AN ATLAS AND NOT ONE TEXTURE PER ROLE.</b> Seven roles times three styles is
/// twenty-one textures. One atlas per style is three, and the role costs two floats:
/// <c>BoardLit</c> already runs <c>o.uv = TRANSFORM_TEX(v.uv, _MainTex)</c> and samples
/// <c>_BumpMap</c>/<c>_MRSMap</c> with that same <c>i.uv</c>, so a material's
/// <c>mainTextureScale/Offset</c> picks the cell for all three maps at once. That is also what
/// makes a live role SWAP (the follow/pin toggle) free.</para>
///
/// <para><b>WHY THE SYMBOL IS CARVED IN AND NOT RAISED.</b> Measured, not stylistic. The
/// board-texture round established that on this shader the specular is a BEVEL term, not a
/// surface term — with the baked key and a viewer in front of the board the half-vector sits
/// ~32° off a flat face, so <c>_SpecStrength</c> 0 vs 0.85 moves the flat-on mean by 0.001. A
/// RAISED symbol reads by its highlight and the highlight is the one thing this surface cannot
/// deliver; a RECESSED one reads by ambient occlusion, which is baked into the albedo and is
/// view-independent. It also echoes the board, whose own pad motifs and frame ornament are
/// carved — and the two REST symbols are not merely similar to the pads beside them, they are
/// the pads' own stencils (<c>rest_short</c> / <c>rest_long</c>, taken from the sheet that
/// style's board took them from).</para>
///
/// <para><b>NO TEXT IS EVER IN THE ATLAS.</b> The generic caps' captions are LIVE GAME TEXT —
/// <c>CardsGameApi.PickDialogOptionLabel</c> reads them off the option button a 2D player would
/// click, so they change with the dialog and with the language, and they are mirrored to peers on
/// <c>NetProtocol.ExtIdCapLabels</c>. A symbol belongs to a ROLE and the roles are fixed; a
/// caption does not. Any design that bakes a caption into a texture is wrong the moment the
/// dialog changes.</para>
///
/// <para><b>GRACEFUL ABSENCE.</b> A bundle without the atlases falls back to the shared
/// <c>KeycapGrain</c> pair and the centred label — i.e. bit-identically to the build before this
/// existed. The plugin ships against whatever bundle is installed, so that path is somebody's
/// live board, not a theoretical one.</para>
/// </summary>
internal static class CapSymbols
{
    /// <summary>Atlas grid — one definition, in <see cref="CapCellMath"/>, which is where the
    /// cell arithmetic lives and is the only part of this class that can be wrong QUIETLY.</summary>
    private const int GridCols = CapCellMath.GridCols;
    private const int GridRows = CapCellMath.GridRows;

    // The inset that keeps a cap's outermost sample off its cell boundary is CapCellMath
    // .InsetTexels; the argument for it is on that constant.

    /// <summary>Bundle path of a style's keycap atlas. The three base names are authored in
    /// <c>cap_atlas.py:STYLE_FILES</c> and must match it exactly.</summary>
    private static string AlbedoPath(ControlBoard style) => $"Assets/Bundle/Table/Keycap{StyleName(style)}_albedo.png";

    private static string NormalPath(ControlBoard style) => $"Assets/Bundle/Table/Keycap{StyleName(style)}_normal.png";

    private static string StyleName(ControlBoard style) => style switch
    {
        ControlBoard.Steel => "Steel",
        ControlBoard.Bronze => "Bronze",
        _ => "Oak",
    };

    private static readonly Texture2D?[] Albedo = new Texture2D?[3];
    private static readonly Texture2D?[] Normal = new Texture2D?[3];

    /// <summary>
    /// Remaining re-probe attempts per style, and the next time each may probe.
    ///
    /// <para><b>SUCCESSES ARE CACHED; MISSES ARE NOT — and that asymmetry is a fix, not a
    /// nicety.</b> A bundled asset is not discoverable until something has loaded the bundle, and
    /// this board is built inside that window often enough that the mod carries a whole bounded
    /// heal for it (<c>BoardButton.TryHealCapMaterial</c>, written because a cap built before
    /// <c>GloomhavenVR/BoardLit</c> was loadable kept the flat fallback FOREVER). The obvious
    /// implementation here — one bool per style, set on the first probe, the pattern
    /// <c>PlayTray.EnsureGrainLoaded</c> uses — reproduces exactly that defect one asset over: a
    /// board built a few frames early would wear no symbols for the whole session, silently, and
    /// the log would say the atlas was "not in bundle (older bundle)" about a bundle that has it.
    /// The grain texture can afford that cache because it is a surface; a symbol is the control's
    /// meaning.</para>
    ///
    /// <para>Bounded, because a bundle that genuinely lacks the atlas must not turn the retry into
    /// a probe storm of the mod's own making — the same rule <c>CardArtGuard</c> and the cap-material
    /// heal both hold. ~10 s of board life at half-second spacing, then it latches and says so.</para>
    /// </summary>
    private static readonly int[] ProbeBudget = { ProbeAttempts, ProbeAttempts, ProbeAttempts };
    private static readonly float[] NextProbeAt = new float[3];

    /// <summary>Re-probe attempts before the miss is treated as final (~10 s at
    /// <see cref="ProbeIntervalSeconds"/>).</summary>
    private const int ProbeAttempts = 20;

    /// <summary>Re-probe cadence. Cheap, but never per material and never per frame.</summary>
    private const float ProbeIntervalSeconds = 0.5f;

    /// <summary>
    /// The style's atlas pair, loaded ONCE from whichever loaded bundle holds it (the same probe
    /// pattern <c>PlayTray.EnsureGrainLoaded</c> uses, and for the same reason: a bundled asset is
    /// not discoverable until something loads it). Caches the NOT-FOUND state too, so an older
    /// bundle never retries per material. Returns false when this bundle has no atlas for the
    /// style — the caller then keeps the shared grain and the centred label.
    /// </summary>
    internal static bool TryAtlas(ControlBoard style, out Texture2D albedo, out Texture2D? normal)
    {
        int i = (int)style;
        if (i < 0 || i >= Albedo.Length)
        {
            albedo = null!;
            normal = null;
            return false;
        }
        if (Albedo[i] == null && ProbeBudget[i] > 0 && Time.unscaledTime >= NextProbeAt[i])
        {
            NextProbeAt[i] = Time.unscaledTime + ProbeIntervalSeconds;
            ProbeBudget[i]--;
            string ap = AlbedoPath(style), np = NormalPath(style);
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null) continue;
                Albedo[i] ??= b.LoadAsset<Texture2D>(ap);
                Normal[i] ??= b.LoadAsset<Texture2D>(np);
                if (Albedo[i] != null && Normal[i] != null) break;
            }
            if (Albedo[i] != null)
            {
                // CLAMP, not Repeat. The cell rect never leaves 0..1, so wrapping cannot occur
                // through the intended path — but a cap whose UVs are ever driven out of range
                // (a future non-planar cap, a bad ST write) must sample its own cell's edge and
                // not wrap around to the far side of the atlas, which would draw a different
                // role's symbol.
                Albedo[i]!.wrapMode = TextureWrapMode.Clamp;
                if (Normal[i] != null)
                    Normal[i]!.wrapMode = TextureWrapMode.Clamp;
                VRLog.Info("Cards", $"Keycap atlas loaded for {style} ('{ap}' {Albedo[i]!.width}×" +
                    $"{Albedo[i]!.height}{(Normal[i] != null ? $" + normal {Normal[i]!.width}×{Normal[i]!.height}" : ", NO normal map")}) — " +
                    $"{GridCols}×{GridRows} cells of {Albedo[i]!.width / GridCols} texels, one per CapRole; " +
                    "the board keycaps get this board's own material with the role symbol carved into it.");
            }
            else if (ProbeBudget[i] == 0)
            {
                // Said ONCE, on the LAST attempt, so a slow bundle load does not fill the log with
                // a line that is about to stop being true — and so a genuinely older bundle still
                // states the outcome plainly instead of leaving it to be inferred from an absence.
                VRLog.Info("Cards", $"Keycap atlas '{ap}' never turned up in any loaded bundle after " +
                    $"{ProbeAttempts} probes over ~{ProbeAttempts * ProbeIntervalSeconds:F0} s — the " +
                    $"{style} board's keycaps keep the shared KeycapGrain surface, with no symbol and " +
                    "a centred label, exactly as they were before this existed. That is the expected " +
                    "outcome on a bundle from before ModBuild 278; on a current one it means the " +
                    "atlas is missing from the pack.");
            }
        }
        albedo = Albedo[i]!;
        normal = Normal[i];
        return albedo != null;
    }

    /// <summary>
    /// The atlas sub-rectangle for a role, as the <c>(scale, offset)</c> pair a material's texture
    /// transform wants — computed from the LOADED texture's own size, so re-authoring the atlas at
    /// a different cell size needs no code change and cannot silently disagree with the script that
    /// wrote it. The arithmetic, including the row-from-the-bottom flip that is the whole trap, is
    /// in <see cref="CapCellMath.Cell"/>, which is Unity-object-free so the wire tests can link it.
    /// </summary>
    internal static void CellTransform(Texture2D atlas, CapRole role, out Vector2 scale, out Vector2 offset) =>
        CapCellMath.Cell(atlas.width, atlas.height, (int)role, out scale, out offset);

    /// <summary>
    /// True when this role's SYMBOL is the whole face and the cap carries no caption of its own.
    ///
    /// <para>THE SPLIT IS THE USER'S OWN RULE, applied per case as he asked ("Entscheide das
    /// selber pro Fall. Versetze dich in einen Spieler der die Symbolik und eventuell auch das
    /// Spiel noch nicht kennt."). The four GENERIC caps keep their text because the caption is the
    /// only thing that says what THIS press will do — it changes every pick, and no symbol can
    /// carry it. The rest pads and the follow/pin toggle mean the same thing forever, so their
    /// caption is a one-time teaching aid, and a teaching aid belongs on the BOARD
    /// (<see cref="BoardEngraving"/>) where it can be read once and then stop competing with the
    /// cap for attention. A player who knows neither the symbol nor the game still learns it,
    /// because the word is engraved directly beside the pad it names.</para>
    ///
    /// <para>It is FALSE for every role while the bundle has no atlas: with no symbol to carry the
    /// meaning, taking the caption away would leave a blank key.</para>
    /// </summary>
    internal static bool SymbolOnly(CapRole role) =>
        role is CapRole.ShortRest or CapRole.LongRest or CapRole.FixedPinned or CapRole.FixedFollow;

    /// <summary>Where the TMP caption's centre sits on a cap of this role, as a fraction of the cap
    /// HEIGHT measured from the cap's centre. Mirrored from <c>cap_atlas.py</c>'s
    /// <c>TEXT_LABEL_CENTRE_DY</c>: change one and the caption runs through the carved symbol.
    ///
    /// <para>THE NUMBERS LIVE IN <see cref="CapFaceLayout"/> AND NOT HERE since round 2, because
    /// they stopped being only a texture-layout question: the caption band, the symbol band and the
    /// cap's own bezel profile are ONE budget, and the third of those is geometry
    /// (<c>CardMesh.BuildBeveledKeycap</c>). Splitting that budget across two files is precisely what
    /// let the caption box be cut by 58 % in ModBuild 281 with nothing in the build noticing —
    /// the "AUSWAHL BEEN" report.</para></summary>
    internal static float LabelCentreY(CapRole role, bool hasSymbol) =>
        hasSymbol && role != CapRole.Plain ? CapFaceLayout.CaptionCentreYWithSymbol : 0f;

    /// <summary>The caption's fit box on a cap of this role, as fractions of the cap footprint.
    /// Mirrored from <c>cap_atlas.py</c>'s <c>TEXT_LABEL_BOX</c>. See <see cref="LabelCentreY"/> for
    /// why both numbers now come from <see cref="CapFaceLayout"/>.
    ///
    /// <para>The no-symbol box used to be the literal <c>(0.92, 0.85)</c> that
    /// <c>BoardButton.Create</c> had always passed. 0.92 of the cap WIDTH is wider than the plateau
    /// it sat on, so that caption overhung the chamfer on both sides and floated past the cap's own
    /// edge — visible in the user's screenshot. It is the recessed FIELD now, on both axes.</para></summary>
    internal static Vector2 LabelBox(CapRole role, bool hasSymbol) =>
        hasSymbol && role != CapRole.Plain
            ? CapFaceLayout.CaptionBoxWithSymbol
            : CapFaceLayout.CaptionBoxNoSymbol;
}
