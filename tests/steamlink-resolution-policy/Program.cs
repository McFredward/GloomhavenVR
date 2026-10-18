using System;
using GloomhavenVR.Core.SteamLink;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }

    private static void Width(int width, int height, int wanted)
    {
        Check(SteamLinkResolutionPolicy.TryWidth(width, height, out int value), "Valid carrier refused.");
        Check(value == wanted, "Wrong transport width for " + width + "x" + height + ": " + value);
    }

    private static void Invalid(int width, int height)
    {
        Check(!SteamLinkResolutionPolicy.TryWidth(width, height, out int value), "Invalid carrier admitted.");
        Check(value == 0, "Invalid carrier supplied a stale or silently clamped width.");
    }

    private static int Main()
    {
        // The accepted Build701 image had4040-square actual source/carrier surfaces,
        // while its independent 3072 request imposed a new lower transport cap.
        Width(4040, 4040, 4096);
        Width(3408, 3408, 3456);
        Width(2048, 2048, 2048);
        Width(4848, 4848, 4864);
        Width(1, 1, 64);
        Width(16384, 16384, 16384);
        Width(4040, 3408, 4096);
        Width(3408, 4040, 4096);
        Width(1728, 3408, 3456);
        Invalid(0, 4040); Invalid(4040, 0);
        Invalid(-1, 4040); Invalid(4040, -1);
        Invalid(int.MinValue, int.MaxValue); Invalid(int.MaxValue, int.MinValue);
        Invalid(16385, 16384); Invalid(16384, 16385);
        Invalid(int.MaxValue, int.MaxValue);

        // A real sequence of allocation changes must move the request in both
        // directions, without retaining a previous request or using a saved UI
        // value that exceeds the current session's actual allocation.
        foreach (int axis in new[] {4040, 3408, 1728, 4848, 2048, 4040})
            Width(axis, axis, (int)(((long)axis + 63) / 64 * 64));

        // Cover every accepted integer axis, including all 64 alignment phases.
        // Never reduce either valid dimension; at most 63 extra pixels, with
        // symmetric landscape/portrait requests and no mutable global state.
        for (int axis = 1; axis <= 16384; axis++)
        {
            Check(SteamLinkResolutionPolicy.TryWidth(axis, 1, out int landscape), "Valid landscape refused.");
            Check(SteamLinkResolutionPolicy.TryWidth(1, axis, out int portrait), "Valid portrait refused.");
            Check(landscape == portrait, "Orientation changed the uniform request.");
            Check(landscape >= axis, "Transport silently reduced carrier resolution.");
            Check(landscape - axis < 64, "Unnecessary upsampling added to the transport request.");
            Check(landscape % 64 == 0 && landscape <= 16384, "Transport request is outside the explicit integer boundary.");
        }
        Console.WriteLine("Steam Link actual-carrier resolution policy: " + _checks + " checks passed.");
        return 0;
    }
}
