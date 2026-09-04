using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// IS EVERY CONTROL THE WINDOW OWNS INSIDE THE RECTANGLE THE MOD COMMITTED FOR IT?
///
/// <para>USER REPORT (2026-09-04, hardware, multiplayer, map environment), verbatim: <i>"3) Zwei
/// DEADLOCKS im Multiplayer in der Map-Umgebung: a) Der Button mit dem ein Szenario starten kann
/// ist nicht im Fenster - man kann also effektiv kein Szenario starten! … Beides sind kritische
/// Fehler die dringend behoben werden müssen."</i></para>
///
/// <para>THE BAR THIS EXISTS TO ENFORCE IS ABSOLUTE: a window the mod floats must show every
/// control the flat game shows. Up to ModBuild 421 nothing in the mod could ANSWER that question.
/// The host-rect fit prints the union of what is DRAWN and whether it clamped
/// (<c>Host rect fit …: 512x826 → 512x801 px … union (-296,-388)..(257,396) px [frame-clamped]</c>,
/// ModBuild 420 host Player.log:45367-46545) and the hit-rect line prints the laser plane, but the
/// union is a union: one control sitting far outside it is invisible to a number that is the
/// OUTER BOUND of everything inside. "One contributor is not the union", and the converse holds
/// too — a union that fits says nothing about the member that does not. So the question this class
/// asks is per-control and the answer NAMES the offenders, because "a control is missing" must be
/// answerable from the log without the user telling us which one.</para>
///
/// <para>WHAT IS MEASURED. Every <see cref="Selectable"/> under the converted window's own target
/// rect — buttons, toggles, scrollbars, input fields; the game builds every clickable control in
/// this UI out of one — that is ACTIVE in the hierarchy this tick. Each one's four world corners
/// are brought into the HOST rect's local space (the rectangle
/// <see cref="ConvertedPanel.HostRect"/> committed, which is the rectangle the panel actually
/// draws and the rectangle <c>RayUguiDriver</c> intersects) and compared against
/// <c>HostRect.rect</c>. Three outcomes per control: fully inside, straddling an edge, or wholly
/// outside. The last two are the report.</para>
///
/// <para>CORNERS AND NOT <c>rect</c>, deliberately, and for the reason this project has written
/// down twice: a control may sit several transforms deep under layout groups with their own scales
/// and pivots, so its own <c>rect</c> is expressed in a parent's space and comparing it with the
/// host's is comparing two different coordinate systems. <c>GetWorldCorners</c> +
/// <c>InverseTransformPoint</c> is the only form that is right at every depth.</para>
///
/// <para>AN INACTIVE CONTROL IS NOT COUNTED AS MISSING, and that is a decision rather than an
/// oversight. The game deactivates controls it does not want shown — online it does exactly that to
/// the single-player travel button (<c>travelButton.gameObject.SetActive(!FFSNetwork.IsOnline)</c>,
/// decompiled AdventureMapUIManager.cs:410-412) — and reporting those as "missing from the window"
/// would drown the real ones. The COUNT of them is printed instead, so a control the game switched
/// off is visible as a number without being accused.</para>
///
/// <para>BOUNDED. Called once per <c>MODAL LIVENESS CENSUS</c> window (20 s) over the floated set,
/// never per frame, and it allocates nothing per call beyond the string it prints: the sweep list,
/// the corner array and the builder are all reused static scratch.</para>
/// </summary>
internal static class FloatedControlCoverage
{
    private const string Scope = "WorldUI";

    /// <summary>How many offenders are NAMED on one line. The count is always exact; the names are
    /// capped because a genuinely broken window can carry dozens and a truncated list that does not
    /// say it is truncated has already produced a wrong reading in this project ("a truncated list
    /// is not absence"). The line therefore prints the total AND how many names were withheld.
    /// </summary>
    private const int NameCap = 8;

    /// <summary>How far outside the host rect still counts as inside, in the host's own uGUI units.
    /// A control whose edge lands a fraction of a pixel out is a rounding artefact of the corner
    /// transform, not a control the player cannot reach.</summary>
    private const float EdgeEpsilon = 1f;

    private static readonly List<Selectable> Scratch = new(128);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly StringBuilder Names = new(256);

