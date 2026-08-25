// DOES A CLOTH COLLIDER MOVED IN Update REACH THE SOLVER WHEN Physics.autoSyncTransforms IS OFF?
//
// Run with --bench=sync. The game turns it off: PhysicsController.Setup (PhysicsController.cs:18-31)
// writes Physics.autoSyncTransforms = false when PlatformLayer.Setting.SimplifyPhysics is on AND
// the scene is "Game_gamepad", and SceneController.cs:1069 passes exactly that predicate. The
// tester's ModBuild 286 log contains "Added scene: Game_gamepad", so the scene half is met on his
// rig; the SimplifyPhysics half is a platform setting that cannot be read from a log.
//
// This measures the OUTCOME — how far the cloth actually moves when a collider is swept through it
// — not the readiness of the mechanism.
//
//   NULL control      the same sweep with NO collider assigned to the cloth at all. If this is not
//                     ~0 the metric is measuring something other than the collider.
//   POSITIVE control  autoSyncTransforms left ON, which is Unity's default and known to work.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ColliderSyncBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=sync") want = true;
        if (!want) return;
        var go = new GameObject("ColliderSyncBench");
        DontDestroyOnLoad(go);
        go.AddComponent<ColliderSyncBench>();
    }

    private const int Grid = 41;
    private const float SlackMax = 0.35f;
    private const int SweepFrames = 90;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) { _out.Add(s); }
    private static double Ms(Stopwatch sw) => sw.ElapsedTicks * 1000d / Stopwatch.Frequency;

    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private GameObject _handA = null!;
    private GameObject _handB = null!;

    private enum Arm { AutoSyncOn, AutoSyncOffNoCall, AutoSyncOffWithCall, NullNoCollider }

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        // 30 Hz physics against 90 Hz render — the shape of the game's SimplifyPhysicsRate path,
        // which is what makes a missed sync visible rather than theoretical.
        Time.fixedDeltaTime = 1f / 30f;
        Say($"Unity {Application.unityVersion} | grid {Grid}x{Grid} ({Grid * Grid} verts) | "
            + $"fixedDeltaTime 1/30 against a 90 Hz Update | sweep {SweepFrames} frames | "
            + "the collider is moved in Update (a coroutine), exactly as FigureClothHands does");

        foreach (Arm arm in new[] { Arm.NullNoCollider, Arm.AutoSyncOn, Arm.AutoSyncOffNoCall, Arm.AutoSyncOffWithCall })
            yield return RunArm(arm);

        Physics.autoSyncTransforms = true;
        Debug.Log("[SYNC] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[SYNC] " + _out[i]);
        Debug.Log("[SYNC] ==================== END ====================");
        Application.Quit(0);
    }

    private IEnumerator RunArm(Arm arm)
    {
        Physics.autoSyncTransforms = true;

        if (_root != null) Destroy(_root);
        if (_handA != null) Destroy(_handA);
        if (_handB != null) Destroy(_handB);
        yield return null;

        _root = Build(Grid);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / Grid) / (float)(Grid - 1);
            c[i].maxDistance = SlackMax * t;
            c[i].collisionSphereDistance = 0.01f * t;
        }
        _cloth.coefficients = c;

        _handA = new GameObject("palm");
        _handB = new GameObject("tip");
        var sa = _handA.AddComponent<SphereCollider>(); sa.radius = 0.08f;
        var sb = _handB.AddComponent<SphereCollider>(); sb.radius = 0.03f;
        _handA.transform.position = new Vector3(-2f, -0.05f, -0.5f);
        _handB.transform.position = new Vector3(-2f, -0.05f, -0.7f);

        if (arm != Arm.NullNoCollider)
            _cloth.sphereColliders = new[] { new ClothSphereColliderPair(sa, sb) };

        for (int i = 0; i < 240; i++) yield return null;      // settle at rest
        Vector3[] rest = Snapshot();

        Physics.autoSyncTransforms = arm == Arm.AutoSyncOn || arm == Arm.NullNoCollider;
        bool call = arm == Arm.AutoSyncOffWithCall;

        var syncMs = new List<double>();
        var sw = new Stopwatch();
        double worst = 0d;

        for (int f = 0; f < SweepFrames; f++)
        {
            // sweep the hand straight through the sheet, left to right
            float x = Mathf.Lerp(-1.2f, 1.2f, f / (float)(SweepFrames - 1));
            _handA.transform.position = new Vector3(x, -0.05f, -0.5f);
            _handB.transform.position = new Vector3(x, -0.05f, -0.7f);
            if (call)
            {
                sw.Restart();
                Physics.SyncTransforms();
                sw.Stop();
                syncMs.Add(Ms(sw));
            }
            yield return null;

            Vector3[] now = Snapshot();
            int n = Mathf.Min(now.Length, rest.Length);
            for (int i = 0; i < n; i++)
            {
                double d = (now[i] - rest[i]).magnitude;
                if (d > worst) worst = d;
            }
        }

        Say("");
        Say(arm switch
        {
            Arm.NullNoCollider      => "NULL control  the same sweep, NO collider assigned to the cloth",
            Arm.AutoSyncOn          => "POSITIVE      Physics.autoSyncTransforms = true (Unity's default)",
            Arm.AutoSyncOffNoCall   => "HAZARD        autoSyncTransforms = FALSE, nothing else done",
            _                       => "REMEDY        autoSyncTransforms = FALSE + Physics.SyncTransforms() after the move",
        });
        Say($"    worst cloth displacement from the resting pose during the sweep: {worst:F5} m");
        if (syncMs.Count > 0)
        {
            syncMs.Sort();
            Say($"    Physics.SyncTransforms(): median {syncMs[syncMs.Count / 2]:F4} ms   "
                + $"min {syncMs[0]:F4}   max {syncMs[syncMs.Count - 1]:F4}   (n={syncMs.Count})");
        }
    }

    private Vector3[] Snapshot() => _cloth != null && _cloth.enabled ? _cloth.vertices : Array.Empty<Vector3>();

    private static GameObject Build(int n)
    {
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var weights = new BoneWeight[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            verts[i] = new Vector3(x / (float)(n - 1) - 0.5f, 0f, -z / (float)(n - 1));
            normals[i] = Vector3.up;
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
        var tris = new List<int>((n - 1) * (n - 1) * 6);
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int i = z * n + x;
            tris.Add(i); tris.Add(i + n); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + n); tris.Add(i + n + 1);
        }
        var mesh = new Mesh { name = "clothgrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris.ToArray();
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateBounds();

        var root = new GameObject("clothroot");
        var bone = new GameObject("bone");
        bone.transform.SetParent(root.transform, false);
        var smr = root.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = new[] { bone.transform };
        smr.rootBone = bone.transform;
        smr.updateWhenOffscreen = true;
        return root;
    }
}
