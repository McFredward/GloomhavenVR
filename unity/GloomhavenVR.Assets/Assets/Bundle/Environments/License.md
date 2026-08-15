# Environments — provenance

Both ambient environments (`Env_Cellar.prefab`, `Env_Swamp.prefab` — the latter
is a night FOREST since the ModBuild 132 round; the file name is a runtime
contract and does not change) are assembled deterministically by
`Assets/Editor/BuildEnvironments.cs` + `Assets/Editor/BuildEnvironmentRooms.cs`:
FX shells (star dome, catalogue stars, mist, wisps, dust) plus full room
interiors under each prefab's `RoomGeo` node. Shaders (`Env*.shader`), sprite
textures, procedural meshes (tree trunks, crowns, canopy, moonlight shafts, the
star-field point sprites), materials and the prefabs are **original work of this
project**. Third-party assets:

## Room models & PBR texture sets (`Imported/`)

All meshes and photo textures under `Imported/Models` and `Imported/Textures`
are from **Poly Haven** (<https://polyhaven.com>) — with THREE exceptions, the
cobweb alpha (TextureCan), the wall-fungus atlas (Wikimedia Commons) and the
fire atlas (**Unity Asset Store, and the only non-CC0 source in this bundle —
read its section before adding another**), which have their own sections
below — license
**CC0 1.0**
(public domain, <https://polyhaven.com/license>) — no attribution required;
credited here with thanks. Modifications: UV-preserving decimation
(MeshLab quadric-with-texture), AO multiplied into albedo, opacity merged
into albedo alpha, resolution reduced (reproducible pipeline script:
`Assets/Editor/polyhaven_pipeline.py` — python3 + pymeshlab + Pillow).

Models (`https://polyhaven.com/a/<name>`):

- cellar: wine_barrel_01, wooden_crate_01, small_wooden_table_01,
  wooden_stool_02, wooden_bookshelf_worn, wooden_bucket_01, jug_01
- forest: dead_tree_trunk, dead_tree_trunk_02, tree_stump_01, tree_stump_02,
  root_cluster_02, single_root, rock_moss_set_01, rock_moss_set_02,
  dry_branches_medium_01, grass_medium_02, fern_02, shrub_03, moss_01,
  wooden_axe_02

Texture sets: medieval_blocks_05 (cellar walls), monastery_stone_floor
(cellar floor/steps), dark_wooden_planks (ceiling/beams), forest_ground_04 +
forest_leaves_04 (forest floor blend), pine_bark + bark_brown_02 (the
procedural conifer trunks). The candle-flame sprite (`candle_flame_alb.jpg`)
is the `flame_diff` map of Poly Haven's brass_candleholders.

### Conifer foliage (`fir_twig_alb.png`)

- **Asset**: the `twig_diff` + `twig_alpha` maps of *fir_tree_01*
  (<https://polyhaven.com/a/fir_tree_01>), Poly Haven, **CC0 1.0**.
- **Why only the maps**: Poly Haven's scanned conifers are 0.5–1 GB
  multi-material photoscans whose alpha twig cards do not survive decimation.
  Their twig ATLAS, however, is exactly what a card-built forest needs — seven
  isolated fir sprigs and one bare branch on clean transparency. The mesh is
  never downloaded; the trunks, crowns and canopy are grown procedurally by
  `BuildEnvironmentRooms.cs` and textured with these real photoscanned needles.
- **Modifications**: diffuse and alpha merged into one RGBA PNG; sub-rects cut
  by connected-component analysis of the alpha channel.

### Cobwebs (`cobweb_alb.png`)

- **Asset**: the `opacity` (+ `color`) maps of *Spider Web / Cobweb*, asset id
  **`others_0015`**, from **TextureCan** —
  <https://www.texturecan.com/details/237/>.
- **License**: **CC0 1.0**, stated at <https://www.texturecan.com/terms/>:
  “All the PBR textures … are under the Creative Commons CC0 1.0 Universe
  License … The textures are allowed to be redistributed together with your
  projects.” No attribution required; credited here with thanks.
- **Why this one**: it is a photoscanned *opacity map*, not a photograph we
  would have to key — a full orb web with irregular anchor strands running off
  every edge, which is exactly what a cellar corner wants. The webs it replaced
  were procedural (nine even spokes, eleven even spirals, on a five-ring quarter
  fan) and the user reported them as “sehr low-poly”; regularity, not triangle
  count, was the problem, and a real web's alpha is the fix.
- **Modifications** (reproducible: `Assets/Editor/cobweb_pipeline.py`, python3 +
  numpy + Pillow): normalised by the source's own peak (it maxes at 114/255,
  not 255), a 5.5 % floor subtracted to kill JPEG ringing around the ~1 px
  threads, 4096 → 1024 by area mean followed by a ×3.0 gain so alpha-test
  coverage is preserved (11.4 % of texels above the cutoff, unchanged), and RGB
  replaced by the source colour map's heavily blurred luminance mapped to
  0.72–1.00 — large-scale dustiness only. Imported BC7 with
  `mipMapsPreserveCoverage` at `EnvironmentsBuilder.WebCutoff`.
- **Not used, but evaluated** (recorded so a future round need not repeat the
  search): ambientCG and Poly Haven have **no** web asset at all (both APIs
  queried in full); Kenney has none. CC0 alternatives that would have worked:
  Wikimedia Commons *“The Web is a Tentative Thing”* (Alan Levine, CC0 1.0,
  5184×3456, <https://commons.wikimedia.org/wiki/File:2016-366-292_The_Web_is_a_Tentative_Thing_(30420326115).jpg>)
  and OpenGameArt *2D Spider Webs* (Christina Lee, CC0, RGBA but only 707×282).
  Rejected on licence: Resource Boy's 4K spiderweb pack — its licence forbids
  redistributing the files as part of a bundle, **even for free**. Rejected as
  mislabeled: Commons `File:Spider Cobweb (blue).jpg`, the top CC0 hit for
  “cobweb”, is a photograph of **feathers**.

The loose hanging strands (`Textures/Env_Strand.png`) are still generated —
`EnvironmentsBuilder.MakeStrand`, original work of this project.

### Wall fungus (`fungus_alb.png`)

- **Asset**: an atlas of **eight** bracket-fungus cutouts on transparent
  background, keyed by this project out of four **Wikimedia Commons**
  photographs. It feeds the cellar "growth cards" — alpha-tested billboards
  stuck horizontally into the masonry.
- **License**: every source is **CC0 1.0 Universal** (public domain dedication,
  <https://creativecommons.org/publicdomain/zero/1.0/>). No attribution
  required; credited here with thanks. Each file's licence was re-verified
  through the Commons API (`action=query&prop=imageinfo&iiprop=extmetadata`,
  fields `LicenseShortName` = `CC0`, `UsageTerms` = "Creative Commons Zero,
  Public Domain Dedication", `LicenseUrl` =
  `http://creativecommons.org/publicdomain/zero/1.0/deed.en`, `Restrictions`
  empty) on **2026-08-14**, immediately before download.

  | Commons file | Author | Direct URL | Used for |
  | --- | --- | --- | --- |
  | `File:Tinder-fungus-1817040.jpg` (3072×2304) — <https://commons.wikimedia.org/wiki/File:Tinder-fungus-1817040.jpg> | **Tappancs** (originally <https://pixabay.com/en/users/Tappancs-829780/>; uploaded to Commons 2017-01-06 by Nikos Andronikos, Pixabay licence review confirmed by Amitie 10g 2017-01-07 — a **pre-2019** Pixabay upload, i.e. from the era when Pixabay's terms *were* CC0) | <https://upload.wikimedia.org/wikipedia/commons/c/c3/Tinder-fungus-1817040.jpg> | `shelf_tinder` |
  | `File:Porling nahe Rheingoldhalle.jpg` (4312×5760) — <https://commons.wikimedia.org/wiki/File:Porling_nahe_Rheingoldhalle.jpg> | **ManuelB701**, own work | <https://upload.wikimedia.org/wikipedia/commons/0/0a/Porling_nahe_Rheingoldhalle.jpg> | `hoof_porling` |
  | `File:Bracket fungus, 2021-09-15, Bird Park, 01.jpg` (4152×2768) — <https://commons.wikimedia.org/wiki/File:Bracket_fungus,_2021-09-15,_Bird_Park,_01.jpg> | **Cbaile19**, own work | <https://upload.wikimedia.org/wikipedia/commons/d/df/Bracket_fungus%2C_2021-09-15%2C_Bird_Park%2C_01.jpg> | `cluster_bird_top`, `cluster_bird_low`, `shelf_bird_right`, `small_bird_far`, `small_bird_left` |
  | `File:Shelf fungus, Fox Chapel, 2022-03-05, 01.jpg` (4160×3120) — <https://commons.wikimedia.org/wiki/File:Shelf_fungus,_Fox_Chapel,_2022-03-05,_01.jpg> | **Cbaile19**, own work | <https://upload.wikimedia.org/wikipedia/commons/9/95/Shelf_fungus%2C_Fox_Chapel%2C_2022-03-05%2C_01.jpg> | `shelf_fox` |

- **Why photographs and not a library asset**: neither Poly Haven nor
  ambientCG has a bracket fungus at all, and a shelf fungus is one of the few
  props whose whole job is its *silhouette* — a scanned albedo would still have
  had to be keyed.
- **Modifications**: crop; segmentation by OpenCV grabCut seeded from a
  brightness-minus-saturation trimap (the fungi are pale, the backdrops are
  dark bark and leaf litter or bright-but-saturated foliage), except the tinder
  hoof, whose grey underside is tonally identical to the bark behind it and
  which therefore needed a hand-traced silhouette with grabCut allowed only to
  refine it; morphological open/close, connected-component filtering and hole
  filling; the trunk removed and the attachment edge cut off along a straight
  vertical line, since that edge is buried in the wall. Colour is then
  re-sourced from a few pixels inside the silhouette — a plain erosion plus an
  explicit rejection of violet pixels near the rim, because the source JPEGs
  carry a chromatic-aberration fringe on every high-contrast edge — and carried
  through the resize **premultiplied**, so no background texel can weight into
  an edge texel; the transparent region is finally filled by nearest-opaque
  bleed so mip generation cannot pull anything foreign into the visible edge.
  Result: 1024×1024 RGBA8, ~0.9 % of texels partially transparent (a one-texel
  ramp), ≥20 transparent texels between cutouts and ≥10 against the border.
- **Sub-rects**: eight, all normalised so that **+U points away from the wall
  and +V points up**; the rect's left edge is the buried attachment edge. The
  table is `Rect[]` data for the card builder, not part of this file.
- **No pipeline script ships**: unlike `polyhaven_pipeline.py` and
  `cobweb_pipeline.py`, the keying here needed per-image hand tuning (one
  traced polygon, per-image morphology radii) and was done offline; the method
  above is the record. Re-deriving it needs python3 + numpy + OpenCV + SciPy +
  Pillow.
- **Not used, but evaluated** (recorded so a future round need not repeat the
  search; both are equally **CC0 1.0 Universal**, verified the same way on the
  same date): `File:Bracket fungus, Trillium Trail, 2022-03-30, 01.jpg`
  (Cbaile19, 3835×2876,
  <https://commons.wikimedia.org/wiki/File:Bracket_fungus,_Trillium_Trail,_2022-03-30,_01.jpg>)
  — tan brackets on tan cut wood, no tonal separation to key against; and
  `File:Hillesheim (Rheinhessen) - Bahnhofstraße, Baumpilz.jpg` (ManuelB701,
  3000×4000,
  <https://commons.wikimedia.org/wiki/File:Hillesheim_(Rheinhessen)_-_Bahnhofstra%C3%9Fe,_Baumpilz.jpg>)
  — a vertically stacked cluster, but wholly inside the trunk's shadow band, so
  the brackets are darker than the lit bark around them.

### Fire atlas (`fire_atlas_alb.png`) — **THE ONE NON-CC0 SOURCE IN THIS BUNDLE**

> **Read this section before adding any further Asset Store material.** Every
> other third-party asset above is CC0 or public domain, i.e. redistributable
> by anyone for anything. These two are not. They are licensed to *this
> project* under the Unity Asset Store EULA and that licence does **not** flow
> to anyone downstream.

- **Assets**: the greyscale flame/turbulence masks of two **free Unity Asset
  Store packages**, supplied by the project owner:
  - *Free Fire VFX - HDRP*, publisher **Vefects**, Asset Store id **239742**
    (<https://assetstore.unity.com/packages/vfx/free-fire-vfx-hdrp-239742>).
    Used: `T_VFX_Fire_Ground_Mask_01.tga` (512², 8-bit grey),
    `T_VFX_Fire_Mask_01.tga` (256²), `T_VFX_Noise_07.tga` (256²).
  - *Fire 001*, publisher **N2Studio**. Used: `Textures/FireSeq1.png` (1024²
    RGB, a 2×2 sheet of four turbulence frames).
- **License**: the **standard Unity Asset Store EULA**
  (<https://unity.com/legal/as-terms>, "Last updated: December 4, 2024"). This
  was determined rather than assumed:
  - **Neither package ships a licence file.** Vefects' `_ Read Me _` folder
    holds two PDFs (`Vefects Fire VFX Info Doc HDRP.pdf`, `Vefects Top
    Secret.pdf`); both were extracted in full and contain **no licence terms
    at all** — marketing copy, links to the publisher's other packs, and a
    Discord invitation. N2Studio ships `Third-Party Notices.txt`, which states
    verbatim: *"This asset is governed by the Asset Store EULA; however, the
    following component is governed by the license indicated below: A. Nova
    Shader — MIT License — Copyright (c) CyberAgent Game Entertainment
    Division"*. (The Nova Shader is MIT but is **URP-only** and is not used.)
  - The store record for 239742 gives `category 116 (VFX)`, `customLicense:
    false`, `isFree: true` — so it is **not** an "Extension Asset" (EULA
    §2.3.2 covers only "Editor Extension", "Scripting" and "Services"), **not**
    a Restricted Asset (§2.9), and carries no non-standard EULA. Free vs paid
    changes exactly one thing: §11.3.2 gives no publisher indemnity for free
    assets.
- **What the EULA permits**, verbatim, §2.2.1: *"Licensor hereby grants to the
  END-USER a non-exclusive, non-transferable, worldwide, and perpetual license
  to the Asset solely: (a) to incorporate the Asset, together with substantial,
  original content not obtained through the Unity Asset Store, into an
  electronic application or digital media that has a purpose, features, and
  functions beyond the display, performance, distribution, or use of Assets
  ("Licensed Product") as an embedded component of that Licensed Product, such
  that the Asset does not comprise a substantial portion of the Licensed
  Product; (b) to reproduce, publicly display, publicly perform, transmit, and
  distribute the Asset as incorporated and embedded in that Licensed Product;
  … (e) … modify the Assets in connection with (a), (b), (c), and (d)."*
- **On the extractability of the bundle** — the question that had to be
  answered before shipping anything here, because this mod is publicly
  downloadable and an AssetBundle can be opened with AssetStudio. **The EULA
  contains no clause prohibiting distribution in an extractable form.** The
  current text and the previous one (as-terms-legacy, "Last Updated: January 1,
  2023") were both searched in full for `extract`, `stand-alone`, `whole or in
  part` and `as is`; the only occurrences of "extraction" are inside the
  AI/ML-training prohibition (§2.2.1.1(g)), which is a different subject. The
  nearest authority is Unity's own EULA FAQ
  (<https://assetstore.unity.com/browse/eula-faq>), verbatim: *"A product is
  not 'incorporated' into the Licensed Product if it is designed to allow your
  end users to extract or download assets separately from the Licensed
  Product."* The operative test is **"designed to allow"**, not "technically
  extractable" — every Unity game ever shipped is technically extractable, so
  mere extractability cannot be the standard without making §2.2.1(b) a
  nullity. This mod is a VR conversion of a commercial game consisting almost
  entirely of original work; the fire atlas is one 512×512 texture in it. It
  qualifies as a Licensed Product and the textures are an embedded component.
  (The FAQ carries its own *"provided to you 'AS IS' and does not constitute
  legal advice"* disclaimer.)
- **What is NOT permitted, and the standing rules that follow:**
  1. **The raw source files may never enter this repository.** Committing the
     `.tga`/`.png` sources, or either `.unitypackage`, is distribution that is
     not "as incorporated and embedded" and is outside the grant. They live in
     `.planning/debug/ressources/`, which is gitignored, and the pipeline
     script reads them from there.
  2. **Nobody downstream receives a licence.** The grant is expressly
     *"non-transferable"* and §2.2.1.1(d) forbids sublicensing. Recipients of
     the mod get these texels only as an embedded component of it. If this
     project ever adopts a blanket open-source licence, **this asset must be
     carved out explicitly** — a repo-wide MIT/GPL statement would be
     inaccurate, because there is no right to grant it.
  3. **Monetisation and UGC.** §2.2.1.1(b)/(c) bar enabling users to
     redistribute the assets for commercial gain and bar monetising them in a
     product whose primary purpose is creating user-generated content. A free
     mod trips neither.
  4. Under §1.4 the **publisher, not Unity, is the Licensor** and the only
     party with standing. A short written permission from Vefects/N2Studio
     would moot this entire analysis, and the FAQ explicitly invites it
     (*"You are always free to reach out to the publisher directly to negotiate
     additional rights."*).
- **Why these and not a CC0 alternative**: they were supplied by the project
  owner with a direct instruction to use a ready-made fire FX rather than a
  hand-built one. Both packages' **shaders are unusable here** — Vefects' five
  shaders are tagged `RenderPipeline = "HDRenderPipeline"` and N2Studio's Nova
  Shader `"UniversalPipeline"`, while this project is BUILT-IN — so what was
  taken is the art only.
- **Modifications** (reproducible: `Assets/Editor/fire_atlas_pipeline.py`,
  python3 + numpy + Pillow, run against the two `.unitypackage` files): the
  ground-flame mask is cropped to its lower 64 %, horizontally re-scaled and
  squeezed into a bottom band to make the **bed**; the same mask full-height
  makes **tongue A**; the flame mask is mirrored and narrowed to 0.80 width for
  **tongue B**; one frame of N2Studio's turbulence sheet, radially feathered,
  makes the **puff**. Each is then normalised on its own peak, given a
  base/tip feed profile, holed by the Vefects noise map, feathered sideways to
  zero well inside its cell, soft-knee compressed to a ceiling of 0.90–0.95 so
  no region can saturate in the additive pass, given a hard-zero 3.5 % border,
  and packed into this project's own 2×2 cell layout with RGB forced to white
  (all colour comes from `EnvFire.cginc`'s three-stop temperature ramp).
  **The result is a re-authored composite: no source image survives in the
  shipped file as delivered, at its delivered size, or in its delivered
  framing.**
- **Not used, and pruned deliberately**: everything else in both packages —
  all HDRP/URP shaders, the Nova Shader library and its editor scripts, the
  demo scenes, the 18 prefabs, all materials, the three fire WAVs, and the 14
  `Vefects_*_Extra_01.tga` files (which are advertising banners for other
  packs). Only the four greyscale masks named above are ever read, and only by
  the pipeline script, which is editor-only.

## Night-sky panorama — REMOVED in ModBuild 134

The environments used to ship a processed *Rogland Clear Night* HDRI panorama
(Greg Zaal, Poly Haven, CC0 1.0, <https://polyhaven.com/a/rogland_clear_night>)
as `Textures/Env_NightSky.png`. The user rejected it as a static image
("entferne das statische Bild und gehe voll zu einem dynamischen Sternenhimmel
(ausschließlich)"), so the texture, its import step and its processing script
are gone and **no third-party sky asset ships any more**. Everything continuous
in the sky — gradient, Milky Way, sub-visual star dust, moon — is now generated
in `EnvStars.shader` from the galactic frame derived in `BuildEnvironments.cs`;
the point stars are the catalogue below. The note is kept because the asset was
once distributed in the bundle.

## Galactic coordinate frame (`EnvStars.shader` Milky Way)

- **Constants**: IAU 1958 galactic frame in J2000 equatorial coordinates —
  north galactic pole RA 192.85948°, Dec +27.12825°; galactic centre
  RA 266.40510°, Dec −28.936175°; position angle of the celestial pole
  l = 122.93192°. Published astronomical constants (facts, not copyrightable);
  `EnvironmentsBuilder.GalacticBasis` builds the rotation from them and asserts
  the third against the first two.

## Star catalogue (`Assets/Editor/bsc5_stars.csv` → the star-field mesh)

- **Data**: *Bright Star Catalogue, 5th Revised Edition (Preliminary)* —
  Hoffleit D. & Warren Jr. W.H., 1991, Astronomical Data Center, NSSDC/ADC.
- **Source**: <http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz> (Harvard/NASA
  mirror; CDS mirror <https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz>)
- **License**: a NASA/NSSDC product — a US Government work, not subject to
  copyright; star positions and magnitudes are uncopyrightable facts in any
  case. No attribution required; credited here.
- **Use**: fetched and reduced by `Assets/Editor/star_catalogue.py` to
  right ascension / declination / V magnitude / B−V for the 8404 stars down to
  V=6.5. Since ModBuild 134 `BuildEnvironments.BuildStarField` uses ALL of them
  (it was V≤6.0, 5080 stars, until the photographic backdrop was deleted and the
  sky between them read empty); `EnvStarPoints.shader` rotates them about the
  celestial pole and twinkles them. The raw CSV lives under
  `Assets/Editor/` and is therefore editor-only — it never enters the bundle or
  the player build; only the derived mesh ships.
- **B−V → colour**: Ballesteros' blackbody fit (Ballesteros, F. J., 2012,
  *"New insights into black bodies"*, EPL **97**(3) 34008, arXiv:1201.1809)
  followed by Tanner Helland's Kelvin→RGB approximation
  (<https://tannerhelland.com/2012/09/18/convert-temperature-rgb-algorithm-code.html>).
  Both are published numerical fits, reimplemented from the formulae.
- **Twinkle**: amplitude follows the Rozenberg (1966) airmass approximation.
- **Dither**: the sky gradient is dithered with interleaved gradient noise
  (Jimenez, *Next Generation Post Processing in Call of Duty: Advanced
  Warfare*, SIGGRAPH 2014) — a published one-line hash, reimplemented.

## History

Earlier iterations assembled full low-poly rooms from CC0 (public domain)
Quaternius model packs ("Modular Dungeon Pack", "Ultimate Nature Pack",
quaternius.com). That geometry was removed when the game took over scenario
geometry generation (Apparance); CC0 required no attribution — this note is
kept for provenance only. The night-marsh dressing that preceded the forest
used brown_mud_leaves_01, dead_quiver_trunk and namaqualand_boulder_05
(also Poly Haven CC0); they were dropped with the swamp.
