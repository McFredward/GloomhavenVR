using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Actual Unity GLES compiler output exercised through the production verifier.</summary>
public static class QuestUiShaderProbe
{
    [Serializable] public sealed class Case
    {
        public string shader, keyword, name, expectedFailure;
        public bool passed;
    }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1, actualCompiledBanks, actualCompiledStages, negativeControls;
        public string unityVersion;
        public bool originalPixelParityVerified;
        public Case[] cases;
    }
    private static readonly List<Case> Cases = new List<Case>();

    public static void Run()
    {
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        Directory.CreateDirectory("ProbeOutput");
        int banks = 0, negative = 0;
        foreach (string guid in new[] { "36fec7f4d3bfafd409fd42ffef9eec70", "ee924f72fbf9fcd4a9ea6e1dceb11982" })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            if (shader == null) throw new InvalidOperationException("Actual UI fixture shader is missing.");
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            foreach (string keyword in shader.name == "Splash Screen Shader" ? new[] { "", "_ADDORMULT_ON" } : new[] { "" })
            {
                var compiled = pass.CompileVariant(ShaderType.Vertex, keyword.Length == 0 ? new string[0] : new[] { keyword },
                    ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0)
                    throw new InvalidOperationException("Actual UI fixture GLES compilation failed.");
                string label = guid + (keyword.Length == 0 ? "-default" : "-add");
                File.WriteAllBytes("ProbeOutput/" + label + ".glsl", compiled.ShaderData);
                string code = new UTF8Encoding(false, true).GetString(compiled.ShaderData);
                Positive(shader.name, keyword, "actual-generated-bank", code);
                Positive(shader.name, keyword, "alternate-semantic-case", Regex.Replace(code, @"\bSV_TARGET0\b", "SV_Target0"));
                banks++;
                Negative(shader.name, keyword, "missing-vertex-section", Replace(code, @"#ifdef VERTEX", "#ifdef OTHER", 1), "incomplete GLES");
                Negative(shader.name, keyword, "missing-fragment-section", Replace(code, @"#ifdef FRAGMENT", "#ifdef OTHER", 1), "incomplete GLES");
                Negative(shader.name, keyword, "wrong-glsl-version", Replace(code, @"#version 300 es", "#version 100", 2), "GLSL300");
                Negative(shader.name, keyword, "missing-vertex-main", Replace(code, @"void main\(\)", "void other()", 2), "main entry");
                int fragmentBegin = code.IndexOf("#ifdef FRAGMENT\n", StringComparison.Ordinal);
                string vertex = code.Substring(0, fragmentBegin), fragment = code.Substring(fragmentBegin);
                Negative(shader.name, keyword, "missing-fragment-main", vertex + Replace(fragment, @"void main\(\)", "void other()", 1), "main entry");
                Negative(shader.name, keyword, "vertex-output-only-in-comment", Replace(code, @"(?m)^\s*gl_Position\s*=[^\n]*", "// gl_Position = vec4(0);", 1), "vertex position output");
                Negative(shader.name, keyword, "missing-output-declaration", Replace(code,
                    @"(?m)^\s*layout\(location = 0\) out highp vec4 SV_TARGET0;", "", 1), "output at location 0");
                Negative(shader.name, keyword, "wrong-output-location", Replace(code, @"layout\(location = 0\)", "layout(location = 1)", 1), "output at location 0");
                Negative(shader.name, keyword, "wrong-output-type", Replace(code, @"out highp vec4 SV_TARGET0;", "out highp vec3 SV_TARGET0;", 1), "output at location 0");
                Negative(shader.name, keyword, "duplicate-output-location", Replace(code, @"out highp vec4 SV_TARGET0;",
                    "out highp vec4 SV_TARGET0;\nlayout(location = 0) out highp vec4 Duplicate;", 1), "output at location 0");
                Negative(shader.name, keyword, "unwritten-rgb", Replace(code, @"SV_TARGET0\.xyz\s*=", "Unrelated.xyz =", 1), "color output");
                Negative(shader.name, keyword, "unwritten-alpha", Replace(code, @"SV_TARGET0\.w\s*=", "Unrelated.w =", 1), "color output");
                Negative(shader.name, keyword, "alpha-only-in-comment", Replace(code,
                    @"(?m)^\s*SV_TARGET0\.w\s*=[^\n]*", "// SV_TARGET0.w = 1.0;", 1), "color output");
                negative += 13;
            }
        }
        // Also exercise the exact full source receipt/GUID/property/compiler gate.
        QuestUiAssetValidation.Validate(false);
        var receipt = new Receipt {
            unityVersion = Application.unityVersion, actualCompiledBanks = banks, actualCompiledStages = banks * 2,
            negativeControls = negative, originalPixelParityVerified = false, cases = Cases.ToArray()
        };
        File.WriteAllText("ProbeOutput/results.json", JsonUtility.ToJson(receipt, true) + "\n");
        Debug.Log("PASS Quest UI shaders: actual banks=" + banks + ", stages=" + (banks * 2) + ", negative controls=" + negative);
    }

    private static string Replace(string source, string pattern, string replacement, int matches)
    {
        var regex = new Regex(pattern);
        if (regex.Matches(source).Count != matches)
            throw new InvalidOperationException("Actual GLES mutation binding drift: " + pattern);
        return regex.Replace(source, replacement, 1);
    }
    private static void Positive(string shader, string keyword, string name, string code)
    {
        QuestUiAssetValidation.VerifiedGlesSections(Encoding.UTF8.GetBytes(code));
        Cases.Add(new Case { shader = shader, keyword = keyword, name = name, passed = true });
    }
    private static void Negative(string shader, string keyword, string name, string code, string expected)
    {
        try { QuestUiAssetValidation.VerifiedGlesSections(Encoding.UTF8.GetBytes(code)); }
        catch (InvalidOperationException error)
        {
            if (error.Message.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Wrong negative boundary: " + name + " " + error.Message);
            Cases.Add(new Case { shader = shader, keyword = keyword, name = name, expectedFailure = expected, passed = true });
            return;
        }
        throw new InvalidOperationException("Production GLES verifier accepted: " + name);
    }
}
