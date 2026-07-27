// A hand-auditable spelling for expected packets, plus the assertion harness.
//
// The expected bytes are written as hex strings with `//` comments per line, so a reader can
// check them against `.planning/refactor/INVARIANTS-Net-Rig.md` Part I §3a-3d / §4a-4e without
// running anything. They are DERIVED FROM THE SPEC, never from the writer — a golden vector
// produced by calling the code it tests proves nothing.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GloomhavenVR.WireTests;

internal static class Hex
{
    /// <summary>Parse "31 52 56 47 // magic" style text into bytes. Whitespace and `//`
    /// comments are ignored, so a vector can be laid out one field per line.</summary>
    public static byte[] Bytes(string spec)
    {
        var outBytes = new List<byte>();
        foreach (string rawLine in spec.Split('\n'))
        {
            string line = rawLine;
            int c = line.IndexOf("//", StringComparison.Ordinal);
            if (c >= 0) line = line.Substring(0, c);
            foreach (string tok in line.Split(new[] { ' ', '\t', '\r', '|' },
                                              StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length % 2 != 0)
                    throw new FormatException($"odd-length hex token '{tok}'");
                for (int k = 0; k < tok.Length; k += 2)
                    outBytes.Add(byte.Parse(tok.Substring(k, 2), NumberStyles.HexNumber));
            }
        }
        return outBytes.ToArray();
    }

    public static string Show(byte[] b, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len; i++)
        {
            if (i > 0 && i % 16 == 0) sb.Append("\n            ");
            else if (i > 0) sb.Append(' ');
            sb.Append(b[i].ToString("X2"));
        }
        return sb.ToString();
    }
}

internal sealed class Harness
{
    private int _passed;
    private readonly List<string> _failures = new();
    private string _case = "?";

    public void Case(string name) => _case = name;

    public void True(bool cond, string what)
    {
        if (cond) { _passed++; return; }
        _failures.Add($"{_case}: {what}");
    }

    public void Equal<T>(T expected, T actual, string what) where T : IEquatable<T>
    {
        if (expected.Equals(actual)) { _passed++; return; }
        _failures.Add($"{_case}: {what}\n      expected: {expected}\n      actual  : {actual}");
    }

    /// <summary>The core assertion: the writer emitted EXACTLY these bytes.</summary>
    public void Wire(byte[] expected, byte[] buffer, int written, string what)
    {
        if (written == expected.Length)
        {
            bool same = true;
            for (int i = 0; i < written; i++)
                if (buffer[i] != expected[i]) { same = false; break; }
            if (same) { _passed++; return; }
        }

        var sb = new StringBuilder();
        sb.Append($"{_case}: {what}\n");
        sb.Append($"      expected {expected.Length} bytes:\n            {Hex.Show(expected, expected.Length)}\n");
        sb.Append($"      actual   {written} bytes:\n            {Hex.Show(buffer, written)}\n");
        for (int i = 0; i < Math.Min(written, expected.Length); i++)
            if (buffer[i] != expected[i])
            {
                sb.Append($"      first difference at offset {i}: "
                          + $"expected {expected[i]:X2}, got {buffer[i]:X2}");
                break;
            }
        _failures.Add(sb.ToString());
    }

    public int Report()
    {
        foreach (string f in _failures)
            Console.Error.WriteLine("FAIL  " + f);
        Console.WriteLine(_failures.Count == 0
            ? $"wire tests: {_passed} assertions passed"
            : $"wire tests: {_passed} passed, {_failures.Count} FAILED");
        return _failures.Count == 0 ? 0 : 1;
    }
}
