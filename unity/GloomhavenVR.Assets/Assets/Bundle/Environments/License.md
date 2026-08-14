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
are from **Poly Haven** (<https://polyhaven.com>) — with TWO exceptions, the
cobweb alpha (TextureCan) and the wall-fungus atlas (Wikimedia Commons), which
have their own sections below — license
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
