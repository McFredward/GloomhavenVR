using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE PULSING ACTIVE REGION ON A PEER'S MIRRORED ACTIVE CARDS — the mirror's half of the picture
/// the owner has had since feature 6, and the surface that had no driver of any kind until now.
///
/// <para>USER ITEM 3 (2026-09-07, verbatim): "Der Spieler sieht bei den aktiven Karten pulsierend
/// den Bereich der aktiv ist - das ist aber nicht der Fall beim remote board. Dort soll das auch
/// entsprechend synchronisiert angezeigt werden - auch wenn der Spieler die jeweilige aktive Karte
/// in die Hand nimmt soll das pulsieren sichtbar sein."</para>
///
/// <para>ROOT CAUSE — A MISSING CALLER, NOT A MISSING FACT. Every piece this needs was already on
/// the observing machine and had been for builds. The mirrored active cell hosts a real
/// <c>Object.Instantiate</c> clone of the game's own <c>FullAbilityCard</c> (<c>RemoteCardArt</c>),
/// so it carries both <c>FullAbilityCardAction</c>s and both <c>CardActionHighlight</c>s — the very
/// objects the owner's own <c>VRCard.SetActionHighlight</c> drives. WHICH half is active is a pure
/// function of <c>CCharacterClass.FindCasterActiveBonuses</c> and
/// <c>CAbilityCard.GetAbilityActionType</c>, i.e. of the rules model, which every client simulates
/// for every actor (<see cref="Cards.ActiveCardSet.ActiveHalves"/>). And the write itself has a
/// shared, gated choke point (<see cref="Cards.ActionHighlightDriver"/>). Nothing was missing
/// except a call: <c>RemoteControlBoard</c> drives <c>RemoteBoardCard.SetHalfStates</c> for the two
/// ROUND recesses (record 14's hover/click) and nothing drove anything for the ACTIVE matrix. NO
/// WIRE FIELD IS NEEDED OR WANTED — a bit saying "the top half is active" would be a second, lossy
/// copy of a fact this machine can compute exactly.</para>
///
/// <para>THE IDENTITY COMES FROM THE CELL, NEVER FROM THE CLONE, and that is not a style choice.
/// <c>FullAbilityCard.abilityCard</c> is a plain private field of a non-Unity type with no
/// <c>[SerializeField]</c> (FullAbilityCard.cs:62), and <c>Object.Instantiate</c> copies SERIALIZED
/// state only — so a mirrored clone's <c>AbilityCard</c> property is null and a membership test
/// built on it would be silently inert on every board. The caller therefore hands this class the
/// card it seated in each cell, and the clone is matched back to its cell GEOMETRICALLY, by the
/// cell-local position the layout gave it. Cells are a grid, so those positions are distinct by
/// construction, and a clone that matches none is simply left alone rather than pulsed as somebody
/// else's card.</para>
///
/// <para>WHY THIS IS NOT <c>RemoteBoardCard.SetHalfStates</c>. That method takes ONE hover half
/// (-1/0/1) because record 14's question — which half is my team-mate pointing at — can only ever
/// have one answer. An ACTIVE card can legitimately have BOTH halves lit: a whole-card or
/// unresolvable bonus lights the whole card on the owner's board, and that is the fallback
/// <c>ActiveHalves</c> ends with. Routing through <c>SetHalfStates</c> would light one half of a
/// whole-card bonus and, worse, would put two writers on the same two highlight objects —
/// <c>ApplyHalf</c> pushes <c>Off</c> to the half it is not hovering on every call, so it would
/// switch the second half back off every frame. One surface, one owner.</para>
///
/// <para>THE CARD IN THE FIST IS THE SAME CARD AND TAKES THE SAME EXPRESSION. "auch wenn der
/// Spieler die jeweilige aktive Karte in die Hand nimmt" is not a second mechanism here: the held
/// slab (<c>RemoteHeldCardFace</c>, record 36) hosts a clone through the same <c>RemoteCardArt</c>,
/// so the caller hands that slab in as a second root with the card record 36's SEAT names. It has
/// to be driven there, because the matrix cell of a held card is deliberately BLANK (report item 1
/// of 2026-09-06: the card must not be drawn in the fist AND in the matrix), so without that root
/// the pulse would vanish at the exact moment the user names.</para>
///
/// <para>THE MATERIAL ISOLATION IS NOT OPTIONAL, and the reason is written out in
/// <c>RemoteBoardCard.IsolateHighlightMaterials</c>: <c>CardActionHighlight.ShowHover</c> writes
/// <c>imageHighlight.material.SetFloat("_AngularHighlightWidth", …)</c>, and <c>Graphic.material</c>
/// is the SHARED asset — unlike <c>Renderer.material</c> it does not instantiate. Driving a peer's
/// mirrored card through the shared material would re-write the shine width on the LOCAL player's
/// own cards. Only the BIG half highlights are isolated, because this surface never lights the
/// standard-action chip: the owner's own <c>VRCard.SetActionHighlight</c> passes
/// <c>wantDefault: false</c> for both halves and this passes the same, and <c>Hide()</c> — all the
/// chip ever gets — touches no material. Nothing else drives these two objects on these roots
/// (<c>RemoteControlBoard</c> drives the RECESS slots only), so this class is their sole writer.
/// </para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. Source:
/// <c>CCharacterClass.FindCasterActiveBonuses</c> off the host-replicated actor. The only wire
/// reading anywhere near it is record 36's held SEAT, which the caller already resolves for the
/// matrix's own blanking; no card identity is read from a packet and none is put on one.</remarks>
internal sealed class RemoteActiveCardPulse
{
    /// <summary>How near a clone's cell-local position must be to a seated cell's to be that cell,
    /// in board-local units. The grid step is card width x the owner's spacing — tens of
    /// millimetres — so this is three orders of magnitude below the smallest real gap and can only
    /// ever match float noise.</summary>
    private const float CellEpsilon = 1e-4f;

