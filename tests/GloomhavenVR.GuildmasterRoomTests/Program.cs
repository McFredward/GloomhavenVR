using System;
using GloomhavenVR.WorldUI.MapRoom;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    { _checks++; if (!condition) throw new Exception(reason); }
    private static void Main()
    {
        var random = new Random(530);
        for (int test = 0; test < 300; test++)
        {
            float scale = .5f + (float)random.NextDouble() * 400f;
            float x = -500f + (float)random.NextDouble() * 1000f;
            float z = -500f + (float)random.NextDouble() * 1000f;
            // Narrow one-column slabs, wider multi-column tables, changing native HUD counts.
            float width = (.09f + (float)random.NextDouble() * .25f) * scale;
            float length = (.3f + (float)random.NextDouble() * .45f) * scale;
            int count = 1 + random.Next(12);
            Check(GuildmasterRoomLayout.TryRail(x, x + width, z, z + length, count,
                .055f * scale, .018f / .055f, 1.35f, out var rail), "measured side rail fits");
            float radius = rail.Cap * 1.35f * .5f;
            float tolerance = .0002f * scale;
            Check(rail.Cap <= .055f * scale + tolerance, "never grows cap beyond normal size");
            for (int i = 0; i < count; i++)
            {
                rail.Position(i, out float cx, out float cz);
                Check(cx - radius >= x - tolerance && cx + radius <= x + width + tolerance,
                    "whole cap stays on right tabletop");
                Check(cz - radius >= z - tolerance && cz + radius <= z + length + tolerance,
                    "whole cap stays behind knife and within far edge");
                if (i >= rail.Columns)
                {
                    rail.Position(i - rail.Columns, out _, out float previousZ);
                    Check(previousZ > cz, "declared button order reads far to near");
                }
            }
            float baseY = -17f * scale;
            Check(Math.Abs(GuildmasterRoomLayout.GroundFloor(baseY, scale) - (baseY + .025f * scale)) < tolerance,
                "furniture floor contact allowance");
        }
        Check(!GuildmasterRoomLayout.TryRail(0f, 0f, 0f, 1f, 8, .055f, .3f, 1.35f, out _), "missing slab has no false fit");
        Check(!GuildmasterRoomLayout.TryRail(0f, 1f, 1f, 0f, 8, .055f, .3f, 1.35f, out _), "knife blocking all space retries");
        Console.WriteLine($"Guildmaster room production layout: {_checks} assertions passed.");
    }
}
