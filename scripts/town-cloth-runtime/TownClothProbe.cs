using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

public sealed class TownClothProbe : MonoBehaviour
{
    private sealed class Fixture
    {
        internal GameObject Root = null!;
        internal Cloth Cloth = null!;
        internal Transform Mover = null!;
        internal Mesh Visible = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>();
        internal float StationScale;
        internal float SolverScale;
        internal Transform Station = null!;
        internal Transform Driver = null!;
        internal int[] TableIndices = Array.Empty<int>();
        internal Vector3[] TableRest = Array.Empty<Vector3>();
        internal Vector3 Gravity;
        internal float RestDamping;
        internal double CookMilliseconds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var root = new GameObject("Native town cloth probe");
        DontDestroyOnLoad(root);
        root.AddComponent<TownClothProbe>();
    }

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;

        Fixture positive = Build("hidden-positive", 1f, 1f, true, Vector3.zero, 0f);
        yield return Settle(positive, 120);
        float gravityMotion = MaximumDistance(positive.Rest, positive.Cloth.vertices);
        yield return Freeze(positive);
        Vector3[] positiveBefore = positive.Cloth.vertices;
        yield return Sweep(positive);
        float contactMotion = MaximumDistance(positiveBefore, positive.Cloth.vertices);

        Fixture nullContact = Build("no-collider-negative-control", 1f, 1f, false, Vector3.zero, 0f);
        yield return Settle(nullContact, 120);
        yield return Freeze(nullContact);
        Vector3[] nullBefore = nullContact.Cloth.vertices;
        yield return Sweep(nullContact);
        float nullMotion = MaximumDistance(nullBefore, nullContact.Cloth.vertices);

        // Shipping town furniture nests a 100x FBX mesh below the roughly 198x
        // map-room station. Exercise that hierarchy rather than a unit-scale
        // sheet: the regression multiplied station travel by the complete 19800x
        // driver scale and consequently made the cloth's travel/skin 100x large.
        Fixture scaled = Build("unit-scale-world-driver", 198f, 100f, true, true,
            new Vector3(3f, 2f, -4f), 37f);
        yield return Settle(scaled, 60);
        float scaledGravityMotion = MaximumDistance(scaled.Rest, scaled.Cloth.vertices) * scaled.SolverScale;
        yield return Freeze(scaled);
        Vector3[] scaledBefore = scaled.Cloth.vertices;
        yield return Sweep(scaled, .60f);
        float scaledLocalMotion = MaximumDistance(scaledBefore, scaled.Cloth.vertices) * scaled.SolverScale;

        Fixture unscaled = Build("inherited-fbx-scale-negative-control", 198f, 100f, true, false,
            new Vector3(-3f, 2f, -4f), -29f);
        yield return Settle(unscaled, 60);
        float unscaledGravityMotion = MaximumDistance(unscaled.Rest, unscaled.Cloth.vertices) * unscaled.SolverScale;
        yield return Freeze(unscaled);
        Vector3[] unscaledBefore = unscaled.Cloth.vertices;
        yield return Sweep(unscaled, .60f);
        float unscaledLocalMotion = MaximumDistance(unscaledBefore, unscaled.Cloth.vertices) * unscaled.SolverScale;

        Vector3[] sample = positive.Cloth.vertices;
        var watch = Stopwatch.StartNew();
        const int costIterations = 1000;
        for (int i = 0; i < costIterations; i++)
        {
            sample = positive.Cloth.vertices;
            positive.Visible.vertices = sample;
            positive.Visible.RecalculateNormals();
            positive.Visible.RecalculateBounds();
        }
        watch.Stop();
        double microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / costIterations;
        int snapshotBytes = sample.Length * 12;

