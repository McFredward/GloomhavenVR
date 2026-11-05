using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WindowPlaneNormalProgram
{
    private static int _checks;
    private static void Check(bool okay, string reason)
    {
        _checks++;
        if (!okay) throw new InvalidOperationException(reason);
    }

    // Independent double-precision geometric reference. It does not call Unity.Normalize or
    // the production normalization helper and retains reflected/sheared plane winding.
    private static Vector3 Reference(Vector3 right, Vector3 up)
    {
        double x = (double)right.y * up.z - (double)right.z * up.y;
        double y = (double)right.z * up.x - (double)right.x * up.z;
        double z = (double)right.x * up.y - (double)right.y * up.x;
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (!(length > 0) || double.IsNaN(length) || double.IsInfinity(length)) return Vector3.zero;
        return new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
    }

    private static void Valid(Transform plane, string label, List<string> rows)
    {
        Vector3 right = plane.TransformVector(Vector3.right), up = plane.TransformVector(Vector3.up);
        Vector3 actual = MenuDepthProjection.PlaneNormal(plane), expected = Reference(right, up);
        Check(MenuDepthProjection.Finite(actual) && Math.Abs(actual.magnitude - 1f) < .00001f,
            "valid physical window retains a finite unit plane normal");
        Check(Vector3.Dot(actual, expected) > .99999f,
            "physical plane winding matches its independent transformed geometric reference");
        Vector3 rightUnit = Reference(up, expected), upUnit = Reference(expected, right);
        Check(Math.Abs(Vector3.Dot(actual, rightUnit)) < .00001f && Math.Abs(Vector3.Dot(actual, upUnit)) < .00001f,
            "physical plane normal is perpendicular to both actual sheared axes");
        Vector3 old = Vector3.Cross(right, up).normalized;
        rows.Add("\"" + label + "\",\"" + actual.ToString("R") + "\",\"" + expected.ToString("R") + "\",\"" + old.ToString("R") + "\"");
    }

    private static void Direction(Vector3 value, bool wanted)
    {
        MethodInfo method = typeof(MenuDepthProjection).GetMethod("TryNormalizeDirection", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Production direction helper binding changed.");
        object[] args = { value, Vector3.zero };
        bool accepted = (bool)method.Invoke(null, args);
        Vector3 normal = (Vector3)args[1];
        Check(accepted == wanted, "normalization accepts only finite nonzero directions");
        Check(MenuDepthProjection.Finite(normal), "normalization never publishes a non-finite direction");
        if (wanted)
            Check(Math.Abs(normal.magnitude - 1f) < .00001f,
                "every accepted finite direction remains unit length without squared-magnitude underflow or overflow");
        else Check(normal == Vector3.zero, "invalid directions retain the explicit zero sentinel");
    }

    public static int Run(string output)
    {
        _checks = 0;
        var rows = new List<string> { "case,actualUnitNormal,independentDoubleReference,historicalRawCrossNormal" };
        var parent = new GameObject("actual parent nonuniform scale760").transform;
        var plane = new GameObject("actual physical pixel basis760").transform;
        plane.SetParent(parent, false);
        try
        {
            // This exact engine cutoff is the source of the current hardware failure.
            Check(new Vector3(0, 0, .00000144f).normalized == Vector3.zero
                && new Vector3(0, 0, .00002f).normalized == Vector3.forward,
                "actual Unity Normalize has the absolute 1e-5 cutoff used by the historical defect");
            foreach (float scale in new[] { .0012f, .026f, .009f, .003f })
            {
                parent.localScale = Vector3.one; parent.rotation = Quaternion.Euler(0, 185.8f, 0);
                plane.localRotation = Quaternion.identity; plane.localScale = Vector3.one * scale;
                Valid(plane, "hardware-" + scale.ToString("R", CultureInfo.InvariantCulture), rows);
            }
            foreach (float scale in new[] { 1e-8f, 1e-7f, 1e-6f, 1e-5f, 1e-4f, .001f, .01f, .1f, 1f, 10f, 100f, 1000f, 1e4f, 1e5f, 1e6f, 1e7f, 1e8f })
            foreach (Vector3 shape in new[] { Vector3.one, new Vector3(-1, 1, 1), new Vector3(1, -1, 1),
                new Vector3(-1, -1, 1), new Vector3(.17f, 3.7f, .8f) })
            {
                parent.SetPositionAndRotation(new Vector3(19.99f, 14.72f, 23.49f), Quaternion.Euler(31, 123, -17));
                parent.localScale = new Vector3(2f, .7f, 1.3f);
                plane.localRotation = Quaternion.Euler(19, -47, 26);
                plane.localScale = shape * scale;
                Valid(plane, "range-" + scale.ToString("R", CultureInfo.InvariantCulture) + "-" + shape.ToString("R"), rows);
            }
            plane.localScale = new Vector3(0, 1, 1);
            Check(MenuDepthProjection.PlaneNormal(plane) == Vector3.zero, "degenerate physical planes keep the zero sentinel");
            plane.localScale = new Vector3(1, 0, 1);
            Check(MenuDepthProjection.PlaneNormal(plane) == Vector3.zero, "zero second plane axis keeps the zero sentinel");
            Check(MenuDepthProjection.PlaneNormal(null!) == Vector3.zero, "absent physical planes keep the zero sentinel");
            Direction(new Vector3(1e-30f, 0, 0), true);
            Direction(new Vector3(float.MaxValue, float.MaxValue, 0), true);
            Direction(new Vector3(-float.MaxValue, 1, float.MaxValue), true);
            Direction(new Vector3(1e-30f, -1e-30f, 1e-30f), true);
            Direction(Vector3.zero, false);
            Direction(new Vector3(float.NaN, 0, 1), false);
            Direction(new Vector3(1, float.PositiveInfinity, 0), false);
            Direction(new Vector3(0, 1, float.NegativeInfinity), false);
        }
        finally { Object.DestroyImmediate(plane.gameObject); Object.DestroyImmediate(parent.gameObject); }
        File.WriteAllLines(Path.Combine(output, "actual-unity-plane-normals760.csv"), rows);
        return _checks;
    }
}
