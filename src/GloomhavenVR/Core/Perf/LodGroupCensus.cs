using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// "How many LOD groups are there?", asked once.
///
/// <para><b>WHY THIS TYPE EXISTS (redundancy survey R43, 2026-09-05).</b> Two instruments asked the
/// question and neither knew about the other: <c>PerfSceneProfile.AppendLodGroups</c> on the perf
/// summary cadence, counting <c>active</c> AND <c>enabled</c>, and <c>AutoLod.AppendLodGroupClause</c>
/// on the AutomaticLOD sweep cadence, counting <c>active</c> only. Same
/// <c>Object.FindObjectsOfType&lt;LODGroup&gt;()</c>, two populations spelled the same way, and two
/// near-identical prose sentences about lodBias being inert. A reader looking at one log with both
/// lines in it had nothing to say which of the two numbers was fresher or whether they were even
/// measuring the same thing — and the answer was that they were not.</para>
///
/// <para><b>THE SWEEP IS SHARED; THE LINES ARE NOT.</b> Merging the two lines would mean rewording
/// one of them, and every one of this mod's log lines is a grep anchor somebody may already be
/// using. So each caller keeps its own sentence and its own cadence, and what is shared is the
/// measurement plus the clause that states the POPULATION RULE — the fix §3 of the audit asks for,
/// and already the house style in <c>PerfSceneProfile</c>'s SCENE line.</para>
///
/// <para><b>COST.</b> One <c>FindObjectsOfType</c> per call, which is this project's default
/// performance suspect and correctly so. Nothing is cached across callers on purpose: a cache would
/// hand one instrument the OTHER one's stale reading, which is a worse failure than paying the
/// sweep twice per half-minute. Both callers are already gated to a multi-second cadence and
/// neither runs while its own instrument is off.</para>
/// </summary>
internal static class LodGroupCensus
{
    /// <summary>One sweep's result. <see cref="Groups"/> is the raw array, for a caller that needs
    /// the components themselves rather than the counts.</summary>
    internal readonly struct Result
    {
        internal Result(LODGroup[] groups, int enabled)
        {
            Groups = groups;
            Enabled = enabled;
        }

        internal LODGroup[] Groups { get; }

        /// <summary>Components whose own <c>enabled</c> flag is set. Always &lt;= <see cref="Active"/>:
        /// <c>FindObjectsOfType</c> returns a DISABLED component that sits on an ACTIVE GameObject,
        /// so the two numbers are not the same measurement and a line that prints one of them has
        /// not answered the other.</summary>
        internal int Enabled { get; }

        /// <summary>Components on an active GameObject, enabled or not — i.e. what
        /// <c>FindObjectsOfType</c> returned.</summary>
        internal int Active => Groups.Length;
    }

    /// <summary>Sweep the scene. Never throws for the caller's sake — a census that takes an
    /// instrument down is worse than a census that returns nothing.</summary>
    internal static Result Sweep()
    {
        LODGroup[] groups = UnityEngine.Object.FindObjectsOfType<LODGroup>();
        int enabled = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] != null && groups[i].enabled)
                enabled++;
        }
        return new Result(groups, enabled);
    }

    /// <summary>
    /// The clause that says what was counted, by whom, and how often — appended to whichever line
    /// carries the numbers. This is the half that makes two LOD counts in one log readable: without
    /// it they look like a disagreement, and with it they are visibly two sweeps taken at two times
    /// by two instruments with two cadences.
    /// </summary>
    /// <param name="owner">The instrument that swept, named as its log line is grepped.</param>
    /// <param name="cadenceSeconds">That instrument's own cadence, in seconds.</param>
    internal static void AppendPopulationRule(StringBuilder sb, string owner, float cadenceSeconds)
    {
        sb.Append(" [population: every LODGroup Object.FindObjectsOfType returns — scene-wide, "
                  + "components on INACTIVE GameObjects excluded because that API does not return "
                  + "them, disabled components on ACTIVE GameObjects included and counted "
                  + "separately as 'enabled'. Swept by ")
          .Append(owner).Append(" this tick, on its own ~").Append(cadenceSeconds.ToString("0.#"))
          .Append(" s cadence. Another LOD count elsewhere in this log is a DIFFERENT sweep at a "
                  + "different moment, not a contradiction]");
    }
}
