namespace GloomhavenVR.Core;

/// <summary>
/// The mod's RELEASE identity — one source of truth for "which build is this", shared by the
/// main-menu version label, the GitHub-Actions release workflow and the in-game updater.
/// </summary>
/// <remarks>
/// <para>THIS IS A PARTIAL CLASS. The three values below are generated at BUILD time into
/// <c>obj/&lt;config&gt;/net472/BuildInfo.Core.g.cs</c> by the <c>GenerateBuildInfo</c> target in
/// <c>src/GloomhavenVR/GloomhavenVR.csproj</c>, from <c>$(Version)</c>, the git short hash and
/// <c>$(GhvrReleaseBuild)</c>. This half exists so the SHAPE is a checked-in, documented file
/// that a reader (and another lane) can find, instead of a string literal inside an MSBuild
/// target. Editing this file cannot change the values; edit the target, or <c>&lt;Version&gt;</c>
/// in the csproj (which <c>scripts/package-release.sh</c> also reads with sed).</para>
///
/// <para>NO GIT, NO PROBLEM. The generator's git calls are <c>ContinueOnError</c> and their exit
/// code is checked, so a build with no git on PATH — or from a source drop that is not a
/// checkout — compiles with <c>Commit = ""</c>, and <see cref="Display"/> degrades to
/// <c>"GloomhavenVR 0.1.0-dev"</c>. Nothing here can fail a build.</para>
///
/// <para><b>NOT <c>NetProtocol.ModBuild</c>, and never derived from it.</b> That number is the
/// MULTIPLAYER version-handshake key: it is bumped by hand on every shared build, it decides
/// whether two peers may play together, and its value is meaningless outside that handshake.
/// This type is the human/release identity and moves on a completely different schedule (it
/// changes when a release is cut, not when a packet layout does). Tying one to the other would
/// either force a protocol bump for a cosmetic release or, worse, let a release ship a silently
/// unchanged handshake key. Keep them apart.</para>
///
/// <para>The sibling <c>GloomhavenVR.BuildInfo</c> (no <c>.Core</c>) is a DIFFERENT generated
/// type in the same target: git hash, branch and build time, i.e. the DEVELOPER identity that
/// answers "did I test the right DLL". It stays where it is — <c>Plugin.cs</c> logs it.</para>
///
/// <para><b>QUALIFY THE NAME OUTSIDE <c>GloomhavenVR.Core</c>.</b> Because that sibling sits in
/// the ROOT namespace and this file's <c>using</c> directives live outside the file-scoped
/// namespace (so they are compilation-unit imports, consulted only at the global-namespace
/// step), a bare <c>BuildInfo</c> written in e.g. <c>GloomhavenVR.WorldUI</c> binds to the
/// enclosing namespace's type — the git-hash one — even with <c>using GloomhavenVR.Core;</c> at
/// the top of the file. It fails loudly (<c>CS0117: does not contain a definition for
/// 'Display'</c>) rather than silently, but write <c>GloomhavenVR.Core.BuildInfo</c> outside this
/// namespace and the question never comes up.</para>
/// </remarks>
internal static partial class BuildInfo
{
    // ---- generated half (BuildInfo.Core.g.cs) ------------------------------------------------
    //
    //   /// <summary>Semantic version of the mod, e.g. "0.2.0". Source of truth: <Version> in
    //   /// src/GloomhavenVR/GloomhavenVR.csproj (scripts/package-release.sh already reads it).</summary>
    //   public const string Version;
    //
    //   /// <summary>Short git commit hash, or the empty string when it could not be determined.</summary>
    //   public const string Commit;
    //
    //   /// <summary>TRUE unless this build was produced by the release workflow.</summary>
    //   public const bool IsDevBuild;

    /// <summary>
    /// "GloomhavenVR 0.2.0" for a release, "GloomhavenVR 0.2.0-dev+ab12cd3" for a dev build.
    /// The empty-commit case degrades to "GloomhavenVR 0.2.0-dev".
    /// </summary>
    /// <remarks>Computed once at type initialisation — it is drawn every time the main menu
    /// comes up and read by the updater, and none of its inputs can change at runtime.</remarks>
    public static string Display { get; } =
        !IsDevBuild ? "GloomhavenVR " + Version
        : Commit.Length == 0 ? "GloomhavenVR " + Version + "-dev"
        : "GloomhavenVR " + Version + "-dev+" + Commit;
}
