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

### Climbing ivy (`ivy_alb.png`)

- **Asset**: **ambientCG "Leaf Set 017"** — <https://ambientcg.com/a/LeafSet017>
  — an *Atlas* asset (`creationMethod` `PBRPhotogrammetry`, released
  2020-05-10), tagged `ivy`, `leaf`, `leaves`, `set`, `vine`. Six photoscanned
  ivy leaves on a clean opacity map, downloaded as `LeafSet017_2K-PNG.zip`
  (Color + Opacity are the only two maps used).
- **License**: **CC0 1.0 Universal**, stated verbatim at
  <https://docs.ambientcg.com/license/>: “All ambientCG assets are provided
  under the Creative Commons CC0 1.0 Universal License”, with the explicit
  permission to “copy, modify, distribute and perform the assets, even for
  commercial purposes” and to “include the raw files in your project, for
  example a video game”, and “You don't need to give credit but I would of
  course appreciate it, if you did it anyways.” No attribution required;
  credited here with thanks. Re-read at source on **2026-08-15**, immediately
  before download.
- **Why this one**: the cellar's Earth growth was three populations of PATCHES
  (wall cushions, floor-joint tufts, crust fungus) and the user asked for ivy,
  which is a LINE — it starts on the floor and climbs, and that direction is
  what a patch can never say. Poly Haven has **no** ivy or vine asset at all
  (its full model and texture indexes were queried for
  ivy/vine/creeper/climb: the single hit is `wine_bottles_01`, tagged
  “vineyard”). ambientCG has two ivy sets; **017** is the one also tagged
  `vine`, and its six leaves are cleanly separated with the asset's own opacity
  map, so nothing had to be keyed by hand.
- **Not used, but evaluated** (recorded so a future round need not repeat the
  search): ambientCG `LeafSet029` (tagged `ivy` but not `vine`, same licence —
  a fine second source if more leaf shapes are ever wanted); Wikimedia Commons
  was not needed and was not searched, because a CC0 asset with a shipped
  opacity map beats a photograph that would have to be segmented.
- **Modifications** (reproducible: `Assets/Editor/ivy_pipeline.py`, python3 +
  numpy + Pillow): connected-component analysis of the shipped opacity mask to
  find the six leaves and reject the nine 1–4 px specks that come with it;
  2048 → 1024 by **premultiplied** area mean, so no transparent texel can weight
  into an opaque neighbour; un-premultiply; then a 64-round nearest-opaque
  bleed over the whole transparent region, so mip generation cannot pull a
  black texel into a leaf edge. Result: 1024×1024 RGBA8, 34.7 % coverage above
  the cutoff, 0.98 % of texels partially transparent (a one-texel ramp).
- **Sub-rects**: six, printed by the pipeline script as a `Rect[]` literal and
  pasted into `BuildEnvironmentRooms` at the ivy block (they are card data, not
  part of this file).
- **Raw source not committed**, per this project's standing rule: the zip is
  downloaded, keyed and discarded; only `ivy_alb.png` ships.

### Handprints (`handprints_alb.png`)

- **Asset**: three real photographed handprints, keyed by this project out of
  ONE **Flickr** photograph and re-authored into the three blood-mark tiles of
  the apparition atlas (`Textures/Env_Haunt.png`, tiles 8, 10 and 11).
- **Source**: *“handprints 4”* by **lisafree54** (Lisa Ann Yount) —
  photo page <https://www.flickr.com/photos/136594255@N06/26383403281>,
  original file
  <https://live.staticflickr.com/1679/26383403281_ba8bfae8de_o.jpg>,
  **2730 × 1820**, Canon PowerShot SX260 HS, EXIF intact.
- **License**: **CC0 1.0 Universal** (public domain dedication). No attribution
  required; credited here with thanks. Verified **first-hand at the source on
  2026-08-15**, immediately before download, by fetching the photo page itself.
  Quoted **verbatim** from that page:
  - its embedded JSON — `"title":"handprints 4"`, `"license":9` and
    `"license": "https://creativecommons.org/publicdomain/zero/1.0/"`
    (Flickr licence id **9** is *Public Domain Dedication*, CC0);
  - and its rendered licence link —
    `<a href="https://creativecommons.org/publicdomain/zero/1.0/deed.en" class="license-icons" rel="license noopener noreferrer" title="CC0 (Public Domain Dedication)" target="_newtab">`.
- **The source photograph is NEVER committed.** `handprint_atlas_pipeline.py`
  downloads it and only the derived RGBA PNG lands in the tree — the same
  convention as the fire and cobweb pipelines. `handprints_alb.png` is itself a
  BAKE-TIME INPUT ONLY: `EnvironmentsBuilder.HauntHandTiles` decodes its bytes
  and stamps the result into `Env_Haunt.png`; nothing at runtime samples it, and
  its importer is deliberately pinned to a 32 px maximum for that reason.
