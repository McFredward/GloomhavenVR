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

namespace GloomhavenVR.WireTests
{
    internal static class Shims
    {
        private static readonly (string File, string Const, int Shim)[] Pinned =
        {
            ("src/GloomhavenVR/Net/HeadMaskLibrary.cs", "MaskCount", Net.HeadMaskLibrary.MaskCount),
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