    /// <summary>Per-clone memory: what this driver last pushed onto each of the clone's two halves,
    /// plus the private highlight materials minted for it. Keyed on the clone's instance id, so a
    /// rebuilt face is a new entry and the old one's materials are destroyed with it.</summary>
    private sealed class Entry
    {
        /// <summary>Last state pushed, per half (0 = bottom, 1 = top) — the driver's gate.</summary>
        public readonly int[] Applied = { int.MinValue, int.MinValue };

        /// <summary>Last REGION pushed, per half. Always false here (see the class note).</summary>
        public readonly bool[] Region = new bool[2];

        /// <summary>Our private copies of the two big highlights' materials, or null where the
        /// prefab has none.</summary>
        public readonly Material?[] Isolated = new Material?[2];

        /// <summary>Touched this pass — the prune key.</summary>
        public bool Seen;
    }

    private readonly Dictionary<int, Entry> _entries = new(8);

    /// <summary>Scratch for the non-allocating component walk; never escapes.</summary>
    private readonly List<FullAbilityCard> _faces = new(8);

    /// <summary>Scratch for the prune, so removing from <see cref="_entries"/> never mutates a
    /// dictionary that is being enumerated.</summary>
    private readonly List<int> _dead = new(4);

    private readonly int _playerId;

    private int _lit;
    private int _faceCount;
    private int _both;
    private int _unmatched;

    internal RemoteActiveCardPulse(int playerId) => _playerId = playerId;

    /// <summary>Open a pass. Every entry is marked unseen so <see cref="End"/> can prune the clones
    /// that have gone.</summary>
    internal void Begin()
    {
        foreach (KeyValuePair<int, Entry> pair in _entries)
            pair.Value.Seen = false;
        _lit = 0;
        _faceCount = 0;
        _both = 0;
        _unmatched = 0;
    }

    /// <summary>
    /// Drive every clone under a ONE-CARD root — a held-card slab. <paramref name="card"/> null
    /// means "whatever is on this slab, it is not one of this actor's active cards", which takes
    /// the highlight back OFF rather than leaving the last one standing.
    /// </summary>
    internal void DriveSingle(Transform? root, CPlayerActor? actor, CAbilityCard? card)
    {
        if (!Collect(root))
            return;
        for (int i = 0; i < _faces.Count; i++)
            ApplyFace(_faces[i], actor, card);
    }

    /// <summary>
    /// Drive every clone under the ACTIVE MATRIX root, matching each back to the cell it sits in.
    /// <paramref name="cellPositions"/> and <paramref name="cellCards"/> are parallel and hold ONLY
    /// the cells the caller actually seated a card in this pass.
    /// </summary>
    internal void DriveMatrix(Transform? root, CPlayerActor? actor,
                              List<Vector3> cellPositions, List<CAbilityCard> cellCards)
    {
        if (!Collect(root))
            return;
        for (int i = 0; i < _faces.Count; i++)
        {
            FullAbilityCard face = _faces[i];
            CAbilityCard? card = null;
            Transform? cell = CellUnder(face, root!);
            if (cell != null)
            {
                Vector3 at = cell.localPosition;
                for (int c = 0; c < cellPositions.Count && c < cellCards.Count; c++)
                {
                    if ((cellPositions[c] - at).sqrMagnitude > CellEpsilon * CellEpsilon)
                        continue;
                    card = cellCards[c];
                    break;
                }
            }
            if (card == null)
                _unmatched++;
            ApplyFace(face, actor, card);
        }
    }

