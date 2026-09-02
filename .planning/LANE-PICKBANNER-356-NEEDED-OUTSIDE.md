# Pick-placard lane (base ModBuild 356) — item 12, and the one change that belongs to another lane

This lane owns `Core/TmpFit.cs`, `Cards/Tray/PlayTray.{1.Core,5.Status}.cs` and
`Net/Remote/RemotePickBanner.cs`. Everything below the fold is a patch to a file this lane does
NOT own and has NOT applied.

**This document supersedes item 12 of `.planning/LANE-MODAL-351-NEEDED-OUTSIDE.md`.** That
document's *fix* was applied here almost verbatim and is good. Its *cause* was wrong, and the
correction is the whole point of this file.

---

## What the screenshot actually shows

`.planning/debug/abgeschnittener-text.jpg`, user item 12: *"Beim Test hatte die Anweisung so einen
abgeschnitten Text oben"*. The placard reads, over two centred lines:

> Testi: Alle Karten liegen — mit der Board-Taste abschließen
> (oder eine Karte zum Tauschen zur

The recorded diagnosis read this as text overflowing its plate: two legible lines and a third drawn
below the parchment onto the dark board. **It is not.** Brightening the region under the plate
shows nothing there — no third line, no ink on the board, and empty parchment below line 2. The
sentence simply *ends* at "zur", mid-word, and line 2 is centred and complete as laid out.

TMP's word wrapping cannot break a word that fits on a line, so a mid-word stop cannot come from
the layout. It comes from the wire:

```
full   : "Testi: Alle Karten liegen — mit der Board-Taste abschließen (oder eine Karte zum Tauschen zurücknehmen)"
         103 chars / 107 bytes UTF-8
capped : "Testi: Alle Karten liegen — mit der Board-Taste abschließen (oder eine Karte zum Tauschen zur"
          93 chars /  96 bytes UTF-8
```

96 bytes is `NetProtocol.PickBannerTextMaxBytes`, and `PresenceState.EncodePickBannerText` drops
whole characters off the end until the encoding fits — no ellipsis, no flag, no log line. The
visible string is **byte-exactly** what that codec produces from the full German line. The placard
in the screenshot is therefore a **peer's mirror** (`Net.RemotePickBanner`, extension record 7);
the owner's own placard is never capped and shows the full sentence.

So the user's report is a WIRE defect wearing a layout defect's clothes.

## What is fixed inside this lane (committed here)

The placard is now sized from what its label actually drew — grow-only, measured with
`ForceMeshUpdate` + `GetRenderedValues`, never shrinking below the authored 0.44 × 0.055 m. That
is a real defect on its own (the plate and the text box were two hard-coded sizes and nothing
measured the string), and it is a **prerequisite** for the change below: raising the cap sends a
longer sentence, and a longer sentence is exactly what would have been drawn off the old fixed
plate. Owner and mirror now read ONE set of constants (`PlayTray.PickPlateSize` / `PickTextBox` /
`PickMaxFont` / `PickPlatePadding`) instead of two hand-copied sets.

**The WIDTH is unchanged and should stay unchanged.** The screenshot's right-edge "cut" is the same
single defect as the bottom one — the sentence ended there — not a line too wide for a 0.42 m box.
With word wrapping on, a line can only exceed the box if a single WORD does, which no word in this
string comes close to. Widening the box would also destabilise the wrapping the placard's whole
look depends on.

---

## NEEDED OUTSIDE THIS LANE — one constant

### `src/GloomhavenVR/Net/NetProtocol.cs` (~line 17143)

Replace:

```csharp
    /// <summary>UTF8 byte cap for <see cref="ExtIdPickBanner"/>. The composed line is one short
    /// sentence; the cap bounds a single extras record and is re-clamped on read (never trust the
    /// wire). Truncation is on a UTF8 CHARACTER boundary, never mid-sequence.</summary>
    public const int PickBannerTextMaxBytes = 96;
```

with:

```csharp
    /// <summary>
    /// UTF8 byte cap for <see cref="ExtIdPickBanner"/>. The cap bounds a single extras record and
    /// is re-clamped on read (never trust the wire); truncation is on a UTF8 CHARACTER boundary,
    /// never mid-sequence.
    ///
    /// <para><b>IT WAS 96, AND 96 WAS THE 2026-09-02 DEFECT (user item 12, "so einen abgeschnitten
    /// Text").</b> "one short sentence" was measured against the ENGLISH strings. The German
    /// <c>pick_confirm_hint</c> composed with a character name is 107 bytes — "Testi: Alle Karten
    /// liegen — mit der Board-Taste abschließen (oder eine Karte zum Tauschen zurücknehmen)" — so
    /// every peer saw it stop at "…zum Tauschen zur", byte-exactly the 96-byte cut, with nothing
    /// anywhere saying so. A cap that silently deletes the end of a sentence is the ModBuild 281
    /// truncation defect one layer down, and the standing ruling is the same one:
    /// <i>"Der Text muss immer voll lesbar sein."</i></para>
    ///
    /// <para>160 B is the same budget the decision-button lines already carry
    /// (<see cref="DecisionLinesMaxBytes"/>), leaves ~50 B of headroom over the longest composed
    /// German line, and stays BELOW <see cref="TooltipTextMaxBytes"/> (192), which a wire-suite
    /// assertion requires. The record's write site is length-guarded against the packet buffer
    /// like every other extras record, so a longer line can only be dropped, never overrun.</para>
    /// </summary>
    public const int PickBannerTextMaxBytes = 160;
```

**Why not simply "no cap":** the record's length prefix is one byte, so 255 is the framing ceiling;
a bounded record is also what lets the reader re-clamp without trusting the sender.

### Verification, all headset-free

* `tests/GloomhavenVR.WireTests/GoldenVectors.cs:960-964` builds its long line FROM the constant, so
  it follows automatically; `:1763` asserts `TooltipTextMaxBytes > PickBannerTextMaxBytes`, which
  192 > 160 keeps true. `./scripts/wire-tests.sh` must still report **204785 assertions passed**.
* `src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs:2061` prints the cap in a privacy note; it reads
  the constant, so it re-words itself.

### Cross-version note (for whoever lands it)

A peer running an OLDER build clamps the incoming length to ITS 96 with
`Math.Min(len, PickBannerTextMaxBytes)` — a clamp by BYTES, so it can land mid-UTF8-sequence and
render a replacement glyph. That is a mismatched-build case the version dialog already covers, and
it is strictly no worse than today's silent cut; it is recorded here so it is not rediscovered.

### The instrument that will tell you it worked

`Net/Remote/RemotePickBanner.Apply` now logs, at Alert (printed at the shipped default), whenever a
received line arrives AT the cap — the signature of a line that was cut down to it. Once the
constant moves, **that line must stop appearing** for this string. It is the one line that decides
item 12.
