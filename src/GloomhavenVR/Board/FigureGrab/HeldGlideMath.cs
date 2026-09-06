using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// PUTTING A HELD THING DOWN — the release glide's duration, its easing curve and the TRS write,
/// once, for both grab paths.
///
/// <para><b>WHY IT IS ITS OWN FILE (2026-09-05).</b> <c>FigureGrabbable</c> and
/// <c>GrabbableProp</c> each carried <c>private const float GlideDurationSeconds = 0.28f</c>, and
/// the prop copy's own doc comment named the hazard out loud: <i>"MIRRORED value: if one is ever
/// tuned, tune both."</i> That is the exact failure <c>scripts/check-mirrors.sh</c> exists for —
/// except this pair was never registered with it, so nothing at all was holding the two numbers
/// together. Both copies then spelled the same five lines of easing and the same three
/// <c>LerpUnclamped/SlerpUnclamped/LerpUnclamped</c> writes, character for character.</para>
///
/// <para><b>WHAT DELIBERATELY STAYED AT THE CALL SITES.</b> Only the arithmetic moved. The two
/// <c>TickGlide</c> bodies still own everything that genuinely differs and must: the figure's
/// nullable-<c>Transform</c> plumbing (its root can be torn down mid-glide and
/// <c>FinishAllGlides</c> needs the null path), its <c>NoteClothScale</c> call — the glide is a
/// size animation and the cape rides it home — and the prop's layer restore, Apparance thaw and
/// <c>PropAnimBelt.Release</c>. Share the machinery, not the look.</para>
///
/// <para>NO UNITY COMPONENTS in <see cref="Sample"/> and no config: it is <c>Mathf</c> and a
/// float, so the curve can be driven outside a Unity process. <see cref="WriteEased"/> takes the
/// transform because writing it IS the operation.</para>
/// </summary>
internal static class HeldGlideMath
{
    /// <summary>Glide time from hand to home — fast but visible, unscaled so it is pause-proof.
    /// ONE value: a prop being put down must look like a mini being put down, and until this file
    /// existed that was two numbers agreeing by hand.</summary>
    internal const float DurationSeconds = 0.28f;

    /// <summary>
    /// How far along the glide is, eased. Returns false once the glide is over, which is the
    /// caller's cue to snap to the exact home pose and finish.
    ///
    /// <para>Cubic ease-out — fast start, soft landing. <paramref name="startTime"/> is an
    /// <c>unscaledTime</c> stamp, so a pause does not stretch the animation.</para>
    /// </summary>
    internal static bool Sample(float startTime, float now, out float eased)
    {
        float u = (now - startTime) / DurationSeconds;
        if (u >= 1f)
        {
            eased = 1f;
            return false;
        }
        eased = 1f - (1f - u) * (1f - u) * (1f - u);
        return true;
    }

    /// <summary>The three local-TRS writes, unclamped so the eased parameter drives them
    /// directly. Size rides the same curve as the position: a thing released after a mid-hold zoom
    /// eases from its latched in-hand size back to the board's live size instead of snapping
    /// there.</summary>
    internal static void WriteEased(
        Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 fromScale,
        Vector3 toPos, Quaternion toRot, Vector3 toScale, float eased)
    {
        t.localPosition = Vector3.LerpUnclamped(fromPos, toPos, eased);
        t.localRotation = Quaternion.SlerpUnclamped(fromRot, toRot, eased);
        t.localScale = Vector3.LerpUnclamped(fromScale, toScale, eased);
    }
}
