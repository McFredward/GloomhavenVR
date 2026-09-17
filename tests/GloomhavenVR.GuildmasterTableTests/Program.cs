using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MapRoom;
using R = GloomhavenVR.WorldUI.MapRoom.GuildmasterTableFit.Rect;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    { _checks++; if (!condition) throw new Exception(reason); }
    private static void Main()
    {
        R map = new(-1f, 1f, -2f, 2f);
        Check(GuildmasterTableFit.TryFit(map, .3f, .02f, Array.Empty<R>(), out R clear), "clear map fits");
        Check(Math.Abs(clear.Area - 2.6f * 4.6f) < .001f, "clear map retains rim");
        R[] props = { new(1.16f, 1.8f, -3f, 3f), new(-3f, 3f, -3f, -2.10f) };
        Check(GuildmasterTableFit.TryFit(map, .3f, .02f, props, out R fitted), "barrel and bench fit");
        Check(fitted.Contains(map), "complete map remains supported");
        Check(fitted.Right <= 1.14f + .00001f && fitted.Near >= -2.08f - .00001f, "prop clearance retained");
        Check(!GuildmasterTableFit.TryFit(map, .3f, .02f, new[] { new R(-.1f, .1f, -.1f, .1f) }, out _), "overlapping map refuses clipped slab");
        var random = new Random(528);
        for (int i = 0; i < 500; i++)
        {
            float scale = .1f + (float)random.NextDouble() * 300f;
            float x = (float)random.NextDouble() * 1000f, z = (float)random.NextDouble() * 1000f;
            R translated = new(x - scale, x + scale, z - 2f * scale, z + 2f * scale);
            R barrel = new(x + 1.15f * scale, x + 2f * scale, z - 3f * scale, z + 3f * scale);
            R bench = new(x - 3f * scale, x + 3f * scale, z - 3f * scale, z - 2.1f * scale);
            Check(GuildmasterTableFit.TryFit(translated, .3f * scale, .02f * scale, new[] { barrel, bench }, out R actual), "scaled translated fit");
            Check(actual.Contains(translated), "scaled map coverage");
            Check(!actual.Overlaps(barrel) && !actual.Overlaps(bench), "scaled prop nonintersection");
        }
        Console.WriteLine($"Guildmaster table production fit: {_checks} assertions passed.");
    }
}
