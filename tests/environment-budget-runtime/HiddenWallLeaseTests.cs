using GloomhavenVR.Core;

public static partial class EnvironmentProgram
{
    private static void HiddenWallCurrentLease()
    {
        PrivateTerrainWallReads();
        using var room = new Room();
        var first = room.Floor(1f); var second = room.Floor(2f);
        using var wall = new WallDormantFixture.AttachmentFixture();
        Configure(true, true, 100); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 1, "hidden-wall lease fixture creates an actual native chunk consumer");
        Tick("HandlePreCull", room.Camera);
        Check(first.forceRenderingOff && second.forceRenderingOff, "actual environment native sources enter current camera leases");
        wall.Attach(first); wall.Hide(first);
        Check(!first.enabled && !first.forceRenderingOff && !second.forceRenderingOff && room.Chunks().Length == 0,
            "actual enabled wall hide revokes current environment chunk lease before native setter");
        // A native re-enable permits a new real lease; no prepared membership substitutes
        // for this actual source state. A late external disable still needs its
        // current geometry consumer revoked, even though Hide has no setter left.
        first.enabled = true; ScenarioEnvironmentBudget.MaterialReady(first);
        ScenarioEnvironmentBudget.MaterialReady(second); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Check(room.Chunks().Length == 1, "reenabled original wall source can rejoin exact environment preparation");
        Tick("HandlePreCull", room.Camera);
        Check(first.forceRenderingOff && second.forceRenderingOff, "reenabled original acquires a fresh actual camera lease");
        first.enabled = false; wall.Present(1f);
        Check(!first.forceRenderingOff && !second.forceRenderingOff && !first.enabled && first.HasPropertyBlock()
            && room.Chunks().Length == 0,
            "dormant native drive refuses current environment consumer and releases before same draw");
        Tick("HandlePostRender", room.Camera);
        first.enabled = true; first.SetPropertyBlock(null); ScenarioEnvironmentBudget.MaterialReady(first);
        ScenarioEnvironmentBudget.MaterialReady(second); ScenarioEnvironmentBudget.BeforeLoadingComplete();
        Tick("HandlePreCull", room.Camera);
        Check(first.forceRenderingOff && second.forceRenderingOff, "late hide control reacquires a separate current chunk lease");
        first.enabled = false; wall.Hide(first);
        Check(!first.forceRenderingOff && !second.forceRenderingOff && !first.enabled && room.Chunks().Length == 0,
            "already disabled wall hide revokes its actual late environment consumer before same draw");
        Tick("FinishCameraPreCull", room.Camera);
        Tick("HandlePostRender", room.Camera);
        room.Render();
        Check(!first.forceRenderingOff && !second.forceRenderingOff && !first.enabled && room.LastRenderedChunks == 0,
            "next actual camera keeps the externally disabled source native after its synchronous consumer release");
        first.enabled = true; wall.Restore();
        Configure(false, false, 100); Tick();
    }

    private static void PrivateTerrainWallReads()
    {
        using var room = new Room();
        using var wall = new WallDormantFixture.AttachmentFixture();
        var material = room.Material(); material.SetFloat("_WallFade_On", 1f);
        var original = room.Surface("NativeWall", material: material);
        var privateDraw = room.Surface("NativeWall", material: material);
        Check(wall.Capable(original), "authored native wall retains its actual material capability");
        ScenarioTerrainBudget.PrivateRenderers.Add(privateDraw);
        try
        {
            // Prime the shared read scratch with a native wall first. Excluding
            // the private draw must clear that previous renderer's material too.
            Check(!wall.Capable(privateDraw),
                "private terrain renderer is excluded from native wall capability reads");
            Check(wall.Capable(original),
                "same-name same-material native wall never inherits another renderer's private ownership");
        }
        finally { ScenarioTerrainBudget.PrivateRenderers.Remove(privateDraw); }
        Check(wall.Capable(privateDraw),
            "released private renderer regains live native wall capability without a stale identity cache");
    }
}
