# Steam Frame shortcut artwork

These five PNGs are for the separate `GloomhavenVR` Steam shortcut. They are
based on Gloomhaven's official Steam artwork (app 780290), with its title
changed to the project's existing GloomhavenVR wordmark. The artwork was edited
with the built-in GPT image generator on 2026-09-30. The icon is a higher
resolution reconstruction of the official 32 × 32 community icon; it contains
no title to replace.

| Output | Pixels | Source image |
| --- | ---: | --- |
| `library_600x900.png` | 600 × 900 | `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/780290/library_600x900_2x.jpg` |
| `library_header.png` | 920 × 430 | `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/780290/library_header_2x.jpg` |
| `library_hero.png` | 3840 × 1240 | `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/780290/library_hero_2x.jpg` |
| `logo.png` | 1280 × 720, alpha | `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/780290/logo_2x.png` |
| `icon.png` | 256 × 256 | `https://shared.fastly.steamstatic.com/community_assets/images/apps/780290/2291d20d20db087c989341397181b511f0cb99e4.jpg` |

All edits used `src/GloomhavenVR/Assets/GloomhavenVR_logo.png` as the second
reference image. The generated output was fitted to Steam's artwork sizes with
Lanczos resampling; the narrow white generator boundary was removed from the
hero before fitting. The original Steam files and generated full-resolution
images are retained for review in `.planning/debug/steam_artwork_gloomhaven_780290/`.

The generator was instructed to change only the existing title, preserve the
original composition and characters, and avoid added elements. As with any
generative edit, the resulting non-title pixels should be inspected visually
against the originals before a public release.
