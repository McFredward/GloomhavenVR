using System;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        _checks++;
        if (!ok) throw new InvalidOperationException(message);
    }
    private static WindowReflowLayout.Window W(float x, float width, float z = 1.3f,
        float y = 0f, bool movable = true) => new()
    {
        X = x, Depth = z, Width = width, Left = (x - width / 2) / z,
        Right = (x + width / 2) / z, Bottom = (y - 0.4f) / z, Top = (y + 0.4f) / z,
        Movable = movable
    };
    private static bool Arrange(WindowReflowLayout.Window[] windows, out float[] x,
        out bool[] included, out float depth, int incoming = 0)
    {
        x = new float[windows.Length]; included = new bool[windows.Length];
        return WindowReflowLayout.TryArrange(windows, windows.Length, incoming, 0.7f, 0.04f,
            3.5f, included, x, out depth);
    }
    private static void Main()
    {
        Check(!Arrange(new[]{W(-0.7f,0.5f),W(0.7f,0.5f)},out _,out _,out _),
            "Separated windows must not move");
        Check(!Arrange(new[]{W(0f,1f),W(0f,1f,y:1f)},out _,out _,out _),
            "Vertical separation must not trigger horizontal rearrangement");
        Check(!Arrange(new[]{W(5f,1f),W(5f,1f)},out _,out _,out _),
            "Offscreen windows must not trigger a recall");
        Check(Arrange(new[]{W(0f,1f),W(0f,1f,movable:false)},out _,out var held,out _),
            "A protected corner must leave room for the new window elsewhere");
        Check(held[0] && !held[1], "Protected corner geometry must never move");
        Check(!Arrange(new[]{W(0f,4f),W(0f,4f)},out _,out _,out _),
            "Impossible readable fit must leave the native window playable");
        Check(!Arrange(new[]{W(float.NaN,1f),W(0f,1f)},out _,out _,out _),
            "Invalid geometry must not create a pose");
        Check(!Arrange(new[]{W(0f,1f,y:3f),W(0f,1f,y:3f)},out _,out _,out _),
            "Windows above the view must not trigger a recall");
        var mixed = new[]{ W(-0.2f,1.1f), W(0.2f,1.1f), W(2f,0.4f) };
        Check(Arrange(mixed,out _,out var affected,out _), "Overlapping visible pair must arrange");
        Check(affected[0] && affected[1] && !affected[2], "An unrelated parked window must remain unchanged");
        var cornerIncoming = new[]{W(0f,0.7f,movable:false),W(-0.3f,0.9f)};
        Check(Arrange(cornerIncoming,out _,out affected,out _), "A fixed incoming quest may move its blocking story");
        Check(!affected[0] && affected[1], "The new quest must retain its authored corner");
        Check(!Arrange(new[]{W(0f,1f,movable:false),W(0f,1f,movable:false)},out _,out _,out _),
            "Two fixed colliders must not claim successful movement");
        var crowded = new[]{W(-0.15f,1.1f),W(0.15f,1.1f),W(0.75f,0.6f,movable:false)};
        Check(Arrange(crowded,out _,out affected,out _), "A fixed quest corner must not veto the encounter pair");
        Check(affected[0] && affected[1] && !affected[2], "The quest corner must remain fixed");
        var chain = new[]{ W(-0.6f,0.8f), W(0f,0.8f), W(0.6f,0.8f) };
        Check(Arrange(chain,out _,out affected,out _), "Transitive collision must arrange as one group");
        Check(affected[0] && affected[1] && affected[2], "The third colliding window must be included");
        // Measured hardware shape: ~49 + 48 degree-wide windows cannot fit side-by-side at their
        // original depth inside the usable 70 degree cone. Moving farther preserves their size.
        var wide = new[]{W(-0.2f,1.24f,1.36f),W(0.1f,1.21f,1.36f)};
        Check(Arrange(wide,out var wx,out _,out var dz), "Hardware overlap must find a layout");
        Check(dz > 1.36f, "Wide windows must move farther instead of offscreen");
        for (int seed = 0; seed < 240; seed++)
        {
            var random = new Random(seed);
            float a = 0.3f + (float)random.NextDouble(), b = 0.3f + (float)random.NextDouble();
            float x0 = -0.05f + (float)random.NextDouble() * 0.1f;
            var pair = new[]{W(x0,a),W(x0 + 0.05f,b)};
            Check(Arrange(pair,out var x,out affected,out float depth), "Overlapping pair must fit");
            Check(depth >= 1.3f && depth <= 3.5f, "Depth remains within readable bounds");
            Check(x[0] + a/2 + 0.0399f <= x[1] - b/2, "Full hit rects must be separated");
            Check(x[0] - a/2 >= -depth*0.7f-0.0001f && x[1]+b/2 <= depth*0.7f+0.0001f,
                "Both complete frames must stay inside the usable view");
            var final = new[]{W(x[0],a,depth),W(x[1],b,depth)};
            Check(!Arrange(final,out _,out _,out _), "An already-arranged pair must not oscillate");
        }
        for (int yaw = -180; yaw <= 180; yaw += 15)
        {
            var origin = new UnityEngine.Vector3(2f, 1f, -3f);
            var rotation = UnityEngine.Quaternion.Yaw(yaw);
            var offset = new UnityEngine.Vector3(0.4f, -0.3f, 0.02f);
            var measured = origin + rotation * offset;
            var desired = new UnityEngine.Vector3(-1f, 1.7f, 2f);
            var targetRotation = UnityEngine.Quaternion.Yaw(yaw + 35f);
            var result = WindowReflowPose.FramePositionForCentre(origin, rotation, measured,
                desired, targetRotation);
            var centre = result + targetRotation * offset;
            Check(Math.Abs(centre.x-desired.x)<0.00001f && Math.Abs(centre.y-desired.y)<0.00001f
                && Math.Abs(centre.z-desired.z)<0.00001f,
                "Off-centre hit rectangles must finish at the packed centre after yaw changes");
        }
        Console.WriteLine($"Window reflow: {_checks} runtime assertions passed.");
    }
}
