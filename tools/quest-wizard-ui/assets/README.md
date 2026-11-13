# Builder branding assets

`gloomhavenvr-logo.png` is an unchanged copy of the project's `docs/img/logo.png`.

`meta-quest-logo.svg` is the unchanged 747 × 96 Meta Quest wordmark SVG from
[Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Meta_Quest_logo.svg),
whose file page identifies Oculus as the original source and publishes it under
CC0 1.0. The wordmark identifies the target headset; Meta retains its trademarks.
The UI presents the SVG in white on its dark background using CSS,
without rewriting its vector paths. It includes no scripts, external references
or embedded images. This logo was also visually compared with the wordmark on
[Meta's Quest page](https://www.meta.com/quest/) on 2026-10-06; the latter's public
174 × 26 bitmap would be too small for the builder heading.

`provenance.json` records the exact source URL, hash, byte count and presentation
for the copied logos. The slideshow's twenty-one public publisher images have their
own [provenance notes](promo/README.md) and hash/dimension manifest. Neither logos
nor slideshow pictures are extracted from the user's installed game.

The two footer link icons are exact locally pinned SVG copies. The GitHub mark
is the official Primer Octicons `mark-github-16` (MIT; complete license in
`octicons-LICENSE.txt`). The cup is Buy Me a Coffee's own button artwork, linked
from its brand-kit ecosystem. Their source URLs, hashes and byte sizes are in
`provenance.json`. GitHub and Buy Me a Coffee retain their respective trademarks.
GitHub's black icon is presented white through CSS; the yellow cup is unchanged.
The links use the repository and maintainer support URLs from the project README.
They do not embed a remote widget, tracking script, font or remote image request.