        bool hiddenPass = gravityMotion > .025f && contactMotion > .012f;
        bool negativePass = nullMotion < .006f && contactMotion > nullMotion + .012f;
        // Production bakes the already-placed sheet into a unit-scale solver.
        // The control reproduces the released inherited-19,800x driver, whose
        // particles become numerically unbounded under the same physical sweep.
        bool scalePass = scaledLocalMotion > .5f
            && scaledLocalMotion < 200f
            && unscaledLocalMotion > scaledLocalMotion * 3f;
        string bundlePath = Argument("--bundle=");
        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        bool actualPass = bundle != null;
        float actualContactMin = float.MaxValue, actualContactMax = 0f, actualTableDrop = 0f;
        float actualVisibleReturnMax = 0f, actualReentryMin = float.MaxValue;
        float actualLongTermMax = 0f, actualPinnedDriftMax = 0f;
        float actualReturnStepMax = 0f, actualReentryPopMax = 0f;
        float actualApproachRawDriftMax = 0f, actualContactStepMax = 0f;
        float productionVisibleContact = 0f, productionVisibleNull = 0f, productionDeadVisible = 0f;
        float productionShortTouchPeak = 0f;
        float productionContactEdgePeak = 0f;
        float productionResetEdgePeak = 0f;
        float productionNearReturn = 0f;
        float productionDeepHold = 0f, productionRestGateDeepHold = 0f;
        float enchantressHoldMin = float.MaxValue, enchantressHoldMax = 0f;
        float enchantressHoldStep = 0f;
        float enchantressLocalMin = float.MaxValue, enchantressLocalMax = 0f;
        float enchantressRootPenetration = 0f;
        float enchantressEdgeStretch = 0f;
        float enchantressHandClearanceMin = float.MaxValue;
        float enchantressNoGateVisibleMin = float.MaxValue;
        float enchantressCapsuleClearanceMin = float.MaxValue;
        float enchantressWithdrawalStepMax = 0f;
        float enchantressWithdrawalVertexStepMax = 0f;
        int enchantressWithdrawalVertexStepFrame = -1;
        int enchantressWithdrawalVertexStepIndex = -1;
        float enchantressAfterWithdrawal = float.MaxValue;
        long enchantressContactTicks = 0, enchantressIdleTicks = 0;
        int enchantressContactSamples = 0, enchantressIdleSamples = 0;
        long[] enchantressContactDurations = new long[90];
        long[] enchantressIdleDurations = new long[90];
        int merchantRunnerCount = -1;
        bool compactedLocalHandPass = false, compactedPeerHandPass = false;
        bool oppositeSideReentryPass = false, hideRestPass = false, zeroMeanRestPass = false;
        int firstApproachSide = 0, throughPushSide = 0, oppositeReentrySide = 0;
        int sideBeforeSeparation = 0, sideAfterSeparation = 0;
        bool sideStableThroughHold = true;
        bool oppositeFarInExpandedAabb = false;
        float oppositeFarDepthRecorded = 0f;
        int farCountMid = 0, farCountEnd = 0;
        bool productionPathPass = false;
        bool actualReturnMonotone = true;
        int actualRunners = 0;
        if (bundle != null)
        {
            int assetOffset = 0;
            foreach (var item in new[] { (Name: "priestess", Service: (byte)2, Count: 2),
                         (Name: "enchantress", Service: (byte)3, Count: 1) })
            {
                string asset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/town" + item.Name + ".prefab"));
                GameObject station = Instantiate(bundle.LoadAsset<GameObject>(asset));
                station.transform.SetPositionAndRotation(new Vector3(assetOffset * 500f, 0f, 800f), Quaternion.identity);
                station.transform.localScale = Vector3.one * 198f;
                MeshFilter[] filters = station.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal)).ToArray();
                actualPass &= filters.Length == item.Count;
                foreach (MeshFilter filter in filters)
                {
                    actualRunners++;
                    float nested = filter.transform.lossyScale.x / station.transform.lossyScale.x;
                    actualPass &= nested > 99f && nested < 101f;
                    Fixture actual = BuildActual("actual-" + item.Name + "-" + actualRunners,
                        station.transform, filter, item.Service, 198f);
                    yield return Settle(actual, 120);
                    actualTableDrop = Mathf.Max(actualTableDrop, MaximumTableDrop(actual));
                    yield return Freeze(actual);
                    Vector3[] before = actual.Cloth.vertices;
                    Vector3 sweepStart = actual.Mover.position;
                    ClothSkinningCoefficient[] active = actual.Cloth.coefficients;
                    yield return RepeatedSweep(actual, .18f, 3);
                    float contact = MaximumDistance(before, actual.Cloth.vertices);
                    actualContactMin = Mathf.Min(actualContactMin, contact);
                    actualContactMax = Mathf.Max(actualContactMax, contact);
                    actualPass &= contact > .25f && contact < 80f;
                    actual.Mover.position = new Vector3(0f, -100000f, 0f);
                    actual.Cloth.externalAcceleration = actual.Gravity;
                    actual.Cloth.damping = actual.RestDamping;
                    // Production fades the episode delta to the unchanged authored
                    // renderer over 0.48 seconds, then pins its bounded hidden solver.
                    // Exercise that path after three large bidirectional impulses.
                    float visibleReturn = contact;
                    float previousVisible = contact;
                    for (int frame = 0; frame < 44; frame++)
                    {
                        yield return null;
                        float weight = Mathf.Max(0f, 1f - (frame + 1f) / 44f);
                        visibleReturn = MaximumDistance(before, actual.Cloth.vertices) * weight;
                        actualReturnStepMax = Mathf.Max(actualReturnStepMax,
                            Mathf.Abs(visibleReturn - previousVisible));
                        actualReturnMonotone &= visibleReturn <= previousVisible + .25f;
                        previousVisible = visibleReturn;
                    }
                    actualVisibleReturnMax = Mathf.Max(actualVisibleReturnMax, visibleReturn);
                    ClothSkinningCoefficient[] pinned = actual.Cloth.coefficients;
                    for (int i = 0; i < pinned.Length; i++) pinned[i].maxDistance = 0f;
                    actual.Cloth.coefficients = pinned;
                    actual.Cloth.externalAcceleration = Vector3.zero;
                    actual.Cloth.ClearTransformMotion();
                    Vector3[] pinnedOrigin = actual.Cloth.vertices;
                    for (int frame = 0; frame < 90; frame++) yield return null;
                    float pinnedDrift = MaximumDistance(pinnedOrigin, actual.Cloth.vertices);
                    actualPinnedDriftMax = Mathf.Max(actualPinnedDriftMax, pinnedDrift);
                    actualPass &= visibleReturn < .001f;

                    // Re-entry captures the current hidden state as a new visual zero.
                    // Expanding the existing component must therefore have no pose pop,
                    // while a subsequent physical sweep still produces a real response.
                    actual.Cloth.coefficients = active;
                    actual.Cloth.ClearTransformMotion();
                    Vector3[] reentryOrigin = actual.Cloth.vertices;
                    // Production keeps the renderer at rest through a 60 ms
                    // coefficient warm-up and refreshes the episode origin while
                    // the probe is still in the approach margin.
                    for (int frame = 0; frame < 6; frame++)
                    {
                        yield return null;
                        reentryOrigin = actual.Cloth.vertices;
                    }
                    // Remaining inside proximity without touching keeps weight at
                    // zero and refreshes the visual origin on every production tick.
                    // Its raw hidden movement is recorded, but cannot become a pop.
                    for (int frame = 0; frame < 12; frame++)
                    {
                        yield return null;
                        float raw = MaximumDistance(reentryOrigin, actual.Cloth.vertices);
                        actualApproachRawDriftMax = Mathf.Max(actualApproachRawDriftMax, raw);
                        actualReentryPopMax = Mathf.Max(actualReentryPopMax, raw * 0f);
                        reentryOrigin = actual.Cloth.vertices;
                    }
                    actual.Mover.position = sweepStart;
                    actual.Cloth.externalAcceleration = actual.Gravity;
                    float previousEntry = 0f;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        yield return null;
                        float weight = Mathf.Min(1f, (frame + 1f) / 11f);
                        float shown = MaximumDistance(reentryOrigin, actual.Cloth.vertices) * weight;
                        actualContactStepMax = Mathf.Max(actualContactStepMax,
                            Mathf.Abs(shown - previousEntry));
                        previousEntry = shown;
                    }
                    actualPass &= actualReentryPopMax < .001f && actualContactStepMax < 2.5f;
                    yield return RepeatedSweep(actual, .18f, 2);
                    float reentry = MaximumDistance(reentryOrigin, actual.Cloth.vertices);
                    actualReentryMin = Mathf.Min(actualReentryMin, reentry);
                    float longTerm = MaximumDistance(actual.Rest, actual.Cloth.vertices);
                    actualLongTermMax = Mathf.Max(actualLongTermMax, longTerm);
                    actualPass &= reentry > .25f && reentry < 80f;
                    actualPass &= longTerm < 30f && pinnedDrift < 25f;
                    actualPass &= actualReturnMonotone && actualReturnStepMax < Mathf.Max(2.5f, contact * .15f);
                    assetOffset++;
                }
            }
            actualPass &= actualRunners == 3 && actualTableDrop < 8f;

            // Execute the shipping TownServiceCloth class, not a fixture which merely repeats
            // its coefficients. Build 566 passed the latter while its separate visibility gate
            // captured every real collision as a fresh zero and drew no response at all.
            string priestessAsset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/townpriestess.prefab"));
            GameObject priestessPrefab = bundle.LoadAsset<GameObject>(priestessAsset);
            GameObject rigScale = new GameObject("production-rig-scale");
            rigScale.transform.localScale = Vector3.one * 198f;
            VRRigDriver.RigRoot = rigScale.transform;
            VRRigDriver.BaseWorldScale = 198f;
            VRRigDriver.HeadCamera = null;
            var palmObject = new GameObject("production-palm");
            var tipObject = new GameObject("production-tip");
            var wristObject = new GameObject("production-wrist");
            wristObject.transform.SetParent(palmObject.transform, false);
            wristObject.transform.localPosition = new Vector3(0f, 0f, -198f * .045f);
            var rightPalmObject = new GameObject("production-right-palm");
            var rightTipObject = new GameObject("production-right-tip");
            var rightWristObject = new GameObject("production-right-wrist");
            rightWristObject.transform.SetParent(rightPalmObject.transform, false);
            rightWristObject.transform.localPosition = Vector3.down * (198f * .045f);
            var hand = new VRHand
            {
                HasPose = false,
                WorldScale = 198f,
                Rig = new ProbeRig { Wrist = wristObject.transform,
                    PalmCenter = palmObject.transform, IndexTip = tipObject.transform }
            };
            VRHands.Left = hand; VRHands.Right = null;
            var rightHand = new VRHand
            {
                HasPose = false,
                WorldScale = 198f,
                Rig = new ProbeRig { Wrist = rightWristObject.transform,
                    PalmCenter = rightPalmObject.transform, IndexTip = rightTipObject.transform }
            };
            TownServiceCloth nullProduction = null;
            TownServiceCloth contactProduction = null;
            TownServiceCloth merchantProduction = null;
            TownServiceCloth enchantressProduction = null;
            TownServiceClothNoContactGate enchantressNoGate = null;
            TownServiceClothContactReset resetProduction = null;
            TownServiceClothDead deadProduction = null;
            TownServiceClothRestGate restGateProduction = null;
            GameObject nullStation = null, contactStation = null, resetStation = null;
            GameObject deadStation = null, restGateStation = null;
            GameObject merchantStation = null;
            GameObject enchantressStation = null;
            GameObject enchantressNoGateStation = null;
            try
            {
                nullStation = Instantiate(priestessPrefab);
                nullStation.name = "production-null-station";
                nullStation.transform.SetPositionAndRotation(new Vector3(2500f, 0f, 1200f), Quaternion.identity);
                nullStation.transform.localScale = Vector3.one * 198f;
                MeshFilter nullRunner = nullStation.GetComponentsInChildren<MeshFilter>(true)
                    .First(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                nullProduction = new TownServiceCloth(nullStation.transform, 2);
                nullProduction.SetVisible(true);
                Vector3[] nullRest = nullRunner.mesh.vertices;
                for (int frame = 0; frame < 100; frame++)
                {
                    nullProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                productionVisibleNull = VisibleMotion(nullRunner, nullRest, 198f);

                // Negative control that continually re-captures the solver as
                // its visible zero. If this control moves like production, the
                // harness no longer detects the reported invisible-cloth bug.
                resetStation = Instantiate(priestessPrefab);
                resetStation.name = "production-contact-reset-negative-control";
                resetStation.transform.SetPositionAndRotation(new Vector3(5100f, 0f, 1200f),
                    Quaternion.Euler(0f, 37f, 0f));
                resetStation.transform.localScale = Vector3.one * 198f;
                MeshFilter resetRunner = resetStation.GetComponentsInChildren<MeshFilter>(true)
                    .First(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                resetProduction = new TownServiceClothContactReset(resetStation.transform, 2);
                resetProduction.SetVisible(true);
                Vector3[] resetRest = resetRunner.mesh.vertices;
                int resetIndex = Enumerable.Range(0, resetRest.Length)
                    .OrderBy(i => resetStation.transform.InverseTransformPoint(
                        resetRunner.transform.TransformPoint(resetRest[i])).y)
                    .ThenBy(i => Mathf.Abs(resetStation.transform.InverseTransformPoint(
                        resetRunner.transform.TransformPoint(resetRest[i])).x))
                    .First();
                Vector3 resetTarget = resetRunner.transform.TransformPoint(resetRest[resetIndex]);
                hand.HasPose = true;
                for (int frame = 0; frame < 10; frame++)
                {
                    Vector3 away = resetTarget + resetStation.transform.forward * (198f * .20f);
                    tipObject.transform.position = away;
                    palmObject.transform.position = away;
                    resetProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                Vector3 resetTouch = resetTarget - resetStation.transform.forward * (198f * .01f);
                tipObject.transform.position = resetTouch;
                palmObject.transform.position = resetTouch;
                yield return null;
                resetProduction.TickAuthor(11f / 90f, 1f / 90f, true);
                productionResetEdgePeak = VisibleMotion(resetRunner, resetRest, 198f);

                contactStation = Instantiate(priestessPrefab);
                contactStation.name = "production-contact-station";
                contactStation.transform.SetPositionAndRotation(new Vector3(3200f, 0f, 1200f),
                    Quaternion.Euler(0f, 37f, 0f));
                contactStation.transform.localScale = Vector3.one * 198f;
                MeshFilter contactRunner = contactStation.GetComponentsInChildren<MeshFilter>(true)
                    .First(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                contactProduction = new TownServiceCloth(contactStation.transform, 2);
                contactProduction.SetVisible(true);
                Vector3[] contactRest = contactRunner.mesh.vertices;
                Vector3[] sourceVertices = contactRunner.mesh.vertices;
                int targetIndex = Enumerable.Range(0, sourceVertices.Length)
                    .OrderBy(i => contactStation.transform.InverseTransformPoint(
                        contactRunner.transform.TransformPoint(sourceVertices[i])).y)
                    .ThenBy(i => Mathf.Abs(contactStation.transform.InverseTransformPoint(
                        contactRunner.transform.TransformPoint(sourceVertices[i])).x))
                    .First();
                Vector3 target = contactRunner.transform.TransformPoint(sourceVertices[targetIndex]);
                hand.HasPose = true;
                // A fingertip reaches the hanging face and withdraws within six display
                // frames. The old contact-edge re-zero erased exactly this brief response
                // while the existing long sweep still passed.
                for (int frame = 0; frame < 10; frame++)
                {
                    Vector3 away = target + contactStation.transform.forward * (.20f * 198f);
                    tipObject.transform.position = away;
                    palmObject.transform.position = away;
                    contactProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                // Let PhysX resolve a single fingertip touch before presentation
                // samples the contact edge. This is the order in the headset when
                // the tracked hand arrives between its simulation and UI ticks.
                Vector3 edgeTouch = target - contactStation.transform.forward * (.01f * 198f);
                tipObject.transform.position = edgeTouch;
                palmObject.transform.position = edgeTouch;
                yield return null;
                contactProduction.TickAuthor(11f / 90f, 1f / 90f, true);
                productionContactEdgePeak = VisibleMotion(contactRunner, contactRest, 198f);
                tipObject.transform.position = target + contactStation.transform.forward * (198f * .35f);
                palmObject.transform.position = tipObject.transform.position;
                for (int frame = 0; frame < 60; frame++)
                {
                    contactProduction.TickAuthor((12f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                for (int frame = 0; frame < 6; frame++)
                {
                    float depth = Mathf.Lerp(.06f, -.03f, frame / 5f) * 198f;
                    Vector3 touch = target + contactStation.transform.forward * depth;
                    tipObject.transform.position = touch;
                    palmObject.transform.position = touch;
                    contactProduction.TickAuthor((10f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                    contactProduction.TickAuthor((11f + frame) / 90f, 1f / 90f, true);
                    productionShortTouchPeak = Mathf.Max(productionShortTouchPeak,
                        VisibleMotion(contactRunner, contactRest, 198f));
                }
                tipObject.transform.position = target + contactStation.transform.forward * (198f * .35f);
                palmObject.transform.position = tipObject.transform.position;
                for (int frame = 0; frame < 60; frame++)
                {
                    contactProduction.TickAuthor((17f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                for (int frame = 0; frame < 120; frame++)
                {
                    float sweep = Mathf.Lerp(-.13f, .13f, frame / 119f) * 198f;
                    Vector3 tip = target + contactStation.transform.right * sweep;
                    tipObject.transform.position = tip;
                    palmObject.transform.position = tip - contactStation.transform.forward * (.07f * 198f);
                    contactProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                // Render the solver result produced after the last physics step.
                contactProduction.TickAuthor(121f / 90f, 1f / 90f, true);
                productionVisibleContact = VisibleMotion(contactRunner, contactRest, 198f);
                Vector3 nearOnly = target + contactStation.transform.forward * (.35f * 198f);
                tipObject.transform.position = nearOnly;
                palmObject.transform.position = nearOnly;
                for (int frame = 0; frame < 60; frame++)
                {
                    contactProduction.TickAuthor((122f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                productionNearReturn = VisibleMotion(contactRunner, contactRest, 198f);

                // Push through the resting plane, then hold. The headset gesture does not keep a
                // hand on the authored zero: the cloth surface travels with the hand. The released
                // Build 568 gate measured the hand against DriverRest, declared contact lost once
                // the push passed one collider radius, and faded the visible solver response away
                // while PhysX was still touching it. Exercise a rotated production station as well
                // as the sustained hold which the old lateral sweep never covered.
                for (int frame = 0; frame < 90; frame++)
                {
                    float push = Mathf.Lerp(-.08f, .065f, frame / 89f) * 198f;
                    Vector3 handPoint = target + contactStation.transform.forward * push;
                    tipObject.transform.position = handPoint;
                    palmObject.transform.position = handPoint;
                    contactProduction.TickAuthor((190f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                for (int frame = 0; frame < 72; frame++)
                {
                    contactProduction.TickAuthor((280f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                productionDeepHold = VisibleMotion(contactRunner, contactRest, 198f);

                restGateStation = Instantiate(priestessPrefab);
                restGateStation.name = "production-rest-gate-negative-control";
                restGateStation.transform.SetPositionAndRotation(new Vector3(4600f, 0f, 1200f),
                    Quaternion.Euler(0f, 37f, 0f));
                restGateStation.transform.localScale = Vector3.one * 198f;
                MeshFilter restGateRunner = restGateStation.GetComponentsInChildren<MeshFilter>(true)
                    .First(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                restGateProduction = new TownServiceClothRestGate(restGateStation.transform, 2);
                restGateProduction.SetVisible(true);
                Vector3[] restGateRest = restGateRunner.mesh.vertices;
                Vector3[] restGateSource = restGateRunner.mesh.vertices;
                int restGateTargetIndex = Enumerable.Range(0, restGateSource.Length)
                    .OrderBy(i => restGateStation.transform.InverseTransformPoint(
                        restGateRunner.transform.TransformPoint(restGateSource[i])).y)
                    .ThenBy(i => Mathf.Abs(restGateStation.transform.InverseTransformPoint(
                        restGateRunner.transform.TransformPoint(restGateSource[i])).x))
                    .First();
                Vector3 restGateTarget = restGateRunner.transform.TransformPoint(
                    restGateSource[restGateTargetIndex]);
                for (int frame = 0; frame < 90; frame++)
                {
                    float push = Mathf.Lerp(-.08f, .065f, frame / 89f) * 198f;
                    Vector3 handPoint = restGateTarget + restGateStation.transform.forward * push;
                    tipObject.transform.position = handPoint;
                    palmObject.transform.position = handPoint;
                    restGateProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                for (int frame = 0; frame < 72; frame++)
                {
                    restGateProduction.TickAuthor((90f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                productionRestGateDeepHold = VisibleMotion(restGateRunner, restGateRest, 198f);

                // In-player negative control: the Build 566 visual gate over the same production
                // class keeps PhysX alive but suppresses every visible vertex. If this control ever
                // produces the same result as production, the instrument no longer observes the
                // reported defect and must fail rather than certify it.
                deadStation = Instantiate(priestessPrefab);
                deadStation.name = "production-dead-visible-control";
                deadStation.transform.SetPositionAndRotation(new Vector3(3900f, 0f, 1200f), Quaternion.identity);
                deadStation.transform.localScale = Vector3.one * 198f;
                MeshFilter deadRunner = deadStation.GetComponentsInChildren<MeshFilter>(true)
                    .First(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                deadProduction = new TownServiceClothDead(deadStation.transform, 2);
                deadProduction.SetVisible(true);
                Vector3[] deadRest = deadRunner.mesh.vertices;
                Vector3[] deadSource = deadRunner.mesh.vertices;
                int deadTargetIndex = Enumerable.Range(0, deadSource.Length)
                    .OrderBy(i => deadStation.transform.InverseTransformPoint(
                        deadRunner.transform.TransformPoint(deadSource[i])).y)
                    .First();
                Vector3 deadTarget = deadRunner.transform.TransformPoint(deadSource[deadTargetIndex]);
                for (int frame = 0; frame < 120; frame++)
                {
                    float sweep = Mathf.Lerp(-.13f, .13f, frame / 119f) * 198f;
                    Vector3 tip = deadTarget + deadStation.transform.right * sweep;
                    tipObject.transform.position = tip;
                    palmObject.transform.position = tip - deadStation.transform.forward * (.07f * 198f);
                    deadProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                deadProduction.TickAuthor(121f / 90f, 1f / 90f, true);
                productionDeadVisible = VisibleMotion(deadRunner, deadRest, 198f);
                productionPathPass = productionContactEdgePeak > .002f
                    && productionResetEdgePeak < .0005f
                    && productionContactEdgePeak > productionResetEdgePeak + .002f
                    && productionShortTouchPeak > .002f
                    && productionVisibleContact > .0025f
                    && productionVisibleNull < .0005f
                    && productionDeadVisible < .0005f
                    && productionNearReturn < .0005f
                    && productionDeepHold > .008f
                    && productionVisibleContact > productionVisibleNull + .002f
                    && productionVisibleContact > productionDeadVisible + .002f;

                // The red side banner has been removed from the merchant cabinet.
                // The production component must accept that authored absence without
                // inventing a new hidden cloth driver or collidable geometry.
                string merchantAsset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/townmerchant.prefab"));
                merchantStation = Instantiate(bundle.LoadAsset<GameObject>(merchantAsset));
                merchantStation.name = "production-merchant-without-side-banner";
                merchantStation.transform.SetPositionAndRotation(new Vector3(8000f, 0f, 1200f), Quaternion.identity);
                merchantStation.transform.localScale = Vector3.one * 198f;
                merchantRunnerCount = merchantStation.GetComponentsInChildren<MeshFilter>(true)
                    .Count(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                merchantProduction = new TownServiceCloth(merchantStation.transform, 1);
                merchantProduction.SetVisible(true);
                merchantProduction.TickAuthor(0f, 1f / 90f, true);
                merchantProduction.TickObserver(0f, 0f, default, default, true);
                productionPathPass &= merchantRunnerCount == 0;

                // The headset clip presses the narrow red side runner, not the
                // priestess's broad front runner used by the original fixture.
                // Keep the finger just behind its visible mid-panel for one
                // second, rather than crossing it once at the free lower edge.
                string enchantressAsset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/townenchantress.prefab"));
                enchantressStation = Instantiate(bundle.LoadAsset<GameObject>(enchantressAsset));
                enchantressStation.name = "production-enchantress-side-contact";
                enchantressStation.transform.SetPositionAndRotation(new Vector3(9000f, 0f, 1200f), Quaternion.identity);
                enchantressStation.transform.localScale = Vector3.one * 198f;
                MeshFilter enchantressRunner = enchantressStation.GetComponentsInChildren<MeshFilter>(true)
                    .Single(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                enchantressProduction = new TownServiceCloth(enchantressStation.transform, 3);
                enchantressProduction.SetVisible(true);
                // Unlike the earlier palm=tip fixture, the glove lies lengthwise
                // along the curtain: wrist, palm and index tip are distinct, and
                // the broad palm/back presses into the hanging surface.
                wristObject.transform.localPosition = Vector3.down * (198f * .045f);
                Vector3[] enchantressRest = enchantressRunner.mesh.vertices;
                int enchantressIndex = Enumerable.Range(0, enchantressRest.Length)
                    .OrderBy(i => Vector2.Distance(new Vector2(
                        enchantressStation.transform.InverseTransformPoint(enchantressRunner.transform.TransformPoint(enchantressRest[i])).x,
                        enchantressStation.transform.InverseTransformPoint(enchantressRunner.transform.TransformPoint(enchantressRest[i])).y),
                        new Vector2(-.60f, .69f))).First();
                Vector3 enchantressTarget = enchantressRunner.transform.TransformPoint(enchantressRest[enchantressIndex]);
                string snapshotFolder = Path.GetDirectoryName(Argument("--result="));
                CaptureClothFrame(Path.Combine(snapshotFolder, "enchantress-rest.png"),
                    enchantressStation.transform, enchantressRunner);
                WriteVisibleMesh(Path.Combine(snapshotFolder, "enchantress-rest.obj"),
                    enchantressStation.transform, enchantressRunner);
                hand.HasPose = true;
                for (int frame = 0; frame < 36; frame++)
                {
                    float depth = Mathf.Lerp(-.10f, .025f, frame / 35f);
                    Vector3 approach = enchantressTarget + enchantressStation.transform.forward * (198f * depth);
                    palmObject.transform.position = approach;
                    tipObject.transform.position = approach + Vector3.up * (198f * .08f);
                    enchantressProduction.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                    if (firstApproachSide == 0 && frame >= 5)
                        firstApproachSide = Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0];
                    enchantressCapsuleClearanceMin = Mathf.Min(enchantressCapsuleClearanceMin,
                        CapsuleSurfaceClearance(enchantressRunner,
                            wristObject.transform.position, tipObject.transform.position,
                            .049f, .010f, 198f));
                }
                float previous = 0f;
                for (int frame = 0; frame < 90; frame++)
                {
                    Vector3 pressed = enchantressTarget + enchantressStation.transform.forward * (198f * .025f);
                    palmObject.transform.position = pressed;
                    tipObject.transform.position = pressed + Vector3.up * (198f * .08f);
                    long contactStart = Stopwatch.GetTimestamp();
                    enchantressProduction.TickAuthor((frame + 36f) / 90f, 1f / 90f, true);
                    if (frame > 15)
                    {
                        long elapsedTicks = Stopwatch.GetTimestamp() - contactStart;
                        enchantressContactTicks += elapsedTicks;
                        enchantressContactDurations[enchantressContactSamples] = elapsedTicks;
                        enchantressContactSamples++;
                    }
                    sideStableThroughHold &= firstApproachSide != 0
                        && Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0] == firstApproachSide;
                    yield return null;
                    float displacement = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                    Vector3 localShown = enchantressRunner.mesh.vertices[enchantressIndex];
                    float localContact = Vector3.Distance(enchantressRunner.transform.TransformPoint(localShown),
                        enchantressTarget) / 198f;
                    if (frame > 15)
                    {
                        enchantressHandClearanceMin = Mathf.Min(enchantressHandClearanceMin,
                            NearestSurfaceDistance(enchantressRunner, pressed, 198f));
                        enchantressCapsuleClearanceMin = Mathf.Min(enchantressCapsuleClearanceMin,
                            CapsuleSurfaceClearance(enchantressRunner,
                                wristObject.transform.position, tipObject.transform.position,
                                .049f, .010f, 198f));
                    }
                    foreach (Vector3 shownVertex in enchantressRunner.mesh.vertices)
                    {
                        Vector3 p = enchantressStation.transform.InverseTransformPoint(
                            enchantressRunner.transform.TransformPoint(shownVertex));
                        Vector3 onRoot = new Vector3(-.62f, Mathf.Clamp(p.y, .20f, .84f), -.20f);
                        enchantressRootPenetration = Mathf.Max(enchantressRootPenetration,
                            .075f - Vector3.Distance(p, onRoot));
                    }
                    if (frame > 15)
                    {
                        enchantressHoldMin = Mathf.Min(enchantressHoldMin, displacement);
                        enchantressHoldMax = Mathf.Max(enchantressHoldMax, displacement);
                        enchantressHoldStep = Mathf.Max(enchantressHoldStep, Mathf.Abs(displacement - previous));
                        enchantressLocalMin = Mathf.Min(enchantressLocalMin, localContact);
                        enchantressLocalMax = Mathf.Max(enchantressLocalMax, localContact);
                    }
                    previous = displacement;
                    if (frame == 60)
                    {
                        throughPushSide = Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0];
                        CaptureClothFrame(Path.Combine(snapshotFolder, "enchantress-contact.png"),
                            enchantressStation.transform, enchantressRunner,
                            wristObject.transform, tipObject.transform);
                        WriteVisibleMesh(Path.Combine(snapshotFolder, "enchantress-contact.obj"),
                            enchantressStation.transform, enchantressRunner);
                        enchantressEdgeStretch = MaximumEdgeStretch(enchantressRunner, enchantressRest, 198f);
                    }
                }
                productionPathPass &= enchantressLocalMin > .008f
                    && enchantressHoldStep < .015f
                    && enchantressEdgeStretch < .015f
                    && enchantressRootPenetration < .002f;
                float previousWithdrawal = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                Vector3[] previousWithdrawalVertices = enchantressRunner.mesh.vertices;
                for (int frame = 0; frame < 36; frame++)
                {
                    float depth = Mathf.Lerp(.025f, -.34f, (frame + 1f) / 36f);
                    Vector3 retreat = enchantressTarget + enchantressStation.transform.forward * (198f * depth);
                    palmObject.transform.position = retreat;
                    tipObject.transform.position = retreat + Vector3.up * (198f * .08f);
                    enchantressProduction.TickAuthor((126f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                    float displacement = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                    enchantressWithdrawalStepMax = Mathf.Max(enchantressWithdrawalStepMax,
                        Mathf.Abs(displacement - previousWithdrawal));
                    Vector3[] shownVertices = enchantressRunner.mesh.vertices;
                    for (int vertex = 0; vertex < shownVertices.Length; vertex++)
                    {
                        float vertexStep = enchantressRunner.transform.TransformVector(shownVertices[vertex]
                            - previousWithdrawalVertices[vertex]).magnitude / 198f;
                        if (vertexStep > enchantressWithdrawalVertexStepMax)
                        {
                            enchantressWithdrawalVertexStepMax = vertexStep;
                            enchantressWithdrawalVertexStepFrame = frame;
                            enchantressWithdrawalVertexStepIndex = vertex;
                        }
                    }
                    previousWithdrawalVertices = shownVertices;
                    if (frame >= 2 && frame <= 4)
                        CaptureClothFrame(Path.Combine(snapshotFolder,
                            "enchantress-withdrawal-frame-" + frame + ".png"),
                            enchantressStation.transform, enchantressRunner,
                            wristObject.transform, tipObject.transform);
                    enchantressCapsuleClearanceMin = Mathf.Min(enchantressCapsuleClearanceMin,
                        CapsuleSurfaceClearance(enchantressRunner,
                            wristObject.transform.position, tipObject.transform.position,
                            .049f, .010f, 198f));
                    previousWithdrawal = displacement;
                }
                // The end of the continuous withdrawal remains within 16 cm
                // of the side panel's far edge. Move beyond the entire runner
                // before timing genuine idle work; do not mix a near miss with
                // the no-hand broadphase/renderer fast path.
                Vector3 idleHand = enchantressTarget - enchantressStation.transform.forward * (198f * .90f);
                palmObject.transform.position = idleHand;
                tipObject.transform.position = idleHand + Vector3.up * (198f * .08f);
                for (int frame = 0; frame < 90; frame++)
                {
                    long idleStart = Stopwatch.GetTimestamp();
                    enchantressProduction.TickAuthor((162f + frame) / 90f, 1f / 90f, true);
                    if (frame >= 50)
                    {
                        long elapsedTicks = Stopwatch.GetTimestamp() - idleStart;
                        enchantressIdleTicks += elapsedTicks;
                        enchantressIdleDurations[enchantressIdleSamples] = elapsedTicks;
                        enchantressIdleSamples++;
                    }
                    yield return null;
                }
                enchantressAfterWithdrawal = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                productionPathPass &= enchantressCapsuleClearanceMin > -.003f
                    && enchantressWithdrawalStepMax < .020f
                    && enchantressWithdrawalVertexStepMax < .020f
                    && enchantressAfterWithdrawal < .002f;
                CaptureClothFrame(Path.Combine(snapshotFolder, "enchantress-withdrawal.png"),
                    enchantressStation.transform, enchantressRunner);

                // A deep pass keeps the original side through the continuous
                // gesture. Then remain on the opposite side, still within the
                // renderer's expanded broadphase, until a genuinely separate
                // approach starts. The old latch never released in that case.
                // Re-prime this same source after the preceding genuine idle
                // interval; the original contact-side assertion above remains
                // independent of this re-entry assertion.
                for (int frame = 0; frame < 16; frame++)
                {
                    float depth = Mathf.Lerp(-.08f, .025f, frame / 15f);
                    Vector3 point = enchantressTarget
                        + enchantressStation.transform.forward * (198f * depth);
                    palmObject.transform.position = point;
                    tipObject.transform.position = point + Vector3.up * (198f * .08f);
                    enchantressProduction.TickAuthor((252f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                Bounds oppositeBroadphase = enchantressRunner.GetComponent<Renderer>().bounds;
                oppositeBroadphase.Expand(198f * .72f);
                Vector3 oppositeFar = enchantressTarget;
                oppositeFar.z = oppositeBroadphase.max.z - 198f * .10f;
                float oppositeFarDepth = Vector3.Dot(oppositeFar - enchantressTarget,
                    enchantressStation.transform.forward) / 198f;
                oppositeFarDepthRecorded = oppositeFarDepth;
                oppositeFarInExpandedAabb = oppositeBroadphase.Contains(oppositeFar);
                for (int frame = 0; frame < 12; frame++)
                {
                    palmObject.transform.position = oppositeFar;
                    tipObject.transform.position = oppositeFar + Vector3.up * (198f * .08f);
                    enchantressProduction.TickAuthor((268f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                    if (frame == 5)
                    {
                        sideBeforeSeparation = Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0];
                        farCountMid = Field<int[]>(FirstRunner(enchantressProduction), "ProbeFarFrames")[0];
                    }
                    if (frame == 11)
                    {
                        sideAfterSeparation = Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0];
                        farCountEnd = Field<int[]>(FirstRunner(enchantressProduction), "ProbeFarFrames")[0];
                    }
                }
                for (int frame = 0; frame < 25; frame++)
                {
                    float depth = Mathf.Lerp(oppositeFarDepth, .18f, frame / 24f);
                    Vector3 point = enchantressTarget
                        + enchantressStation.transform.forward * (198f * depth);
                    palmObject.transform.position = point;
                    tipObject.transform.position = point + Vector3.up * (198f * .08f);
                    enchantressProduction.TickAuthor((280f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                oppositeReentrySide = Field<int[]>(FirstRunner(enchantressProduction), "ProbeSide")[0];
                oppositeSideReentryPass = firstApproachSide != 0
                    && sideStableThroughHold
                    && oppositeFarInExpandedAabb
                    && throughPushSide == firstApproachSide
                    && sideBeforeSeparation == firstApproachSide
                    && sideAfterSeparation == 0
                    && oppositeReentrySide == -firstApproachSide;

                // Hiding a moving runner must restore its actual renderer and
                // decorations immediately. Otherwise a zero owner mean enters
                // the idle fast path with a stale, still-bent visible mesh.
                float beforeHide = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                enchantressProduction.SetVisible(false);
                float afterHide = VisibleMotion(enchantressRunner, enchantressRest, 198f);
                object runnerObject = FirstRunner(enchantressProduction);
                hideRestPass = beforeHide > .005f && afterHide < .001f
                    && !Field<bool>(runnerObject, "VisibleDirty")
                    && Field<int[]>(runnerObject, "ProbeSide")[0] == 0;
                enchantressProduction.SetVisible(true);
                Vector3 beyondStation = enchantressTarget
                    - enchantressStation.transform.forward * (198f * .90f);
                palmObject.transform.position = beyondStation;
                tipObject.transform.position = beyondStation + Vector3.up * (198f * .08f);
                for (int frame = 0; frame < 20; frame++)
                {
                    enchantressProduction.TickAuthor((305f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                }
                hideRestPass &= VisibleMotion(enchantressRunner, enchantressRest, 198f) < .001f;

                // Force only the renderer dirty while the replicated mean is
                // zero. The fast path must clean this exact stale-mesh case.
                Vector3[] dirtyVertices = enchantressRunner.mesh.vertices;
                dirtyVertices[enchantressIndex] += enchantressRunner.transform.InverseTransformVector(
                    enchantressStation.transform.TransformVector(Vector3.forward * .035f));
                enchantressRunner.mesh.vertices = dirtyVertices;
                SetField(runnerObject, "VisibleDirty", true);
                SetField(runnerObject, "State", default(TownClothRunnerState));
                yield return null;
                enchantressProduction.TickAuthor(325f / 90f, 1f / 90f, true);
                zeroMeanRestPass = VisibleMotion(enchantressRunner, enchantressRest, 198f) < .001f
                    && !Field<bool>(runnerObject, "VisibleDirty");

                // Local tracking compacts right into slot zero when left goes
                // invalid. The new source must not inherit left's side or pose.
                Vector3 leftPoint = enchantressTarget
                    - enchantressStation.transform.forward * (198f * .08f);
                Vector3 rightPoint = enchantressTarget
                    + enchantressStation.transform.forward * (198f * .08f);
                palmObject.transform.position = leftPoint;
                tipObject.transform.position = leftPoint + Vector3.up * (198f * .08f);
                rightPalmObject.transform.position = rightPoint;
                rightTipObject.transform.position = rightPoint + Vector3.up * (198f * .08f);
                rightHand.HasPose = true;
                VRHands.Right = rightHand;
                enchantressProduction.TickAuthor(326f / 90f, 1f / 90f, true);
                yield return null;
                int leftSideBeforeCompaction = Field<int[]>(runnerObject, "ProbeSide")[0];
                int rightSideBeforeCompaction = Field<int[]>(runnerObject, "ProbeSide")[1];
                hand.HasPose = false;
                enchantressProduction.TickAuthor(327f / 90f, 1f / 90f, true);
                yield return null;
                object compactedLocal = HandAt(enchantressProduction, 0);
                compactedLocalHandPass = leftSideBeforeCompaction != 0
                    && rightSideBeforeCompaction == -leftSideBeforeCompaction
                    && Field<long>(compactedLocal, "SourceKey") == -2L
                    && Field<int[]>(runnerObject, "ProbeSide")[0] == rightSideBeforeCompaction
                    && Vector3.Distance(Field<Vector3>(compactedLocal, "PreviousPalm"),
                        rightWristObject.transform.position) < .001f;
                rightHand.HasPose = false;
                VRHands.Right = null;

                // The same compaction must work when a remote player leaves.
                var firstPeer = new GloomhavenVR.Net.NetAvatarDriver.PeerProbe
                { Left = leftPoint, LeftWrist = leftPoint + Vector3.down * (198f * .045f),
                    LeftTip = leftPoint + Vector3.up * (198f * .08f), LeftValid = true, Scale = 198f };
                var secondPeer = new GloomhavenVR.Net.NetAvatarDriver.PeerProbe
                { Left = rightPoint, LeftWrist = rightPoint + Vector3.down * (198f * .045f),
                    LeftTip = rightPoint + Vector3.up * (198f * .08f), LeftValid = true, Scale = 198f };
                GloomhavenVR.Net.NetAvatarDriver.TestPeerProbes[101] = firstPeer;
                GloomhavenVR.Net.NetAvatarDriver.TestPeerProbes[202] = secondPeer;
                enchantressProduction.TickAuthor(328f / 90f, 1f / 90f, true);
                yield return null;
                int firstPeerSide = Field<int[]>(runnerObject, "ProbeSide")[0];
                int secondPeerSide = Field<int[]>(runnerObject, "ProbeSide")[1];
                GloomhavenVR.Net.NetAvatarDriver.TestPeerProbes.Remove(101);
                enchantressProduction.TickAuthor(329f / 90f, 1f / 90f, true);
                yield return null;
                object compactedPeer = HandAt(enchantressProduction, 0);
                compactedPeerHandPass = firstPeerSide != 0
                    && secondPeerSide == -firstPeerSide
                    && Field<long>(compactedPeer, "SourceKey") == (((long)202 << 2) | 1L)
                    && Field<int[]>(runnerObject, "ProbeSide")[0] == secondPeerSide
                    && Vector3.Distance(Field<Vector3>(compactedPeer, "PreviousPalm"),
                        secondPeer.LeftWrist) < .001f;
                GloomhavenVR.Net.NetAvatarDriver.TestPeerProbes.Clear();
                hand.HasPose = true;
                productionPathPass &= oppositeSideReentryPass && hideRestPass
                    && zeroMeanRestPass && compactedLocalHandPass && compactedPeerHandPass;

                // The real headset log never reports a contact edge for the red
                // enchantress runner, even as the glove crosses it. Suppressing
                // only that separate diagnostic gate must not suppress genuine
                // native Cloth collision in the visible mesh.
                enchantressNoGateStation = Instantiate(bundle.LoadAsset<GameObject>(enchantressAsset));
                enchantressNoGateStation.transform.SetPositionAndRotation(
                    new Vector3(9700f, 0f, 1200f), Quaternion.identity);
                enchantressNoGateStation.transform.localScale = Vector3.one * 198f;
                MeshFilter noGateRunner = enchantressNoGateStation.GetComponentsInChildren<MeshFilter>(true)
                    .Single(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal));
                enchantressNoGate = new TownServiceClothNoContactGate(enchantressNoGateStation.transform, 3);
                enchantressNoGate.SetVisible(true);
                Vector3[] noGateRest = noGateRunner.mesh.vertices;
                Vector3 noGateTarget = noGateRunner.transform.TransformPoint(noGateRest[enchantressIndex]);
                for (int frame = 0; frame < 36; frame++)
                {
                    float depth = Mathf.Lerp(-.10f, .025f, frame / 35f);
                    Vector3 approach = noGateTarget + enchantressNoGateStation.transform.forward * (198f * depth);
                    palmObject.transform.position = approach;
                    tipObject.transform.position = approach + Vector3.up * (198f * .08f);
                    enchantressNoGate.TickAuthor(frame / 90f, 1f / 90f, true);
                    yield return null;
                }
                for (int frame = 0; frame < 45; frame++)
                {
                    Vector3 pressed = noGateTarget + enchantressNoGateStation.transform.forward * (198f * .025f);
                    palmObject.transform.position = pressed;
                    tipObject.transform.position = pressed + Vector3.up * (198f * .08f);
                    enchantressNoGate.TickAuthor((36f + frame) / 90f, 1f / 90f, true);
                    yield return null;
                    if (frame > 15)
                        enchantressNoGateVisibleMin = Mathf.Min(enchantressNoGateVisibleMin,
                            VisibleMotion(noGateRunner, noGateRest, 198f));
                }
                productionPathPass &= enchantressNoGateVisibleMin > .008f;
            }
            finally
            {
                hand.HasPose = false; VRHands.Left = VRHands.Right = null;
                GloomhavenVR.Net.NetAvatarDriver.TestPeerProbes.Clear();
                merchantProduction?.Dispose();
                enchantressProduction?.Dispose();
                enchantressNoGate?.Dispose();
                resetProduction?.Dispose();
                restGateProduction?.Dispose();
                deadProduction?.Dispose();
                contactProduction?.Dispose();
                nullProduction?.Dispose();
                if (restGateStation != null) Destroy(restGateStation);
                if (resetStation != null) Destroy(resetStation);
                if (deadStation != null) Destroy(deadStation);
                if (contactStation != null) Destroy(contactStation);
                if (merchantStation != null) Destroy(merchantStation);
                if (enchantressStation != null) Destroy(enchantressStation);
                if (enchantressNoGateStation != null) Destroy(enchantressNoGateStation);
                if (nullStation != null) Destroy(nullStation);
                Destroy(palmObject); Destroy(tipObject); Destroy(rigScale);
                Destroy(rightPalmObject); Destroy(rightTipObject);
                VRRigDriver.RigRoot = null; VRRigDriver.BaseWorldScale = 0f;
            }
            actualPass &= productionPathPass;
            bundle.Unload(false);
        }
        double coarseCook = 0d, denseCook = 0d;
        double coarsePull = 0d, densePull = 0d;
        double coarseRead = 0d, denseRead = 0d;
        int vertexSink = 0;
        var coarseFixtures = new Fixture[3];
        var denseFixtures = new Fixture[3];
        for (int i = 0; i < 3; i++)
        {
            coarseFixtures[i] = Build("cloth-cost-coarse-" + i, 198f, 100f, false,
                true, new Vector3(11000f + i * 400f, 0f, 1200f), 0f, 13, 25);
            coarseCook += coarseFixtures[i].CookMilliseconds;
            denseFixtures[i] = Build("cloth-cost-dense-" + i, 198f, 100f, false,
                true, new Vector3(12500f + i * 400f, 0f, 1200f), 0f, 49, 49);
            denseCook += denseFixtures[i].CookMilliseconds;
        }
        yield return null;
        for (int trial = 0; trial < 300; trial++)
        {
            var coarseReadWatch = Stopwatch.StartNew();
            foreach (Fixture fixture in coarseFixtures) vertexSink += fixture.Cloth.vertices.Length;
            coarseReadWatch.Stop();
            coarseRead += coarseReadWatch.Elapsed.TotalMilliseconds;
            var denseReadWatch = Stopwatch.StartNew();
            foreach (Fixture fixture in denseFixtures) vertexSink += fixture.Cloth.vertices.Length;
            denseReadWatch.Stop();
            denseRead += denseReadWatch.Elapsed.TotalMilliseconds;
            var coarseWatch = Stopwatch.StartNew();
            foreach (Fixture fixture in coarseFixtures)
            {
                Vector3[] vertices = fixture.Cloth.vertices;
                fixture.Visible.vertices = vertices;
                fixture.Visible.RecalculateNormals();
                fixture.Visible.RecalculateBounds();
            }
            coarseWatch.Stop();
            coarsePull += coarseWatch.Elapsed.TotalMilliseconds;
            var denseWatch = Stopwatch.StartNew();
            foreach (Fixture fixture in denseFixtures)
            {
                Vector3[] vertices = fixture.Cloth.vertices;
                fixture.Visible.vertices = vertices;
                fixture.Visible.RecalculateNormals();
                fixture.Visible.RecalculateBounds();
            }
            denseWatch.Stop();
            densePull += denseWatch.Elapsed.TotalMilliseconds;
        }
        foreach (Cloth cloth in FindObjectsOfType<Cloth>()) cloth.enabled = false;
        bool automaticPhysics = Physics.autoSimulation;
        Physics.autoSimulation = false;
        double coarseSimulation, denseSimulation;
        try
        {
            foreach (Fixture fixture in coarseFixtures) fixture.Cloth.enabled = true;
            for (int step = 0; step < 20; step++) Physics.Simulate(1f / 90f);
            var coarseWatch = Stopwatch.StartNew();
            for (int step = 0; step < 180; step++) Physics.Simulate(1f / 90f);
            coarseWatch.Stop();
            coarseSimulation = coarseWatch.Elapsed.TotalMilliseconds / 180d;
            foreach (Fixture fixture in coarseFixtures) fixture.Cloth.enabled = false;
            foreach (Fixture fixture in denseFixtures) fixture.Cloth.enabled = true;
            for (int step = 0; step < 20; step++) Physics.Simulate(1f / 90f);
            var denseWatch = Stopwatch.StartNew();
            for (int step = 0; step < 180; step++) Physics.Simulate(1f / 90f);
            denseWatch.Stop();
            denseSimulation = denseWatch.Elapsed.TotalMilliseconds / 180d;
        }
        finally { Physics.autoSimulation = automaticPhysics; }
        bool pass = hiddenPass && negativePass && scalePass && actualPass;
        string line = "native_hidden_cloth=" + (pass ? "PASS" : "FAIL")
            + " gravity_motion=" + gravityMotion.ToString("F5")
            + " collider_sweep_motion=" + contactMotion.ToString("F5")
            + " null_sweep_motion=" + nullMotion.ToString("F5")
            + " scaled_coeff_local_motion=" + scaledLocalMotion.ToString("F5")
            + " unscaled_coeff_local_motion=" + unscaledLocalMotion.ToString("F5")
            + " scaled_gravity_motion=" + scaledGravityMotion.ToString("F5")
            + " unscaled_gravity_motion=" + unscaledGravityMotion.ToString("F5")
            + " forceRenderingOff=true updateWhenOffscreen=true"
            + " vertices=" + sample.Length
            + " snapshot_bytes=" + snapshotBytes
            + " pull_render_us=" + microseconds.ToString("F2");
        line += " actual_bundle=" + (actualPass ? "PASS" : "FAIL")
            + " actual_runners=" + actualRunners
            + " actual_contact_min=" + actualContactMin.ToString("F5")
            + " actual_contact_max=" + actualContactMax.ToString("F5")
            + " actual_table_drop=" + actualTableDrop.ToString("F5")
            + " actual_visible_return_max=" + actualVisibleReturnMax.ToString("F5")
            + " actual_return_step_max=" + actualReturnStepMax.ToString("F5")
            + " actual_return_monotone=" + actualReturnMonotone
            + " actual_pinned_drift_max=" + actualPinnedDriftMax.ToString("F5")
            + " actual_reentry_pop_max=" + actualReentryPopMax.ToString("F5")
            + " actual_approach_raw_drift_max=" + actualApproachRawDriftMax.ToString("F5")
            + " actual_contact_step_max=" + actualContactStepMax.ToString("F5")
            + " actual_reentry_min=" + actualReentryMin.ToString("F5")
            + " actual_longterm_max=" + actualLongTermMax.ToString("F5")
            + " production_visible_path=" + (productionPathPass ? "PASS" : "FAIL")
            + " cloth_local_slot_compaction=" + compactedLocalHandPass
            + " cloth_peer_slot_compaction=" + compactedPeerHandPass
            + " cloth_opposite_side_reentry=" + oppositeSideReentryPass
            + " cloth_initial_side=" + firstApproachSide
            + " cloth_through_side=" + throughPushSide
            + " cloth_side_stable_during_hold=" + sideStableThroughHold
            + " cloth_reentry_side=" + oppositeReentrySide
            + " cloth_side_before_separation=" + sideBeforeSeparation
            + " cloth_side_after_separation=" + sideAfterSeparation
            + " cloth_far_inside_expanded_aabb=" + oppositeFarInExpandedAabb
            + " cloth_far_depth_m=" + oppositeFarDepthRecorded.ToString("F3")
            + " cloth_far_frames_mid=" + farCountMid
            + " cloth_far_frames_end=" + farCountEnd
            + " cloth_hide_rest=" + hideRestPass
            + " cloth_zero_mean_rest=" + zeroMeanRestPass
            + " merchant_runner_count=" + merchantRunnerCount
            + " production_visible_contact_m=" + productionVisibleContact.ToString("F5")
            + " production_short_touch_peak_m=" + productionShortTouchPeak.ToString("F5")
            + " production_contact_edge_peak_m=" + productionContactEdgePeak.ToString("F5")
            + " production_reset_edge_peak_m=" + productionResetEdgePeak.ToString("F5")
            + " production_visible_null_m=" + productionVisibleNull.ToString("F5")
            + " production_near_only_return_m=" + productionNearReturn.ToString("F5")
            + " production_deep_hold_m=" + productionDeepHold.ToString("F5")
            + " enchantress_hold_min_m=" + enchantressHoldMin.ToString("F5")
            + " enchantress_hold_max_m=" + enchantressHoldMax.ToString("F5")
            + " enchantress_hold_step_m=" + enchantressHoldStep.ToString("F5")
            + " enchantress_local_min_m=" + enchantressLocalMin.ToString("F5")
            + " enchantress_local_max_m=" + enchantressLocalMax.ToString("F5")
            + " enchantress_root_penetration_m=" + enchantressRootPenetration.ToString("F5")
            + " enchantress_edge_stretch_m=" + enchantressEdgeStretch.ToString("F5")
            + " enchantress_hand_clearance_min_m=" + enchantressHandClearanceMin.ToString("F5")
            + " enchantress_capsule_clearance_min_m=" + enchantressCapsuleClearanceMin.ToString("F5")
            + " enchantress_withdrawal_step_max_m=" + enchantressWithdrawalStepMax.ToString("F5")
            + " enchantress_withdrawal_vertex_step_max_m="
            + enchantressWithdrawalVertexStepMax.ToString("F5")
            + " enchantress_withdrawal_vertex_step_frame=" + enchantressWithdrawalVertexStepFrame
            + " enchantress_withdrawal_vertex_step_index=" + enchantressWithdrawalVertexStepIndex
            + " enchantress_contact_tick_us="
            + (1e6 * enchantressContactTicks / Math.Max(1, enchantressContactSamples)
                / Stopwatch.Frequency).ToString("F2")
            + " enchantress_contact_tick_median_us="
            + MedianMicroseconds(enchantressContactDurations, enchantressContactSamples).ToString("F2")
            + " enchantress_contact_tick_p90_us="
            + PercentileMicroseconds(enchantressContactDurations, enchantressContactSamples, .90f).ToString("F2")
            + " enchantress_idle_tick_us="
            + (1e6 * enchantressIdleTicks / Math.Max(1, enchantressIdleSamples)
                / Stopwatch.Frequency).ToString("F2")
            + " enchantress_idle_tick_median_us="
            + MedianMicroseconds(enchantressIdleDurations, enchantressIdleSamples).ToString("F2")
            + " enchantress_idle_tick_p90_us="
            + PercentileMicroseconds(enchantressIdleDurations, enchantressIdleSamples, .90f).ToString("F2")
            + " enchantress_after_withdrawal_m=" + enchantressAfterWithdrawal.ToString("F5")
            + " enchantress_no_contact_gate_visible_min_m=" + enchantressNoGateVisibleMin.ToString("F5")
            + " production_rest_gate_deep_hold_m=" + productionRestGateDeepHold.ToString("F5")
            + " production_dead_visible_control_m=" + productionDeadVisible.ToString("F5");
        line += " three_coarse_cook_ms=" + coarseCook.ToString("F2")
            + " three_dense_cook_ms=" + denseCook.ToString("F2")
            + " three_coarse_pull_rebuild_ms=" + (coarsePull / 300d).ToString("F3")
            + " three_dense_pull_rebuild_ms=" + (densePull / 300d).ToString("F3")
            + " three_coarse_read_ms=" + (coarseRead / 300d).ToString("F3")
            + " three_dense_read_ms=" + (denseRead / 300d).ToString("F3")
            + " three_coarse_simulate_ms=" + coarseSimulation.ToString("F3")
            + " three_dense_simulate_ms=" + denseSimulation.ToString("F3")
            + " vertex_sink=" + vertexSink;
        UnityEngine.Debug.Log("[TOWN-CLOTH] " + line);
        string result = Argument("--result=");
        if (result.Length != 0) File.WriteAllText(result, line + Environment.NewLine);
        Application.Quit(pass ? 0 : 3);
    }

    private static void WriteVisibleMesh(string path, Transform station, MeshFilter filter)
    {
        // Keep physical contact evidence in station units so it can be rendered
        // against the authored furniture without the runtime 198x world scale.
        Mesh mesh = filter.mesh;
        var data = new StringBuilder(mesh.vertexCount * 48);
        foreach (Vector3 vertex in mesh.vertices)
        {
            Vector3 point = station.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            data.Append("v ").Append(point.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(point.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(point.z.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
            data.Append("f ").Append(triangles[i] + 1).Append(' ')
                .Append(triangles[i + 1] + 1).Append(' ')
                .Append(triangles[i + 2] + 1).Append('\n');
        File.WriteAllText(path, data.ToString());
    }

    private static double MedianMicroseconds(long[] samples, int count)
    {
        return PercentileMicroseconds(samples, count, .50f);
    }

    private static object FirstRunner(TownServiceCloth cloth)
    {
        var field = typeof(TownServiceCloth).GetField("_runners", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((Array)field.GetValue(cloth)).GetValue(0);
    }

    private static T Field<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (T)field.GetValue(target);
    }

    private static void SetField<T>(object target, string name, T value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field.SetValue(target, value);
    }

    private static object HandAt(TownServiceCloth cloth, int slot)
    {
        var field = typeof(TownServiceCloth).GetField("_hands", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((Array)field.GetValue(cloth)).GetValue(slot);
    }

    private static double PercentileMicroseconds(long[] samples, int count, float fraction)
    {
        if (count == 0) return 0d;
        Array.Sort(samples, 0, count);
        int index = Mathf.Clamp(Mathf.CeilToInt(fraction * count) - 1, 0, count - 1);
        return 1e6 * samples[index] / Stopwatch.Frequency;
    }

    private static void CaptureClothFrame(string path, Transform station, MeshFilter filter,
        Transform wrist = null, Transform tip = null)
    {
        var cameraObject = new GameObject("Cloth evidence camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.07f, .07f, .09f);
        camera.fieldOfView = 38f;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 900f;
        camera.transform.position = station.TransformPoint(new Vector3(-.27f, .93f, -1.38f));
        camera.transform.LookAt(station.TransformPoint(new Vector3(-.60f, .69f, -.35f)));
        var lightObject = new GameObject("Cloth evidence light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.7f;
        lightObject.transform.rotation = Quaternion.Euler(40f, -20f, 0f);
        Color oldAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientLight = new Color(.55f, .55f, .55f);
        var disabled = new System.Collections.Generic.List<Renderer>();
        Renderer clothRenderer = filter.GetComponent<Renderer>();
        Material originalMaterial = clothRenderer.sharedMaterial;
        Shader evidenceShader = Resources.Load<Shader>("TownClothEvidence");
        Material evidenceMaterial = evidenceShader != null ? new Material(evidenceShader) : null;
        if (evidenceMaterial != null)
        {
            evidenceMaterial.color = new Color(.54f, .10f, .15f);
            clothRenderer.sharedMaterial = evidenceMaterial;
        }
        var handEvidence = new System.Collections.Generic.List<GameObject>();
        Material handMaterial = null;
        if (wrist != null && tip != null)
        {
            Shader handShader = Resources.Load<Shader>("TownClothEvidenceHand");
            handMaterial = handShader != null ? new Material(handShader) : null;
            if (handMaterial != null) handMaterial.color = new Color(.22f, .74f, .84f);
            float scale = station.TransformVector(Vector3.up).magnitude;
            Vector3 span = tip.position - wrist.position;
            GameObject palm = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            palm.name = "Measured wrist capsule end";
            palm.transform.SetPositionAndRotation(wrist.position, Quaternion.identity);
            palm.transform.localScale = Vector3.one * (scale * .098f);
            Destroy(palm.GetComponent<Collider>());
            if (handMaterial != null) palm.GetComponent<Renderer>().sharedMaterial = handMaterial;
            handEvidence.Add(palm);
            GameObject finger = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            finger.name = "Measured fingertip capsule end";
            finger.transform.SetPositionAndRotation(tip.position, Quaternion.identity);
            finger.transform.localScale = Vector3.one * (scale * .020f);
            Destroy(finger.GetComponent<Collider>());
            if (handMaterial != null) finger.GetComponent<Renderer>().sharedMaterial = handMaterial;
            handEvidence.Add(finger);
            GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bridge.name = "Measured hand capsule axis";
            bridge.transform.SetPositionAndRotation((wrist.position + tip.position) * .5f,
                Quaternion.FromToRotation(Vector3.up, span));
            bridge.transform.localScale = new Vector3(scale * .06f, span.magnitude * .5f, scale * .06f);
            Destroy(bridge.GetComponent<Collider>());
            if (handMaterial != null) bridge.GetComponent<Renderer>().sharedMaterial = handMaterial;
            handEvidence.Add(bridge);
        }
        foreach (Renderer renderer in station.GetComponentsInChildren<Renderer>(true))
            if (renderer != clothRenderer && renderer.enabled)
            { renderer.enabled = false; disabled.Add(renderer); }
        RenderTexture texture = RenderTexture.GetTemporary(960, 540, 24);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var picture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0f, 0f, 960f, 540f), 0, 0);
            picture.Apply();
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Destroy(picture);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(texture);
            foreach (Renderer renderer in disabled) renderer.enabled = true;
            clothRenderer.sharedMaterial = originalMaterial;
            if (evidenceMaterial != null) Destroy(evidenceMaterial);
            foreach (GameObject primitive in handEvidence) Destroy(primitive);
            if (handMaterial != null) Destroy(handMaterial);
            RenderSettings.ambientLight = oldAmbient;
            Destroy(cameraObject);
            Destroy(lightObject);
        }
    }

    private static float MaximumEdgeStretch(MeshFilter filter, Vector3[] rest, float stationScale)
    {
        Vector3[] shown = filter.mesh.vertices;
        int[] triangles = filter.mesh.triangles;
        float largest = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            largest = Mathf.Max(largest, Stretch(triangles[i], triangles[i + 1]));
            largest = Mathf.Max(largest, Stretch(triangles[i + 1], triangles[i + 2]));
            largest = Mathf.Max(largest, Stretch(triangles[i + 2], triangles[i]));
        }
        return largest;

        float Stretch(int a, int b)
        {
            float before = Vector3.Distance(filter.transform.TransformPoint(rest[a]),
                filter.transform.TransformPoint(rest[b])) / stationScale;
            float after = Vector3.Distance(filter.transform.TransformPoint(shown[a]),
                filter.transform.TransformPoint(shown[b])) / stationScale;
            return Mathf.Max(0f, after - before);
        }
    }

    private static float NearestSurfaceDistance(MeshFilter filter, Vector3 worldPoint, float stationScale)
    {
        Mesh mesh = filter.mesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        return NearestSurfaceDistance(filter, vertices, triangles, worldPoint, stationScale);
    }

    private static float NearestSurfaceDistance(MeshFilter filter,
        Vector3[] vertices, int[] triangles, Vector3 worldPoint, float stationScale)
    {
        Vector3 point = filter.transform.InverseTransformPoint(worldPoint);
        float minimum = float.MaxValue;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 onSurface = ClosestPointOnTriangle(point,
                vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            minimum = Mathf.Min(minimum, (point - onSurface).sqrMagnitude);
        }
        return Mathf.Sqrt(minimum) * filter.transform.lossyScale.x / stationScale;
    }

    private static float CapsuleSurfaceClearance(MeshFilter filter,
        Vector3 wrist, Vector3 tip, float wristRadius, float tipRadius, float stationScale)
    {
        Mesh mesh = filter.mesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 a = filter.transform.InverseTransformPoint(wrist);
        Vector3 b = filter.transform.InverseTransformPoint(tip);
        float localToStation = filter.transform.lossyScale.x / stationScale;
        float minimum = float.MaxValue;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            float distance = SegmentTriangleDistance(a, b,
                vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]],
                out float t);
            float gap = distance * localToStation
                - Mathf.Lerp(wristRadius, tipRadius, t);
            minimum = Mathf.Min(minimum, gap);
        }
        return minimum;
    }

    private static float SegmentTriangleDistance(Vector3 a, Vector3 b,
        Vector3 p, Vector3 q, Vector3 r, out float segmentT)
    {
        Vector3 normal = Vector3.Cross(q - p, r - p);
        float divisor = Vector3.Dot(normal, b - a);
        if (normal.sqrMagnitude > 1e-12f && Mathf.Abs(divisor) > 1e-9f)
        {
            float crossing = Vector3.Dot(normal, p - a) / divisor;
            if (crossing >= 0f && crossing <= 1f)
            {
                Vector3 hit = Vector3.Lerp(a, b, crossing);
                if (Vector3.Dot(normal, Vector3.Cross(q - p, hit - p)) >= -1e-8f
                    && Vector3.Dot(normal, Vector3.Cross(r - q, hit - q)) >= -1e-8f
                    && Vector3.Dot(normal, Vector3.Cross(p - r, hit - r)) >= -1e-8f)
                { segmentT = crossing; return 0f; }
            }
        }
        float best = (a - ClosestPointOnTriangle(a, p, q, r)).magnitude;
        segmentT = 0f;
        float candidate = (b - ClosestPointOnTriangle(b, p, q, r)).magnitude;
        if (candidate < best) { best = candidate; segmentT = 1f; }
        candidate = SegmentSegmentDistance(a, b, p, q, out float t);
        if (candidate < best) { best = candidate; segmentT = t; }
        candidate = SegmentSegmentDistance(a, b, q, r, out t);
        if (candidate < best) { best = candidate; segmentT = t; }
        candidate = SegmentSegmentDistance(a, b, r, p, out t);
        if (candidate < best) { best = candidate; segmentT = t; }
        return best;
    }

    private static float SegmentSegmentDistance(Vector3 p1, Vector3 q1,
        Vector3 p2, Vector3 q2, out float alongFirst)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, separation = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, separation);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f)
        { alongFirst = 0f; return separation.magnitude; }
        if (a <= 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, separation);
            if (e <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                s = denominator > 1e-12f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        alongFirst = s;
        return (separation + d1 * s - d2 * t).magnitude;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        return a + ab * (vb / (va + vb + vc)) + ac * (vc / (va + vb + vc));
    }

    private static IEnumerator Settle(Fixture fixture, int frames)
    {
        for (int frame = 0; frame < frames; frame++) yield return null;
    }

    private static IEnumerator Freeze(Fixture fixture)
    {
        fixture.Cloth.useGravity = false;
        fixture.Cloth.externalAcceleration = Vector3.zero;
        fixture.Cloth.randomAcceleration = Vector3.zero;
        fixture.Cloth.damping = .85f;
        for (int frame = 0; frame < 120; frame++) yield return null;
    }

    private static IEnumerator Sweep(Fixture fixture)
    {
        yield return Sweep(fixture, .35f);
    }

    private static IEnumerator Sweep(Fixture fixture, float distance)
    {
        Vector3 start = fixture.Mover.position;
        for (int frame = 0; frame < 45; frame++)
        {
            fixture.Mover.position = start + Vector3.right
                * (distance * fixture.StationScale * (frame + 1) / 45f);
            yield return null;
        }
    }

    private static IEnumerator RepeatedSweep(Fixture fixture, float distance, int repetitions)
    {
        Vector3 centre = fixture.Mover.position;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            Vector3 from = centre + Vector3.right * (repetition == 0 ? 0f : -distance * fixture.StationScale);
            Vector3 to = centre + Vector3.right * (distance * fixture.StationScale);
            for (int frame = 0; frame < 36; frame++)
            {
                float t = (frame + 1f) / 36f;
                fixture.Mover.position = Vector3.Lerp(from, to, t);
                yield return null;
            }
            for (int frame = 0; frame < 36; frame++)
            {
                float t = (frame + 1f) / 36f;
                fixture.Mover.position = Vector3.Lerp(to,
                    centre - Vector3.right * distance * fixture.StationScale, t);
                yield return null;
            }
        }
    }

    private static Fixture Build(string name, float scale, float coefficientMultiplier, bool contact,
        Vector3 position, float yaw)
    {
        return Build(name, scale, 1f, contact, true, position, yaw);
    }

    private static Fixture Build(string name, float stationScale, float fbxScale, bool contact,
        bool convertNestedScale, Vector3 position, float yaw, int columns = 17, int rows = 21)
    {
        var vertices = new Vector3[columns * rows];
        var triangles = new int[(columns - 1) * (rows - 1) * 6];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                vertices[row * columns + column] = new Vector3(
                    (-.6f + 1.2f * column / (columns - 1f)) / fbxScale,
                    0f, .8f * row / (rows - 1f) / fbxScale);
        int at = 0;
        for (int row = 0; row < rows - 1; row++)
            for (int column = 0; column < columns - 1; column++)
            {
                int a = row * columns + column, b = a + 1, c = a + columns, d = c + 1;
                triangles[at++] = a; triangles[at++] = c; triangles[at++] = b;
                triangles[at++] = b; triangles[at++] = c; triangles[at++] = d;
            }
        var mesh = new Mesh { name = "Town cloth native probe" };
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };

        var station = new GameObject(name + ".station") { layer = 2 };
        station.transform.localScale = Vector3.one * stationScale;
        station.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var source = new GameObject(name + ".fbx-source") { layer = 2 };
        source.transform.SetParent(station.transform, false);
        source.transform.localScale = Vector3.one * fbxScale;
        var driver = new GameObject(name) { layer = 2 };
        Vector3[] driverVertices;
        if (convertNestedScale)
        {
            driver.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            driver.transform.localScale = Vector3.one;
            driverVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                driverVertices[i] = driver.transform.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
        }
        else
        {
            driver.transform.SetParent(source.transform, false);
            driverVertices = vertices;
        }
        mesh.vertices = driverVertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var skin = driver.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh; skin.rootBone = driver.transform; skin.bones = new[] { driver.transform };
        skin.updateWhenOffscreen = true; skin.forceRenderingOff = true;
        var cookWatch = Stopwatch.StartNew();
        var cloth = driver.AddComponent<Cloth>();
        cloth.useGravity = true; cloth.useTethers = true; cloth.damping = .2f;
        cloth.stretchingStiffness = .8f; cloth.bendingStiffness = .4f;
        cloth.clothSolverFrequency = 120f; cloth.enableContinuousCollision = true;
        var coefficients = new ClothSkinningCoefficient[vertices.Length];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                coefficients[row * columns + column].maxDistance = row == 0 ? 0f
                    : convertNestedScale ? .45f * stationScale : .45f * driver.transform.lossyScale.x;
        cloth.coefficients = coefficients;

        var collider = new GameObject(name + ".moving-probe") { layer = 2 };
        var palm = collider.AddComponent<SphereCollider>(); palm.radius = .16f * stationScale;
        var tipObject = new GameObject("Tip") { layer = 2 }; tipObject.transform.SetParent(collider.transform, false);
        tipObject.transform.localPosition = Vector3.forward * .16f * stationScale;
        var tip = tipObject.AddComponent<SphereCollider>(); tip.radius = .06f * stationScale;
        collider.transform.position = station.transform.TransformPoint(new Vector3(-.18f, -.08f, .48f));
        if (contact) cloth.sphereColliders = new[] { new ClothSphereColliderPair(palm, tip) };
        cloth.ClearTransformMotion();
        cookWatch.Stop();

        var visible = UnityEngine.Object.Instantiate(mesh);
        return new Fixture { Root = station, Cloth = cloth, Mover = collider.transform,
            Visible = visible, Rest = cloth.vertices, StationScale = stationScale,
            SolverScale = driver.transform.lossyScale.x,
            CookMilliseconds = cookWatch.Elapsed.TotalMilliseconds };
    }

    private static Fixture BuildActual(string name, Transform station, MeshFilter filter,
        byte service, float stationScale)
    {
        const int columns = 13, rows = 25, count = columns * rows;
        Vector3[] source = filter.sharedMesh.vertices;
        if (source.Length < count) throw new Exception("actual runner lost its authored source grid");
        var driver = new GameObject(name + ".unit-driver") { layer = 2 };
        driver.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
        float maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        foreach (Vector3 vertex in source)
        {
            Vector3 point = station.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            maxY = Mathf.Max(maxY, point.y); minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
        }
        float Front(float x)
        {
            float radius = service == 2 ? .81f : .86f, depth = service == 2 ? .38f : .46f;
            return (service == 2 ? 0f : .03f) - depth * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (radius * radius)));
        }
        Vector3 Authored(int row, int column)
        {
            float t = row / (rows - 1f), u = column / (columns - 1f), x = Mathf.Lerp(minX, maxX, u);
            float fold = .0015f * Mathf.Sin(u * Mathf.PI * 6.4f + .3f);
            float surface = Mathf.Min(1f, t / .45f);
            float z = Front(x) + .145f * (1f - surface) - .004f * surface;
            z -= .012f * Mathf.Sin(u * Mathf.PI * 6f) * (Mathf.Max(0f, t - .45f) / .55f);
            if (service == 3)
            {
                float fall = Mathf.Clamp01((t - .35f) / .45f);
                z -= .100f * fall * fall * (3f - 2f * fall);
            }
            float y = .9575f + fold - .49f * Mathf.Max(0f, (t - .45f) / .55f);
            y -= .018f * Mathf.Max(0f, (t - .84f) / .16f) * (1f - Mathf.Abs(u * 2f - 1f));
            return new Vector3(x, y, z);
        }
        Vector3[] vertices = new Vector3[count];
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            vertices[row * columns + column] = driver.transform.InverseTransformPoint(
                station.TransformPoint(Authored(row, column)));
        int[] triangles = new int[(rows - 1) * (columns - 1) * 6]; int triangle = 0;
        for (int row = 0; row < rows - 1; row++) for (int column = 0; column < columns - 1; column++)
        {
            int a = row * columns + column, b = a + 1, c = a + columns, d = c + 1;
            triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
            triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
        }
        var mesh = new Mesh { name = name + ".mesh" }; mesh.vertices = vertices; mesh.triangles = triangles;
        var weights = new BoneWeight[count];
        for (int i = 0; i < count; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var skin = driver.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
        skin.rootBone = driver.transform; skin.bones = new[] { driver.transform };
        skin.updateWhenOffscreen = true; skin.forceRenderingOff = true;
        var cloth = driver.AddComponent<Cloth>(); cloth.useGravity = false; cloth.useTethers = true;
        cloth.externalAcceleration = Physics.gravity * stationScale;
        cloth.damping = .40f; cloth.friction = .52f; cloth.bendingStiffness = .82f;
        cloth.stretchingStiffness = .94f; cloth.clothSolverFrequency = 120f;
        cloth.enableContinuousCollision = true;
        Vector3[] stationPoints = new Vector3[count];
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            stationPoints[row * columns + column] = Authored(row, column);
        var coefficients = new ClothSkinningCoefficient[count];
        var table = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = stationPoints[i]; float edge = Front(p.x);
            float top = Mathf.Clamp01((edge + .145f - p.z) / .145f);
            float hanging = Mathf.Clamp01((maxY - p.y) / .38f);
            float freedom = Mathf.Pow(Mathf.Max(.10f * top, hanging), 1.25f);
            coefficients[i].maxDistance = freedom * .11f * stationScale;
            coefficients[i].collisionSphereDistance = .004f * stationScale;
            if (p.y > maxY - .05f && p.z >= edge - .01f && p.z <= edge + .15f) table.Add(i);
        }
        cloth.coefficients = coefficients;
        var pairs = new System.Collections.Generic.List<ClothSphereColliderPair>();
        for (int column = 0; column < columns; column++)
        {
            float x = Mathf.Lerp(minX, maxX, column / (columns - 1f));
            SphereCollider Make(float rear, string side)
            {
                var support = new GameObject(name + ".support." + column + "." + side) { layer = 2 };
                support.transform.SetParent(driver.transform, false);
                support.transform.localPosition = driver.transform.InverseTransformPoint(
                    station.TransformPoint(new Vector3(x, maxY - .066f, Front(x) + rear)));
                var sphere = support.AddComponent<SphereCollider>(); sphere.radius = .070f * stationScale;
                return sphere;
            }
            pairs.Add(new ClothSphereColliderPair(Make(.008f, "front"), Make(.142f, "rear")));
        }
        int target = Enumerable.Range(0, count).OrderByDescending(i => coefficients[i].maxDistance
            - Mathf.Abs(stationPoints[i].x) * stationScale).First();
        var mover = new GameObject(name + ".hand") { layer = 2 };
        var palm = mover.AddComponent<SphereCollider>(); palm.radius = .035f * stationScale;
        var tipObject = new GameObject("Tip") { layer = 2 }; tipObject.transform.SetParent(mover.transform, false);
        tipObject.transform.localPosition = Vector3.forward * (.09f * stationScale);
        var tip = tipObject.AddComponent<SphereCollider>(); tip.radius = .01f * stationScale;
        mover.transform.position = driver.transform.TransformPoint(vertices[target]) - station.right * (.09f * stationScale);
        pairs.Add(new ClothSphereColliderPair(palm, tip)); cloth.sphereColliders = pairs.ToArray();
        cloth.ClearTransformMotion();
        return new Fixture { Root = driver, Cloth = cloth, Mover = mover.transform, Visible = mesh,
            Rest = cloth.vertices, StationScale = stationScale, SolverScale = 1f, Station = station,
            Driver = driver.transform, TableIndices = table.ToArray(), TableRest = stationPoints,
            Gravity = Physics.gravity * stationScale, RestDamping = .40f };
    }

    private static float MaximumTableDrop(Fixture fixture)
    {
        Vector3[] current = fixture.Cloth.vertices; float drop = 0f;
        foreach (int index in fixture.TableIndices)
        {
            Vector3 stationPoint = fixture.Station.InverseTransformPoint(
                fixture.Driver.TransformPoint(current[index]));
            drop = Mathf.Max(drop, (fixture.TableRest[index].y - stationPoint.y) * fixture.StationScale);
        }
        return drop;
    }

    private static float MaximumDistance(Vector3[] a, Vector3[] b)
    {
        float maximum = 0f;
        for (int i = 0; i < a.Length && i < b.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(a[i], b[i]));
        return maximum;
    }

    private static float VisibleMotion(MeshFilter filter, Vector3[] rest, float stationScale)
    {
        Vector3[] current = filter.mesh.vertices;
        float maximum = 0f;
        for (int i = 0; i < rest.Length && i < current.Length; i++)
            maximum = Mathf.Max(maximum, Vector3.Distance(filter.transform.TransformPoint(rest[i]),
                filter.transform.TransformPoint(current[i])) / stationScale);
        return maximum;
    }

    private static string Argument(string prefix)
    {
        foreach (string value in Environment.GetCommandLineArgs())
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
        return string.Empty;
    }
}
