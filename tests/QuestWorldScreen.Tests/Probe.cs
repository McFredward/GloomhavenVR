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

/// <summary>Actual Android compiler banks and defective capture/eye bindings.</summary>
public static class QuestWorldScreenProbe
{
    [Serializable] public sealed class Result
    {
        public int schema = 1, actualBanks, actualStages, defectControls;
        public bool hardwareVerified;
        public string[] cases;
    }
    static readonly List<string> Cases = new List<string>();
    public static void Run()
    {
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        Directory.CreateDirectory("ProbeOutput");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(QuestWorldScreenValidation.SourcePath);
        if (shader == null) throw new InvalidOperationException("Actual world-screen source is missing.");
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        int banks = 0, defects = 0;
        foreach (string keyword in new[] { "", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON" })
        {
            var compiled = pass.CompileVariant(ShaderType.Vertex,
                keyword.Length == 0 ? new string[0] : new[] { keyword },
                ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
            if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0)
                throw new InvalidOperationException("Actual world-screen GLES fixture failed: " + keyword);
            string label = keyword.Length == 0 ? "mono" : keyword;
            File.WriteAllBytes("ProbeOutput/" + label + ".glsl", compiled.ShaderData);
            string code = new UTF8Encoding(false, true).GetString(compiled.ShaderData);
            QuestWorldScreenValidation.VerifiedBank(compiled.ShaderData, keyword);
            Cases.Add(label + ":actual-compiled-bank");
            banks++;
            Negative(label, keyword, "missing-vertex-stage", Replace(code, @"#ifdef VERTEX", "#ifdef ABSENT"), "incomplete GLES"); defects++;
            Negative(label, keyword, "missing-fragment-stage", Replace(code, @"#ifdef FRAGMENT", "#ifdef ABSENT"), "incomplete GLES"); defects++;
            Negative(label, keyword, "left-capture-array-mismatch", Replace(code,
                @"sampler2D\s+_MainTex\b", "sampler2DArray _MainTex"), "not plain 2D"); defects++;
            Negative(label, keyword, "left-sampler-only-in-comment", Replace(code,
                @"(?m)^.*\bsampler2D\s+_MainTex\b.*$", "// uniform sampler2D _MainTex;"), "not plain 2D"); defects++;
            if (keyword.Length != 0)
            {
                Negative(label, keyword, "right-capture-array-mismatch", Replace(code,
                    @"sampler2D\s+_RightTex\b", "sampler2DArray _RightTex"), "not plain 2D"); defects++;
                Negative(label, keyword, "stereo-routing-switch-lost", code.Replace("_StereoCapture", "_Unrelated"), "switch is missing"); defects++;
                string eye = keyword == "STEREO_INSTANCING_ON" ? "gl_InstanceID" : "gl_ViewID_OVR";
                Negative(label, keyword, "vertex-eye-selection-lost", code.Replace(eye, "unrelatedEye"), "eye selection is missing"); defects++;
            }
        }
        QuestWorldScreenValidation.Validate(false);
        File.WriteAllText("ProbeOutput/results.json", JsonUtility.ToJson(new Result {
            actualBanks = banks, actualStages = banks * 2, defectControls = defects,
            hardwareVerified = false, cases = Cases.ToArray()
        }, true) + "\n");
        Debug.Log("PASS Quest world-screen native shader banks=" + banks + " stages=" + banks * 2 + " defect controls=" + defects);
    }
    static string Replace(string code, string pattern, string replacement)
    {
        var expression = new Regex(pattern);
        if (expression.Matches(code).Count != 1)
            throw new InvalidOperationException("Actual world-screen mutation binding drift: " + pattern);
        return expression.Replace(code, replacement);
    }
    static void Negative(string label, string keyword, string name, string code, string expected)
    {
        try { QuestWorldScreenValidation.VerifiedBank(Encoding.UTF8.GetBytes(code), keyword); }
        catch (InvalidOperationException error)
        {
            if (!error.Message.Contains(expected))
                throw new InvalidOperationException("Wrong world-screen defect boundary " + name + ": " + error.Message);
            Cases.Add(label + ":" + name);
            return;
        }
        throw new InvalidOperationException("Production world-screen verifier accepted defect " + name);
    }
}
