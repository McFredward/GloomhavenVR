using System;

namespace GloomhavenVR.Core;

internal static partial class Loc
{
    internal static string ProgressHelpModeName(int mode)
    {
        bool german = string.Equals(CurrentLanguage, "German", StringComparison.Ordinal);
        return mode switch
        {
            0 => german ? "Immer" : "Always",
            1 => german ? "Nie" : "Never",
            _ => german ? "Nach Wartezeit" : "After a delay",
        };
    }
}