    /// <summary>Close the pass: prune the clones that went away and report.</summary>
    internal void End()
    {
        Prune();
        ReportIfChanged();
    }

    /// <summary>Walk one root for hosted card widgets. Returns false when there is nothing to
    /// drive.</summary>
    private bool Collect(Transform? root)
    {
        _faces.Clear();
        if (root == null)
            return false;
        try
        {
            // includeInactive: FALSE on purpose. A blanked matrix cell (the held-card suppression,
            // and the in-flight hold user item 2 adds beside it) is switched OFF, and a highlight
            // asserted onto an inactive hierarchy is a write nobody can see that would still count
            // in the census.
            root.GetComponentsInChildren(false, _faces);
        }
        catch { return false; }
        _faceCount += _faces.Count;
        return _faces.Count > 0;
    }

    /// <summary>The direct child of <paramref name="root"/> this clone hangs under — one
    /// <c>RemoteBoardCard</c>'s own object, whose local position IS its cell.</summary>
    private static Transform? CellUnder(FullAbilityCard face, Transform root)
    {
        Transform? t = face != null ? face.transform : null;
        // Bounded: the hosted clone is three levels down (cell → RemoteCardArt host → clone) and
        // the cap is only a guard against a hierarchy that is not the one this class expects.
        for (int guard = 0; t != null && guard < 16; guard++)
        {
            if (ReferenceEquals(t.parent, root))
                return t;
            t = t.parent;
        }
        return null;
    }

