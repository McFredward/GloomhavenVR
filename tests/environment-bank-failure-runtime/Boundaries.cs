using System;
using System.Collections.Generic;
namespace GloomhavenVR.Core;
// Platform selection and owned-receipt admission are explicit boundaries. The
// successful native-index case is PC; the actual Android payload is unavailable.
internal static class QuestStandalonePlatform
{
    internal static bool Enabled;
    internal static HashSet<string> OwnedEnvironmentSources(byte[] index, byte[] provenance) =>
        throw new InvalidOperationException("owned provenance boundary must not be reached without its asset");
}
internal static class VRLog
{
    internal static readonly List<string> Notes=new();
    internal static void Note(string scope,string message)=>Notes.Add(message);
}
