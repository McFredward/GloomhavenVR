# Town service hardware follow-up — ModBuild 560

The build-559 evidence is the seven screenshots in `.planning/debug/npc_probleme/`
and the current local logs in `.planning/debug/`. They show the temple visit but
no native donation callback. The purse's visible body and the previous release
point were offset, so the player could lower the visible sack into the bowl while
its tested origin remained outside. The original temple book geometry also
extended over a shorter page span than the backing calculation assumed, leaving
native inscription text on narrow disconnected patches.

The release point now follows the body of the original purse. A transparent
non-interactive clone of that purse floats over the original donation bowl while
an eligible purse is held; it has no gameplay collider or payment callback.
The original native blessing confirmation still owns payment and its guarded
callback. The book skin is fitted to the original page triangles.

The merchant's public cabinet, category, page and card movement have one elected
author. A visitor follows that public state before claiming the cabinet; the
visitor's locally hidden card controls are unavailable until public rack state
arrives. The buyer's original confirmation title, text and buttons remain one
private owner-authored presentation that every peer can see, but inert observer
clones cannot buy or cancel. An actual local inventory change after the native
confirmation is required for a buy/sell voice reaction. The offer greeting is
triggered by an opened original confirmation; a rejected hover never triggers it.

The resident voices use original English lines generated offline for this mod,
one consistent voice per NPC. Three earlier greetings were reused; eight new
lines required eight pay-as-you-go calls, with a listed-price estimate of
USD 0.0255. Speech, age and mouth shape are carried by the elected resident
author; a visitor's confirmation queues only a bounded cosmetic reaction for
that author. No speech packet can perform a native gameplay action. Prayer is
quiet and spatial, narration takes precedence, and the coin foley is softened.

Merchant attention finishes the current coin movement before both hands turn
to the visitor. The enchantress cycles three deterministic spell poses and
effects. Cloth follows its station geometry and shared hand/head contact,
including observer playback. The cabinet's crank has a mounted bracket and
irregular wear; coins are seated on the tabletop. The priestess ear and book
have revised geometry and texture. The Windows town asset bundle includes the
new sound and timing files.

Source-bound tests cover the donation decision, native confirmation, original
prompt mirroring, cabinet authority and relay ordering; Unity bundle and strict
Release checks are run before this build is handed to a headset. Automated
checks do not verify real headset sound level, eye/ear appearance, cloth touch
or a completed online donation. A multiplayer hardware pass must verify the
shared merchant cabinet under simultaneous visitors, a category/page change,
and a buyer's confirmation viewed by another player.

The rebuilt Windows bundle is `prebuilt/ghvr-town.bundle` (UnityFS format 7,
Unity 2021.3.5f1, 96,867,229 bytes). Its SHA-256 is
`ff4621544fd4bb0e5c3528d09fef765c5d7ca16a2073249deec5cd3c7351b8dd`.
The native ear remains partly occluded by the priestess's original hood from
some viewpoints; no headset evidence establishes its appearance at close range.

The final local guard run passed all 14 source gates, 77/77 local suites and
286,501 wire assertions. Strict Release compilation passed with zero warnings
and errors. `refactor-guard.sh check --summary` exits 1 only for its historical
compiled snapshot comparison: that inherited snapshot predates the NPC feature
and contains none of its new types. This is a scoped baseline difference, not
a failed test or a claim of headset parity.