    private void ApplyFace(FullAbilityCard face, CPlayerActor? actor, CAbilityCard? card)
    {
        bool top = false;
        bool bottom = false;
        if (card != null && ActiveCardSet.IsActive(actor, card))
        {
            ActiveCardSet.ActiveHalves(actor, card, out top, out bottom);
            _lit++;
            if (top && bottom)
                _both++;
        }

        int key;
        try { key = face.GetInstanceID(); }
        catch { return; }
        if (!_entries.TryGetValue(key, out Entry entry))
        {
            entry = new Entry();
            _entries[key] = entry;
            Isolate(face, entry);
        }
        entry.Seen = true;
        try
        {
            // wantDefault:false on both halves — the owner's own VRCard.SetActionHighlight passes
            // exactly that, so the mirror lights the BIG action region and never the chip.
            ActionHighlightDriver.Assert(face.bottomActionButton,
                bottom ? ActionHighlightDriver.Hover : ActionHighlightDriver.Off, wantDefault: false,
                ref entry.Applied[0], ref entry.Region[0],
                ActionHighlightDriver.Site.MirroredActiveMatrix);
            ActionHighlightDriver.Assert(face.topActionButton,
                top ? ActionHighlightDriver.Hover : ActionHighlightDriver.Off, wantDefault: false,
                ref entry.Applied[1], ref entry.Region[1],
                ActionHighlightDriver.Site.MirroredActiveMatrix);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", "Remote active-card pulse: the game's own action highlight could not "
                + $"be driven on a mirrored active card ({ex.Message}) — that card simply does not "
                + "pulse on this client; nothing else on the board is affected.");
        }
    }

    /// <summary>Give this clone's two BIG highlights their own material instance — see the class
    /// note for why writing the shared one would restyle the LOCAL player's cards.</summary>
    private static void Isolate(FullAbilityCard face, Entry entry)
    {
        entry.Isolated[0] = IsolateOne(face.bottomActionButton);
        entry.Isolated[1] = IsolateOne(face.topActionButton);

        static Material? IsolateOne(FullAbilityCardAction? action)
        {
            try
            {
                CardActionHighlight? hl = action != null ? action.highlightAction : null;
                UnityEngine.UI.Image? img = hl != null ? hl.imageHighlight : null;
                Material? shared = img != null ? img.material : null;
                if (img == null || shared == null)
                    return null;
                var owned = new Material(shared) { name = shared.name + " (RemoteActiveCardPulse)" };
                img.material = owned;
                return owned;
            }
            catch { return null; }
        }
    }

    /// <summary>Drop every entry whose clone was not walked this pass and destroy its materials. A
    /// mirrored face is re-instantiated whenever its card changes, so without this the minted
    /// materials would accumulate for the life of the scenario.</summary>
    private void Prune()
    {
        _dead.Clear();
        foreach (KeyValuePair<int, Entry> pair in _entries)
            if (!pair.Value.Seen)
                _dead.Add(pair.Key);
        for (int i = 0; i < _dead.Count; i++)
        {
            if (_entries.TryGetValue(_dead[i], out Entry entry))
                Release(entry);
            _entries.Remove(_dead[i]);
        }
        _dead.Clear();
    }

    private static void Release(Entry entry)
    {
        for (int i = 0; i < entry.Isolated.Length; i++)
        {
            if (entry.Isolated[i] != null)
                Object.Destroy(entry.Isolated[i]);
            entry.Isolated[i] = null;
        }
    }

    /// <summary>Board teardown: every minted material is ours and dies here.</summary>
    internal void Destroy()
    {
        foreach (KeyValuePair<int, Entry> pair in _entries)
            Release(pair.Value);
        _entries.Clear();
    }

    // ------------------------------------------------------------------ the instrument --

    /// <summary>Change key for <see cref="ReportIfChanged"/>.</summary>
    private int _logged = int.MinValue;

    /// <summary>
    /// HARDWARE EVIDENCE for user item 3. Grep token: ACTIVE PULSE MIRROR.
    ///
    /// <para>READ IT AGAINST <c>[Cards] ACTIVE SET</c> for the same character, which says how many
    /// cards that peer HAS active. <c>lit</c> must equal that count; <c>faces</c> is how many
    /// mirrored card clones the pass could reach at all.</para>
    /// <list type="bullet">
    ///   <item><c>lit&gt;0</c> — WORKING, and <c>[Cards] ACTION HIGHLIGHT PULSE</c> will now read
    ///     <c>mirrored active matrix=YES</c> with its own restart and gated counts beside it.</item>
    ///   <item><c>faces&gt;0, lit=0, unmatched=0</c> — the clones were found and matched to cells
    ///     and the MODEL said none of those cards is active. Check the MODEL row of
    ///     <c>ACTIVE SET</c> for that character before looking here.</item>
    ///   <item><c>unmatched&gt;0</c> — a clone could not be traced back to a seated cell, so it was
    ///     left alone. That is the geometric match failing, i.e. the matrix's cell layout and the
    ///     positions handed to this class have drifted apart; it is a defect in the CALLER, and the
    ///     safe behaviour (leave it alone) is what stops it lighting somebody else's half.</item>
    ///   <item><c>faces=0</c> — no hosted <c>FullAbilityCard</c> anywhere: that peer's active cells
    ///     are drawing BACKS or the mod's fallback panel, which have no game highlight to drive.
    ///     That is a face-path finding and belongs to <c>PeerCardFaceCensus</c>.</item>
    /// </list>
    /// <para>Change-gated on all four numbers, so a settled board costs no lines.</para>
    /// </summary>
    private void ReportIfChanged()
    {
        int key = ((_lit * 401 + _faceCount) * 401 + _both) * 401 + _unmatched;
        if (key == _logged)
            return;
        _logged = key;
        if (_lit == 0 && _faceCount == 0)
            return;   // nothing mirrored, nothing to say
        // HW-VERIFY: user item 3 (2026-09-07). Grep token: ACTIVE PULSE MIRROR.
        VRLog.Note("Net", $"ACTIVE PULSE MIRROR [player {_playerId}]: {_lit} of {_faceCount} "
            + $"mirrored card face(s) reachable on this board are being pulsed as ACTIVE ({_both} of "
            + $"them on BOTH halves, which is what a whole-card or unresolvable bonus means and is "
            + $"the owner's own fallback too; {_unmatched} clone(s) matched no seated cell and were "
            + "left alone). THE REGION IS RESOLVED LOCALLY AND NOTHING IS ON THE WIRE: "
            + "ActiveCardSet.ActiveHalves reads CCharacterClass.FindCasterActiveBonuses and "
            + "CAbilityCard.GetAbilityActionType off the host-replicated actor, which is the same "
            + "pair of calls the owner's own CardsGameApi.GetActiveHalves makes for their board. The "
            + "faces counted here are the ACTIVE matrix's cells PLUS that peer's held-card slabs, "
            + "because an active card taken into the fist is blanked in the matrix (report item 1, "
            + "2026-09-06) and must keep pulsing in the hand — 'auch wenn der Spieler die jeweilige "
            + "aktive Karte in die Hand nimmt soll das pulsieren sichtbar sein'. THE CLONE'S OWN "
            + "AbilityCard IS NEVER ASKED and must not be: that field is unserialized, so "
            + "Object.Instantiate leaves it null on every mirrored copy and a membership test built "
            + "on it would read as a working fix while doing nothing. COMPARE lit against the "
            + "active-card count for this character in '[Cards] ACTIVE SET'; they must be equal. The "
            + "PERIOD of the pulse is not this line's question — '[Cards] ACTION HIGHLIGHT PULSE' "
            + "owns that, and its 'mirrored active matrix' clause is this surface's row there.");
    }
}
