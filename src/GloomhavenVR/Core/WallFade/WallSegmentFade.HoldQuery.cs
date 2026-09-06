using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE ENABLE LEDGER — the wall system re-enables ONLY what it disabled (the door-leaf
/// regression, user report 2026-09-03: "die runde Tür verschwindet nicht mehr wenn man die Tür
/// regulär öffnet", runde_tür_problem.jpg).
///
/// <para><b>THE RULE.</b> Every path in this subsystem that writes <c>Renderer.enabled = false</c>
/// records the renderer in <see cref="FadeDriver._hidByEnable"/>; every path that writes
/// <c>Renderer.enabled = true</c> does so only if the ledger says WE turned it off. A renderer
/// the GAME disabled after we adopted it — a door leaf hidden by the door's own 'Open'
/// animation, a prop the rules removed — is never switched back on by an unfade, a handover, the
/// orphan guard or a teardown. Before this ledger <c>RestoreProp</c> ended in an unconditional
/// <c>if (!r.enabled) r.enabled = true;</c> — "restored bit-for-bit to the value captured at
/// adoption" — which is a stale capture the moment the game changes its mind about a renderer the
/// mod merely rides.</para>
///
/// <para><b>WHY A DRIVER-LEVEL SET AND NOT A FLAG ON <see cref="MountedProp"/>.</b>
/// A renderer changes lanes (stacked → mounted, corner → mounted, unit dressing → mounted; the
/// ADOPTED BY THE WRONG WALL line counts these) and each lane may build its own piece object for
/// it. A flag on the piece would be lost in the handover and the piece would stay hidden after
/// its new owner's restore. The ledger is keyed on the renderer itself, so it survives every
/// handover and every re-created piece, and it is pruned of dead keys once per rescan.</para>
///
/// <para><b>WHAT THIS DOES NOT CHANGE.</b> Property blocks, material swaps and particle modules
/// are restored exactly as before — those are values the mod wrote and only the mod writes. Only
/// the <c>enabled</c> bit gains the "did we write it" test, because it is the one field the game
/// also writes on renderers the mod holds a rule on.</para>
///
/// <para>MULTIPLAYER: local presentation only, as the whole wall system is.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    /// <summary>Which wall-fade table (if any) currently holds a rule on <paramref name="r"/> —
    /// a short lane tag, or null when no lane owns, drives or has written it. Read-only; used
    /// by <see cref="DoorOpenWatch"/> to answer "does the mod hold a rule on this door leaf".</summary>
    internal static string? DescribeHold(Renderer r) => _driver != null ? _driver.DescribeHoldOn(r) : null;

    /// <summary>Is <paramref name="r"/> currently held OFF by the wall system — i.e. the wall fade
    /// wrote <c>enabled = false</c> and has not restored it yet? The healer's "don't win a write war"
    /// term (Core/MaterialLoaderHeal.cs): a renderer in the ledger is disabled ON PURPOSE by this
    /// mod and must not be switched back on by another of its subsystems.</summary>
    internal static bool IsHeldHiddenByEnable(Renderer r) => _driver != null && _driver.IsHeldHidden(r);

    // THERE IS NO `HideByEnableExternal` / `ShowIfWeHidExternal` ANY MORE, AND THERE MUST NOT BE
    // ONE AGAIN WITHOUT THE FLOOR GUARD ON IT.
    //
    // They existed for ONE caller — Core/Environment/DoorOpenWatch.cs hiding an opened door leaf
    // — and that hide was deleted outright in ModBuild 429 by a user ruling ("entferne jegliche
    // workarounds die du eingebaut hattest mit dem deaktivieren"). It is not coming back, so from
    // 429 until now the pair sat here with ZERO callers anywhere in src/ or tests/.
    //
    // WHY THAT WAS A HAZARD AND NOT A TIDY-UP. Each of them carried a NO-DRIVER branch that wrote
    // the enable bit DIRECTLY — `r.enabled = false` / `r.enabled = true` — around
    // FadeDriver.HideByEnable and therefore around the floor rule that lives on it. The file
    // summary below and WallSegmentFade.Floor.cs both state that HideByEnable is the ONLY
    // `Renderer.enabled = false` in the subsystem and that every enable-delivery lane is covered
    // by that one line. With this pair present that was true only by accident of nobody calling
    // them: the moment anything wired one up, a floor tile could be hidden with no floor test and
    // no ledger row — invisible, and invisible to the restitution sweep as well, which reads
    // _hidByEnable. ModBuild 431 has just spent a round on exactly that class of defect (a
    // refusal with no restitution behind it), so the fifth write path goes rather than waits.
    //
    // If an outside caller ever needs this again: route it through FadeDriver.HideByEnable, which
    // is guarded and ledgered, and give the no-driver case the same floor test — never a bare
    // enable write.

    private sealed partial class FadeDriver
    {
        /// <summary>Renderers whose <c>enabled</c> bit THIS driver switched off and has not yet
        /// switched back on. See the file summary. Keyed on the renderer, not the piece.</summary>
        private readonly HashSet<Renderer> _hidByEnable = new(256);

        /// <summary>How many restore paths declined to re-enable a renderer because the ledger
        /// said the mod never disabled it — i.e. the GAME turned it off while we held a rule on
        /// it. Reported by <see cref="DescribeHoldOn"/>'s caller lines; a non-zero count on a door
        /// leaf is the stale-restore defect this ledger exists to prevent, caught in the act.</summary>
        private int _declinedForeignEnables;

        /// <summary>Write <c>enabled = false</c> and remember that we did.</summary>
        internal void HideByEnable(Renderer r)
        {
            if (r == null)
                return;
            // FLOOR NEVER FADES (user 2026-09-05, fehlende_boden_tiles.jpg) — write primitive 3 of
            // 4, and the only `Renderer.enabled = false` in the whole subsystem, so every
            // enable-delivery lane is covered by this one line. Nothing is added to the ledger
            // either: we did not hide it, so no restore path may ever claim we did.
            if (FloorNeverFades(r))
                return;
            // A PROP IN A HAND NEVER FADES (user 2026-09-06) - write primitive 3 of 4. Same
            // contract as the floor term above, including the part that is easy to miss: nothing
            // is added to the ledger either, because we did not hide it and no restore path may
            // ever claim we did.
            if (HeldNeverFades(r))
                return;
            if (r.enabled)
                r.enabled = false;
            _hidByEnable.Add(r);
        }

        /// <summary>Write <c>enabled = true</c> ONLY if we were the one who wrote false. Returns
        /// true when the renderer was switched on by this call.</summary>
        internal bool ShowIfWeHid(Renderer r)
        {
            if (r == null)
                return false;
            bool ours = _hidByEnable.Remove(r);
            if (r.enabled)
                return false;
            if (!ours)
            {
                _declinedForeignEnables++;
                return false;
            }
            r.enabled = true;
            return true;
        }

        /// <summary>Drop dead keys (renderers destroyed by an Apparance rebuild or a scene
        /// change). Once per rescan cycle; the set is small and the walk is one null test per key.</summary>
        private void PruneHidLedger()
        {
            if (_hidByEnable.Count == 0)
                return;
            _hidByEnable.RemoveWhere(static x => x == null);
        }

        internal bool IsHeldHidden(Renderer r) => r != null && _hidByEnable.Contains(r);

        internal string? DescribeHoldOn(Renderer r)
        {
            if (r == null)
                return null;
            if (_mountedTouched.ContainsKey(r))
                return "mounted:driven";
            if (_mountedOwned.Contains(r))
                return "mounted:owned";
            if (_attachmentOwned.ContainsKey(r))
                return "attachment";
            if (_stackedOwned.Contains(r))
                return "stacked";
            if (_propUnitTouched.Contains(r))
                return "prop-unit";
            if (_unitDressingOwned.Contains(r))
                return "unit-dressing";
            if (_freeListed.Contains(r))
                return "free-standing";
            if (_fadeWrittenSet.Contains(r))
                return "fade-written";
            if (_solidOwners.ContainsKey(r))
                return "solid-owner";
            if (_hidByEnable.Contains(r))
                return "hid-by-enable";
            return null;
        }

        /// <summary>Restore paths that declined a foreign enable so far this session — read by the
        /// door watch's sample line so a stale-restore attempt on a door leaf is visible.</summary>
        internal int DeclinedForeignEnables => _declinedForeignEnables;
    }

    /// <summary>See <see cref="FadeDriver.DeclinedForeignEnables"/>.</summary>
    internal static int DeclinedForeignEnables => _driver != null ? _driver.DeclinedForeignEnables : 0;
}
