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
        Check(!Arrange(new[]{W(0f,1f),W(0f,1f,movable:false)},out _,out var held,out _),
            "A fixed older corner must never relocate the incoming window");
        Check(!held[0] && !held[1], "Neither incoming nor protected corner may move");
        Check(!Arrange(new[]{W(0f,4f),W(0f,4f)},out _,out _,out _),
            "Impossible readable fit must leave the native window playable");
        Check(!Arrange(new[]{W(float.NaN,1f),W(0f,1f)},out _,out _,out _),
            "Invalid geometry must not create a pose");
        Check(!Arrange(new[]{W(0f,1f,y:3f),W(0f,1f,y:3f)},out _,out _,out _),
            "Windows above the view must not trigger a recall");
        var mixed = new[]{ W(-0.2f,1.1f), W(0.2f,1.1f), W(2f,0.4f) };
        Check(Arrange(mixed,out _,out var affected,out _), "Overlapping visible pair must arrange");
        Check(!affected[0] && affected[1] && !affected[2], "Only the overlapping older window may move");
        var cornerIncoming = new[]{W(0f,0.7f,movable:false),W(-0.3f,0.9f)};
        Check(Arrange(cornerIncoming,out _,out affected,out _), "A fixed incoming quest may move its blocking story");
        Check(!affected[0] && affected[1], "The new quest must retain its authored corner");
        Check(!Arrange(new[]{W(0f,1f,movable:false),W(0f,1f,movable:false)},out _,out _,out _),
            "Two fixed colliders must not claim successful movement");
        var crowded = new[]{W(-0.15f,1.1f),W(0.15f,1.1f),W(0.75f,0.6f,movable:false)};
        Check(!Arrange(crowded,out _,out affected,out _),
            "An anchored opening and occupied side must refuse an unreadable fit");
        Check(!affected[0] && affected[1] && !affected[2], "Incoming and quest corner must remain fixed");
        var chain = new[]{ W(-0.6f,0.8f), W(0f,0.8f), W(0.6f,0.8f) };
        Check(Arrange(chain,out var chainX,out affected,out float chainDepth),
            "Transitive collision must arrange as one group");
        Check(!affected[0] && affected[1] && affected[2], "Only older transitive colliders must be included");
        var arrangedChain = new[]{chain[0],W(chainX[1],0.8f,chainDepth),W(chainX[2],0.8f,chainDepth)};
        for (int i = 0; i < arrangedChain.Length; i++)
            Check(!WindowReflowLayout.HasVisibleOverlap(arrangedChain,3,i,0.7f),
                "A transitive arrangement must clear every affected window");
        Check(chainX[1] < chainX[2], "Older transitive windows retain their visual order");
        Check(!Arrange(arrangedChain,out _,out _,out _),
            "An already arranged transitive component must not oscillate");
        // Measured hardware shape: the newly opened window retains its original reading pose;
        // only the old window may move farther to fit beside it inside the usable cone.
        var wide = new[]{W(-0.2f,1.24f,1.36f),W(0.1f,1.21f,1.36f)};
        Check(Arrange(wide,out var wx,out _,out var dz), "Hardware overlap must find a layout");
        Check(dz > 1.36f, "Wide windows must move farther instead of offscreen");
        // Build-515 hardware used different live rig and stable parchment scales. A 1.7 m
        // physical reading distance must not be rejected as more than 3.5 parchment metres.
        var zoomed = new[]{W(-80f,509.5572f,577.497f),W(80f,509.5572f,577.497f)};
        var zx = new float[2]; var zi = new bool[2];
        float cone = MathF.Tan(35f * MathF.PI / 180f);
        Check(!WindowReflowLayout.TryArrange(zoomed,2,0,cone,6.9342f,3.5f*198.12f,zi,zx,out _),
            "Stable parchment units reproduce the false distance refusal");
        Check(WindowReflowLayout.TryArrange(zoomed,2,0,cone,6.9342f,3.5f*424.63f,zi,zx,out float zd),
            "Readable zoomed hardware windows must fit in physical metres");
        Check(zd/424.63f <= 3.5f,"The anchored hardware correction remains within the reading limit");
        var centeredQuest = new[]{W(0.65f,0.3f,movable:false),W(0f,0.6f)};
        Check(!Arrange(centeredQuest,out _,out affected,out _,incoming:1),
            "A centred quest clear of the right-side quest list must not move");
        var laterIncoming = new[]{W(-0.05f,0.8f),W(0.05f,0.8f),W(0.85f,0.2f,movable:false)};
        Check(Arrange(laterIncoming,out var laterX,out affected,out float laterDepth,incoming:1),
            "An opening outside array slot zero must make room in older windows");
        Check(affected[0] && !affected[1] && !affected[2],
            "Incoming identity, not enumeration order, determines the fixed anchor");
        Check(!WindowReflowLayout.HasVisibleOverlap(new[]{W(laterX[0],0.8f,laterDepth),
            laterIncoming[1],laterIncoming[2]},3,1,0.7f),
            "Older-window placement must clear the original incoming pose");
        var blocked = new[]{W(0f,1.9f),W(0f,0.5f)};
        Check(!Arrange(blocked,out _,out affected,out _),
            "An incoming frame covering the view must not be moved as a fit fallback");
        Check(!affected[0], "An impossible layout must not admit incoming movement");
        var fixedAndMoving = new[]{W(0f,0.8f),W(0f,0.8f),W(0.7f,0.25f,movable:false)};
        Check(Arrange(fixedAndMoving,out var obstacleX,out affected,out float obstacleDepth),
            "A fixed side obstacle must leave the opposite side available to old windows");
        var moved = W(obstacleX[1],0.8f,obstacleDepth);
        Check(moved.Right <= fixedAndMoving[0].Left || moved.Left >= fixedAndMoving[0].Right,
            "An old frame must not cover the new opening");
        Check(moved.Right <= fixedAndMoving[2].Left || moved.Left >= fixedAndMoving[2].Right,
            "An old frame must not cover a protected side obstacle");
        int fitted = 0, refused = 0;
        for (int seed = 0; seed < 240; seed++)
        {
            var random = new Random(seed);
            float a = 0.3f + (float)random.NextDouble(), b = 0.3f + (float)random.NextDouble();
            float x0 = -0.05f + (float)random.NextDouble() * 0.1f;
            var pair = new[]{W(x0,a),W(x0 + 0.05f,b)};
            bool fits = Arrange(pair,out var x,out affected,out float depth);
            Check(!affected[0], "No feasible or refused layout may move the incoming window");
            if (!fits)
            {
                refused++;
                continue;
            }
            fitted++;
            Check(depth >= 1.3f && depth <= 3.5f, "Depth remains within readable bounds");
            Check(!affected[0] && affected[1], "The opening is always anchored even when movable");
            Check((x[1] + b/2) / depth <= pair[0].Left - 0.0399f/depth
                || (x[1] - b/2) / depth >= pair[0].Right + 0.0399f/depth,
                "The older frame must clear the anchored opening with a readable gap");
            Check(x[1] - b/2 >= -depth*0.7f-0.0001f && x[1]+b/2 <= depth*0.7f+0.0001f,
                "The complete moved frame must stay inside the usable view");
            var final = new[]{pair[0],W(x[1],b,depth)};
            Check(!Arrange(final,out _,out _,out _), "An already-arranged pair must not oscillate");
        }
        Check(fitted > 150 && refused > 0,
            "Randomized coverage must include feasible and constrained anchored layouts");
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
