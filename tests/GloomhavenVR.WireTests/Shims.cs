// The two compile-time constants the wire files reach for outside Net/.
//
// `HeadMaskLibrary` and `Hands.HandStyles` are otherwise full of Unity asset loading, so
// compiling them here would drag in the bundle, Addressables and the whole plugin. Only the
// counts matter to the serializers (both are clamp bounds), so only the counts are shimmed.
//
// The obvious risk is that a shim drifts from the real declaration and the clamp tests then
// assert the wrong bound. REVIEW-Net-Rig §W5 suggested pinning them against a literal 3.
// That is weaker than it needs to be: a literal only proves the shim is 3, not that the real
// constant still is. `Shims.VerifyAgainstSource()` reads the actual source files and asserts
// the declaration, so changing `HeadMaskLibrary.MaskCount` to 4 fails these tests instead of
// silently invalidating them. It runs first, before any vector.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.Net
{
    internal static class HeadMaskLibrary
    {
        /// <summary>Mirror of the real <c>HeadMaskLibrary.MaskCount</c> — pinned by
        /// <see cref="GloomhavenVR.WireTests.Shims.VerifyAgainstSource"/>.</summary>
        public const int MaskCount = 3;
    }
}

namespace GloomhavenVR.Hands
{
    internal static class HandStyles
    {
        /// <summary>Mirror of the real <c>HandStyles.Count</c> — pinned by
        /// <see cref="GloomhavenVR.WireTests.Shims.VerifyAgainstSource"/>.</summary>
        public const int Count = 3;
    }
}

namespace GloomhavenVR.Core
{
    /// <summary>
    /// The mod's logger, reduced to the overloads <c>Cards/BoardAnchors.cs</c> reaches for so that
    /// file can be LINKED here rather than have its arithmetic copied.
    ///
    /// <para>Unlike the two constants above, this shim is NOT pinned against its source, and that
    /// is a deliberate difference rather than an oversight. Those stand in for VALUES the
    /// serializers compute with, so a drifted shim would make a passing test assert the wrong
    /// bound. This one stands in for a SIDE EFFECT nothing here observes: the seat and asset-pose
    /// clamps log when they bite, and every assertion in <c>BoardSeatVectors</c> reads the returned
    /// geometry instead. The only way the real <c>VRLog.Warn</c> could invalidate these tests is by
    /// changing SHAPE, and that fails the link at compile time, which is exactly the failure a sink
    /// needs to have.</para>
    ///
    /// <para>The real class is BepInEx logging behind a scope filter, so compiling it would drag
    /// the plugin in — the same reason <c>HeadMaskLibrary</c> is shimmed rather than linked.</para>
    /// </summary>
    internal static class VRLog
    {
        internal static void Warn(string scope, string message) { _ = scope; _ = message; }
        internal static void Info(string scope, string message) { _ = scope; _ = message; }
    }
}

namespace GloomhavenVR.WireTests
{
    internal static class Shims
    {
        private static readonly (string File, string Const, int Shim)[] Pinned =
        {
            ("src/GloomhavenVR/Net/Avatar/HeadMaskLibrary.cs", "MaskCount", Net.HeadMaskLibrary.MaskCount),
            ("src/GloomhavenVR/Hands/HandStyle.cs",     "Count",     Hands.HandStyles.Count),
        };

        public static void VerifyAgainstSource(string repoRoot)
        {
            foreach (var (file, name, shim) in Pinned)
            {
                string path = Path.Combine(repoRoot, file);
                if (!File.Exists(path))
                    throw new InvalidOperationException(
                        $"shim pin: {file} not found. The wire tests shim {name} = {shim}; if that " +
                        "file moved, re-point this pin or the clamp vectors below are unverified.");

                var m = Regex.Match(File.ReadAllText(path),
                                    @"const\s+int\s+" + Regex.Escape(name) + @"\s*=\s*(\d+)\s*;");
                if (!m.Success)
                    throw new InvalidOperationException(
                        $"shim pin: could not find `const int {name}` in {file}.");

                int real = int.Parse(m.Groups[1].Value);
                if (real != shim)
                    throw new InvalidOperationException(
                        $"shim pin: {file} declares {name} = {real}, the wire tests shim {shim}. " +
                        "Update Shims.cs — the clamp vectors below depend on this bound.");
            }
        }
    }
}