- **Why a photograph**: the prints it replaces were built from signed-distance
  capsules — a palm capsule plus finger capsules, min-unioned — and the user's
  verdict was *“Sie sind keine wirklichen Hände … Nutze hier irgendwelche
  Texturen aus dem Internet die tatsächlich Horror verursachen könnten.”* A real
  print carries what that grammar cannot fake and what makes it read as a
  contact rather than a pictogram: **a missing palm arch** (a flat hand touches
  at the heel and the pads; the hollow often does not touch at all), **a
  detached thumb**, **fingers broken into pad segments**, **real gravity drips**,
  and four genuinely different hands. Measured from the file: ink coverage
  8.21 %, mean ink sRGB (175, 86, 69), darkest 5 % of ink sRGB (142, 13, 6),
  and the coverage moves only 8.41 % → 7.80 % across thresholds 0.02–0.15, i.e.
  it keys out of the white paper in **one step**, with no rotoscoping.
- **Modifications** (reproducible:
  `Assets/Editor/handprint_atlas_pipeline.py`, python3 + numpy + Pillow):
  chroma key `R − min(G,B)`; the four prints separated by explicit region
  predicates that are gated against the coverage and bounding box each must
  produce, so a different photograph fails the build loudly; each print scaled
  to **life size** (190 mm adult, 132 mm child) and area-resampled into its own
  256² tile; one print mirrored for handedness and one dragged into a **smear**;
  and — the substantive change — **the colour is thrown away and re-authored**.
  The source is red PAINT, terracotta and far too light. Blood's colour is a
  function of optical depth, so a per-texel thickness is measured from the
  green-channel optical density (de-highlighted by a greyscale closing, whose
  residual becomes the wet specular mask) and mapped through a three-stop ramp —
  thin edge sRGB (0.62, 0.10, 0.06), bulk (0.20, 0.020, 0.014), deepest
  (0.06, 0.005, 0.005) — whose two basis weights are what the atlas actually
  carries.
- **Shape library, same author, same licence, not shipped**: *“handprints 2”*
  — <https://www.flickr.com/photos/136594255@N06/25844671504>, original
  <https://live.staticflickr.com/1447/25844671504_e490989a0e_o.jpg>,
  2080 × 2310, `"license":9` and the same
  `creativecommons.org/publicdomain/zero/1.0/deed.en` link, verified the same
  way on the same date. ~60 real prints on a plank door, adult and child. Used
  only as a reference for what real contact patterns look like; **no pixel of it
  is in this repository**.
- **Searched and rejected** (recorded so a future round need not repeat it): a
  photorealistic *blood-on-masonry* handprint does not exist under CC0. Poly
  Haven (849 textures) and ambientCG (2000 materials) have **zero** blood or
  handprint assets — both catalogues dumped in full. The two best genuine-blood
  handprint photographs on Wikimedia Commons (Ura Tepe, Tajikistan; and a
  menstrual print) are **CC BY-SA** and are therefore rejected under this
  project's standard. Unsplash and Pexels are not CC0 and both carry
  redistribution clauses an extractable bundle would breach. Prehistoric cave
  hand *stencils* are public domain but are the photographic INVERSE of a print
  (pigment sprayed around the hand). Grauman's-Chinese-Theatre-class handprints
  are impressions in cement — relief, no pigment, no silhouette. Commons
  `File:Serbian_bloody_handprint_symbol.svg` is genuinely CC0 and genuinely
  print-shaped but is a live political emblem of the 2024–25 Serbian protests,
  which is a needless association and cheap to avoid.

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
  base/tip feed profile, feathered sideways to zero well inside its cell,
  soft-knee compressed to a ceiling of 0.90–0.95 so no region can saturate in
  the additive pass, given a hard-zero 3.5 % border, and packed into this
  project's own 2×2 cell layout.
  **The result is a re-authored composite: no source image survives in the
  shipped file as delivered, at its delivered size, or in its delivered
  framing.**
- **ModBuild 148 — the RGB channels now carry a DERIVED EROSION FIELD**, and
  this changes what is taken from `T_VFX_Noise_07.tga` rather than what is
  taken from the other three. Up to ModBuild 147 that map was used once, at
  bake time, to punch static holes in the four masks, and RGB was forced to
  white. It is now the source of a two-octave field packed into R and G (R one
  tile over the 512² image, G a 4×4 tiling of a 4× downsample), which
  `EnvFlame.shader` scrolls through the alpha every frame — the animated
  erosion that is the whole of how the supplied pack's own fire moves, and the
  reason the user's ModBuild 147 verdict was "es flackert überhaupt nicht
  natürlich". The static holes are gone; the field replaced them.
  **The derivation is not a copy**: the 256² source is resized, rank-transformed
  to a uniform \[0,1] distribution (destroying its histogram), inverted, tiled,
  and — for G — box-filtered. The shipped R and G are a monotone re-mapping of a
  re-sampled tiling of the source; the source's own tonal identity does not
  survive it. Same grant, same clause (2.2.1 (b), incorporated and embedded),
  same non-transferability. `B` is a constant 1.0 and carries nothing.
- **Nothing new was licensed for ModBuild 148, and the search is recorded here
  because the project has rejected packs on licence grounds before.** The
  question asked was whether a CC0 flame or noise source would improve on what
  is already imported. It would not: the deficiency the user reported is
  *temporal* (a mask that does not dissolve) and *geometric* (cards that hang
  off the props and read as strokes), and neither is a property of the art —
  the same four masks, eroded, are what the supplied pack itself ships. A new
  texture would have added a second sampler to the heaviest-overdraw fragment
  in the room to buy nothing. So: no download, no new source, no new licence
  obligation, and the two `.unitypackage` files in the gitignored drop
  directory remain the only third-party fire input this bundle has ever had.
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
