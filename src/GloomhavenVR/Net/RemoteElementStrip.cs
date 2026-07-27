using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Element infusions ("Elemente") — GLOBAL
// =================================================================================================

/// <summary>
/// The element infusion board, drawn in the LEFT column below the objectives — the mirror of the
/// local board's docked <c>ElementBoardSurface</c> (same <c>PlayTray.ElementMountBase</c> offset).
///
/// SOURCE (global, zero wire): <c>ElementInfusionBoardManager.ElementColumn(EElement)</c>, the exact
/// static the game's own <c>InfusionBoardUI.UpdateBoard</c> reads to decide each chip's state. The
/// infusion table is scenario-wide and identical on every client, so this needs no traffic and
/// reveals nothing.
///
/// PRESENTATION mirrors vanilla: an INERT element is not drawn at all (InfusionBoardUI
/// <c>SetActive(false)</c>s it), STRONG draws at full colour, WANING dimmed and smaller — which is
/// what the game's strong/waning sprite pair conveys. Chip colours come from the game's own
/// <c>UIInfoTools.GetElementHighlightColor</c> when that singleton is up, with a hardcoded fallback
/// so the strip still reads in the menu/loading window where UIInfoTools is absent.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ElementInfusionBoardManager.ElementColumn</c>. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteElementStrip
{
    private const float MountX = -RemoteControlBoard.BoardHalfW - 0.012f;
    private const float Width = 0.26f;
    private const float ChipSize = 0.030f;
    private const float ChipStep = 0.038f;

    /// <summary>Column Y below the objectives dock — mirrors PlayTray.ElementMountBase
    /// (ObjectivesMountMaxHeight/2 + 0.012 + ElementMountMaxHeight/2 = 0.16 + 0.012 + 0.06).</summary>
    private const float MountY = -0.232f;

    private static readonly Color[] Fallback =
    {
        new(0.95f, 0.40f, 0.15f), // Fire
        new(0.45f, 0.80f, 1.00f), // Ice
        new(0.72f, 0.80f, 0.86f), // Air
        new(0.45f, 0.72f, 0.30f), // Earth
        new(1.00f, 0.95f, 0.58f), // Light
        new(0.56f, 0.40f, 0.82f), // Dark
    };

    private readonly Transform _root;
    private readonly MeshRenderer[] _chips = new MeshRenderer[6];
    private readonly Material[] _mats = new Material[6];
    private int _signature = -1;

    /// <summary>How many non-inert elements the strip currently draws (diagnostics).</summary>
    public int ActiveCount { get; private set; }

    public RemoteElementStrip(Transform boardRoot)
    {
        _root = new GameObject("Elements").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(MountX - Width * 0.5f, MountY, RemoteControlBoard.ProudZLocal);

        for (int i = 0; i < 6; i++)
        {
            _mats[i] = BoardVisual.Unlit(Fallback[i]);
            _chips[i] = BoardVisual.Quad(_root, $"Element_{(ElementInfusionBoardManager.EElement)i}",
                new Vector2(ChipSize, ChipSize), _mats[i]);
            _chips[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Re-read the infusion table and repaint on an actual change.</summary>
    public void Refresh()
    {
        int sig = 0;
        int visible = 0;
        var state = new ElementInfusionBoardManager.EColumn[6];
        for (int i = 0; i < 6; i++)
        {
            ElementInfusionBoardManager.EColumn col;
            try { col = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { col = ElementInfusionBoardManager.EColumn.Inert; }
            state[i] = col;
            sig = sig * 3 + (int)col;
            if (col != ElementInfusionBoardManager.EColumn.Inert)
                visible++;
        }
        if (sig == _signature)
            return;
        _signature = sig;
        ActiveCount = visible;

        // Pack the visible chips left-to-right and centre the run, exactly like the game's own
        // horizontal element holder does with its layout group.
        float left = -(visible - 1) * 0.5f * ChipStep;
        int slot = 0;
        for (int i = 0; i < 6; i++)
        {
            bool on = state[i] != ElementInfusionBoardManager.EColumn.Inert;
            if (_chips[i].gameObject.activeSelf != on)
                _chips[i].gameObject.SetActive(on);
            if (!on)
                continue;
            bool strong = state[i] == ElementInfusionBoardManager.EColumn.Strong;
            Color c = ColorFor((ElementInfusionBoardManager.EElement)i, i);
            _mats[i].color = strong ? c : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.80f);
            float s = strong ? ChipSize : ChipSize * 0.74f;
            _chips[i].transform.localScale = new Vector3(s, s, 1f);
            _chips[i].transform.localPosition = new Vector3(left + slot * ChipStep, 0f, 0f);
            slot++;
        }
    }

    private static Color ColorFor(ElementInfusionBoardManager.EElement e, int index)
    {
        try
        {
            if (UIInfoTools.Instance != null)
                return UIInfoTools.Instance.GetElementHighlightColor(e, 1f);
        }
        catch { /* menu / loading window — fall through */ }
        return Fallback[index];
    }

}