    /// <summary>
    /// One clause describing this panel's control coverage, or <see cref="string.Empty"/> when the
    /// panel cannot be measured this tick (no host rect, no target, both of which are ordinary
    /// during a conversion and are not worth a line).
    /// </summary>
    internal static string Describe(string label, ConvertedPanel? panel, out int unreachable)
    {
        unreachable = 0;
        if (panel == null)
            return string.Empty;
        RectTransform host = panel.HostRect;
        RectTransform target = panel.Target;
        if (host == null || target == null)
            return string.Empty;

        Scratch.Clear();
        target.GetComponentsInChildren(includeInactive: true, Scratch);
        if (Scratch.Count == 0)
            return string.Empty;

        Rect frame = host.rect;
        int active = 0, inactive = 0, inside = 0, straddling = 0, outside = 0, named = 0;
        Names.Clear();

        for (int i = 0; i < Scratch.Count; i++)
        {
            Selectable sel = Scratch[i];
            if (sel == null)
                continue;
            if (!sel.gameObject.activeInHierarchy)
            {
                inactive++;
                continue;
            }
            if (sel.transform is not RectTransform rt)
                continue;
            active++;

            rt.GetWorldCorners(Corners);
            float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
            for (int c = 0; c < 4; c++)
            {
                Vector3 p = host.InverseTransformPoint(Corners[c]);
                if (p.x < xMin) xMin = p.x;
                if (p.x > xMax) xMax = p.x;
                if (p.y < yMin) yMin = p.y;
                if (p.y > yMax) yMax = p.y;
            }

            bool anyIn = xMax > frame.xMin + EdgeEpsilon && xMin < frame.xMax - EdgeEpsilon
                         && yMax > frame.yMin + EdgeEpsilon && yMin < frame.yMax - EdgeEpsilon;
            bool allIn = xMin >= frame.xMin - EdgeEpsilon && xMax <= frame.xMax + EdgeEpsilon
                         && yMin >= frame.yMin - EdgeEpsilon && yMax <= frame.yMax + EdgeEpsilon;
            if (allIn)
            {
                inside++;
                continue;
            }
            if (anyIn) straddling++;
            else outside++;

            if (named >= NameCap)
                continue;
            named++;
            if (Names.Length > 0)
                Names.Append(", ");
            // WHICH EDGE AND BY HOW MUCH, in the host's own uGUI units — the same unit the fit line
            // and the hit-rect line print, so the three can be read together without conversion.
            float overL = frame.xMin - xMin, overR = xMax - frame.xMax;
            float overD = frame.yMin - yMin, overU = yMax - frame.yMax;
            Names.Append('\'').Append(sel.name).Append("' (")
                 .Append(anyIn ? "straddles" : "WHOLLY OUTSIDE").Append(", interactable=")
                 .Append(sel.IsInteractable()).Append(", over L")
                 .Append(Mathf.Max(0f, overL).ToString("F0")).Append(" R")
                 .Append(Mathf.Max(0f, overR).ToString("F0")).Append(" D")
                 .Append(Mathf.Max(0f, overD).ToString("F0")).Append(" U")
                 .Append(Mathf.Max(0f, overU).ToString("F0")).Append(" px)");
        }
        Scratch.Clear();

        if (active == 0)
            return string.Empty;

        int missed = straddling + outside;
        unreachable = missed;
        return $"{label}: {active} active interactive control(s) ({inactive} the game has switched "
               + $"off, not counted as missing), host rect {frame.width:F0}x{frame.height:F0} px "
               + $"spanning x {frame.xMin:F0}..{frame.xMax:F0} and y {frame.yMin:F0}..{frame.yMax:F0}; "
               + $"{inside} fully INSIDE it, {straddling} straddling an edge, {outside} WHOLLY "
               + $"OUTSIDE — "
               + (missed == 0
                   ? "every control this window owns is reachable inside the committed rect"
                   : $"{Names}"
                     + (missed > named ? $" (+{missed - named} more not named)" : string.Empty))
               + ".";
    }

    /// <summary>
    /// Emit the coverage line for one floated panel, if there is anything to say. Silent when every
    /// control is inside — a line that reads "0 of 0 missing" every 20 s for a whole session trains
    /// a reader to skip the one that says something, which is the same rule the liveness census
    /// already applies to itself. A window whose controls ARE all inside is still evidence: it
    /// appears in the census's own count of panels examined.
    /// </summary>
    /// <returns>True when a line was printed.</returns>
    internal static bool ReportIfIncomplete(string panelName, ConvertedPanel? panel)
    {
        // THE VERDICT IS A COUNT, NOT A SUBSTRING. An earlier draft decided whether to print by
        // searching this class's own sentence for a phrase — a claim measuring itself, and one
        // that a harmless rewording of the prose would silently turn off forever.
        string clause = Describe($"MODAL CONTROL COVERAGE '{panelName}'", panel, out int unreachable);
        if (clause.Length == 0 || unreachable == 0)
            return false;
        // HW-VERIFY: report 3a — "Der Button mit dem ein Szenario starten kann ist nicht im Fenster".
        VRLog.Alert(Scope, clause
                           + " THIS IS THE 3a FALSIFIER. The bar is absolute: a window this mod "
                           + "floats must show every control the flat game shows, so a non-zero "
                           + "straddling or WHOLLY OUTSIDE count is a defect and not a tolerance. "
                           + "READ IT AGAINST THE FIT LINE, WHICH CANNOT SEE THIS: 'Host rect fit' "
                           + "prints the UNION of what is drawn, and a union is an outer bound — a "
                           + "control far outside it is invisible to it, and a control the game had "
                           + "not rendered yet when the one-shot fit committed is not in it at all "
                           + "(the 420 log's own fit rejected '25 culled/disabled, 4 faint, 1 "
                           + "clipped out'). The offsets above are in the host's own uGUI px, the "
                           + "same unit the fit line and the HIT RECT line use, so the three lines "
                           + "can be read side by side. A control listed here with interactable="
                           + "True is one the player is entitled to press and cannot reach.");
        return true;
    }
}
