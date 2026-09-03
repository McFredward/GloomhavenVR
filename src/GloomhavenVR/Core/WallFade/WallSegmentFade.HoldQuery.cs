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
        private void HideByEnable(Renderer r)
        {
            if (r == null)
                return;
            if (r.enabled)
                r.enabled = false;
            _hidByEnable.Add(r);
        }

        /// <summary>Write <c>enabled = true</c> ONLY if we were the one who wrote false. Returns
        /// true when the renderer was switched on by this call.</summary>
        private bool ShowIfWeHid(Renderer r)
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
