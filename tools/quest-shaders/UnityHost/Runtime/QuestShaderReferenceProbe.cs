using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Compare recovered candidate assets with original Windows/D3D11 assets through
/// the same actual Unity renderer. No native game scripts or gameplay run here.
/// </summary>
public sealed class QuestShaderReferenceProbe : MonoBehaviour
{
    [Serializable] public sealed class OriginalAsset
    {
        public string assetName, rootType, componentType;
        public int[] childIndices;
        public int componentIndex, materialIndex;
        public string[] bundlePaths;
    }
    [Serializable] public sealed class RenderCase
    {
        public string id, shaderGuid, materialGuid, meshGuid, textureProperty, alphaProperty, animationProperty, animationMode;
        public string fixtureGeometry;
        public string[] keywords, features;
        public FloatProperty[] floats;
        public VectorProperty[] vectors;
        public OriginalAsset originalMaterial, originalMesh;
        public float alphaValue = 0.25f, animationValue = 0.35f;
    }
    [Serializable] public sealed class FloatProperty { public string name; public float value; }
    [Serializable] public sealed class VectorProperty { public string name; public Vector4 value; }
    [Serializable] public sealed class Configuration
    {
        public int schema, width = 256, height = 256, allowedByteError = 3;
        public string sourceManifestSha256, outputRoot, candidateBundlePath, colorSpace;
        public bool trialProbe;
        public RenderCase[] cases;
    }
    [Serializable] public sealed class Picture
    {
        public string caseId, feature, referenceFile, candidateFile;
        public int maximumByteError, differingPixels, referenceForegroundPixels, observedInputChangePixels;
        public bool passed;
    }
    [Serializable] public sealed class NegativeControl
    {
        public string caseId, defect;
        public bool rejected;
    }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1;
        public string sourceManifestSha256, unityVersion, graphicsDeviceType, graphicsDeviceName;
        public bool originalWindowsDxbcPixelsCompared, allCasesPassed, androidMultiviewPixelsVerified, headsetPictureVerified;
        public Picture[] pictures;
        public NegativeControl[] negativeControls;
        public string[] errors;
    }

    private Configuration _configuration;
    private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
    private readonly List<Picture> _pictures = new List<Picture>();
    private readonly List<NegativeControl> _negativeControls = new List<NegativeControl>();
    private readonly List<string> _errors = new List<string>();
    private Camera _camera;
    private Light _light;
    private GameObject _subject;
    private Renderer _renderer;
    private Color32[] _clear;
    private Color32[] _baseline;
    private Color32[] _featureReference;
    private static readonly Color Background = new Color(0.0627451f, 0.1254902f, 0.1882353f, 1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!Environment.GetCommandLineArgs().Contains("--quest-shader-config")) return;
        DontDestroyOnLoad(new GameObject("QuestOriginalShaderReferenceProbe").AddComponent<QuestShaderReferenceProbe>().gameObject);
    }

    private void Awake()
    {
        Application.logMessageReceived += Log;
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--quest-shader-config");
        if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("Shader probe configuration argument is missing.");
        _configuration = JsonUtility.FromJson<Configuration>(File.ReadAllText(args[index + 1]));
        if (_configuration == null || _configuration.schema != 1 || _configuration.cases == null || _configuration.cases.Length == 0 ||
            _configuration.width < 64 || _configuration.width > 1024 || _configuration.height < 64 || _configuration.height > 1024 ||
            _configuration.allowedByteError < 0 || _configuration.allowedByteError > 4)
            throw new InvalidOperationException("Shader reference configuration is invalid.");
        Directory.CreateDirectory(_configuration.outputRoot);
        WriteReceipt(false);
    }

    private IEnumerator Start()
    {
        yield return null;
        try
        {
            if (Application.unityVersion != "2021.3.5f1" || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("Original DXBC comparison requires actual original-version Unity Direct3D11.");
            if (QualitySettings.activeColorSpace.ToString() != _configuration.colorSpace)
                throw new InvalidOperationException("Native shader reference color-space contract differs.");
            var candidates = Bundle(_configuration.candidateBundlePath);
            ConfigureScene();
            foreach (var input in _configuration.cases)
            {
                var originalMaterialAsset = Original<Material>(input.originalMaterial);
                var originalMesh = _configuration.trialProbe && input.fixtureGeometry == "triangle" ? Triangle() : Original<Mesh>(input.originalMesh);
                var candidateMaterialAsset = candidates.LoadAsset<Material>(input.materialGuid);
                var candidateMesh = candidates.LoadAsset<Mesh>(input.meshGuid);
                if (originalMaterialAsset == null || originalMesh == null || candidateMaterialAsset == null || candidateMesh == null)
                    throw new InvalidOperationException("Exact original/candidate render assets failed to load: " + input.id);
                var originalMaterial = new Material(originalMaterialAsset);
                var candidateMaterial = new Material(candidateMaterialAsset);
                foreach (var row in input.floats ?? new FloatProperty[0])
                {
                    RequireProperty(originalMaterial, row.name);
                    RequireProperty(candidateMaterial, row.name);
                    originalMaterial.SetFloat(row.name, row.value);
                    candidateMaterial.SetFloat(row.name, row.value);
                }
                foreach (var row in input.vectors ?? new VectorProperty[0])
                {
                    originalMaterial.SetVector(row.name, row.value);
                    candidateMaterial.SetVector(row.name, row.value);
                }
                if (_configuration.trialProbe)
                {
                    var checker = Checker(1);
                    RequireProperty(originalMaterial, input.textureProperty);
                    RequireProperty(candidateMaterial, input.textureProperty);
                    originalMaterial.SetTexture(input.textureProperty, checker);
                    candidateMaterial.SetTexture(input.textureProperty, checker);
                }
                RejectErrorShader(originalMaterial);
                RejectErrorShader(candidateMaterial);
                if (originalMaterial.shader.name != candidateMaterial.shader.name)
                    throw new InvalidOperationException("Recovered candidate material changes its original shader identity: " + input.id);
                if (input.keywords != null)
                {
                    originalMaterial.shaderKeywords = input.keywords;
                    candidateMaterial.shaderKeywords = input.keywords;
                }
                ConfigureSubject(originalMesh, input);
                _renderer.enabled = false;
                _clear = Capture(originalMaterial, originalMesh);
                _renderer.enabled = true;
                _baseline = Capture(originalMaterial, originalMesh);
                var baselineCandidate = Capture(candidateMaterial, candidateMesh);
                Compare(input.id, "baseline", _baseline, baselineCandidate, _baseline, true);
                foreach (string feature in input.features)
                    ProbeFeature(input, feature, originalMaterial, candidateMaterial, originalMesh, candidateMesh);
                NegativeShader(input, candidateMaterial);
                if (input.features.Contains("texture")) NegativeTexture(input, originalMaterial, candidateMaterial, originalMesh, candidateMesh);
                DestroyImmediate(_subject);
                DestroyImmediate(originalMaterial);
                DestroyImmediate(candidateMaterial);
                _subject = null;
                if (_errors.Count != 0) throw new InvalidOperationException("Native Unity renderer reported a shader/import failure.");
            }
            WriteReceipt(true);
            Debug.Log("PASS original Campaign shader D3D11 pixels: pictures=" + _pictures.Count + ", negative controls=" + _negativeControls.Count + ". Android/headset pictures are separate evidence.");
            Application.Quit(0);
        }
        catch (Exception error)
        {
            _errors.Add(error.ToString());
            WriteReceipt(false);
            Debug.LogError("Original Campaign shader reference probe failed: " + error);
            Application.Quit(1);
        }
    }

    private void ConfigureScene()
    {
        foreach (var camera in FindObjectsOfType<Camera>()) camera.enabled = false;
        QualitySettings.antiAliasing = 0;
        QualitySettings.shadows = ShadowQuality.Disable;
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.19f, 0.13f, 0.09f, 1);
        _camera = new GameObject("OriginalShaderReferenceCamera").AddComponent<Camera>();
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Background;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.renderingPath = RenderingPath.Forward;
        _camera.orthographic = true;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 1000;
        _light = new GameObject("OriginalShaderReferenceLight").AddComponent<Light>();
        _light.type = LightType.Directional;
        _light.color = new Color(0.83f, 0.74f, 0.61f, 1);
        _light.intensity = 1.1f;
        _light.transform.rotation = Quaternion.Euler(39, -24, 0);
        RenderSettings.sun = _light;
    }

    private void ConfigureSubject(Mesh mesh, RenderCase input)
    {
        if (mesh.vertexCount < 3 || mesh.triangles.Length < 3)
            throw new InvalidOperationException("Original shader fixture does not contain renderable geometry.");
        _subject = new GameObject("ExactOriginalShaderSubject");
        if (input.animationMode == "skin")
        {
            if (mesh.bindposes.Length == 0 || mesh.boneWeights.Length != mesh.vertexCount)
                throw new InvalidOperationException("Skeletal rendering hypothesis lacks original weights/bindposes.");
            var skin = _subject.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[mesh.bindposes.Length];
            for (int index = 0; index < bones.Length; index++)
            {
                var pose = mesh.bindposes[index].inverse;
                var bone = new GameObject("OriginalBindpose" + index).transform;
                bone.SetParent(_subject.transform, false);
                bone.localPosition = pose.GetColumn(3);
                bone.localRotation = Quaternion.LookRotation(pose.GetColumn(2), pose.GetColumn(1));
                bone.localScale = new Vector3(pose.GetColumn(0).magnitude, pose.GetColumn(1).magnitude, pose.GetColumn(2).magnitude);
                bones[index] = bone;
            }
            skin.bones = bones;
            skin.rootBone = _subject.transform;
            skin.updateWhenOffscreen = true;
            _renderer = skin;
        }
        else
        {
            _subject.AddComponent<MeshFilter>();
            _renderer = _subject.AddComponent<MeshRenderer>();
        }
        var bounds = mesh.bounds;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        float extent = Math.Max(0.01f, bounds.extents.magnitude);
        _camera.orthographicSize = extent * 1.2f;
        _camera.transform.position = bounds.center + new Vector3(extent * 0.35f, extent * 0.19f, -extent * 3.1f);
        _camera.transform.LookAt(bounds.center);
    }

    private Color32[] Capture(Material material, Mesh mesh)
    {
        _renderer.sharedMaterial = material;
        var skin = _renderer as SkinnedMeshRenderer;
        if (skin != null) skin.sharedMesh = mesh;
        else _subject.GetComponent<MeshFilter>().sharedMesh = mesh;
        var target = RenderTexture.GetTemporary(_configuration.width, _configuration.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.filterMode = FilterMode.Point;
        var previous = RenderTexture.active;
        var pixels = new Texture2D(_configuration.width, _configuration.height, TextureFormat.RGBA32, false, true);
        try
        {
            _camera.targetTexture = target;
            _camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            pixels.Apply(false, false);
            return pixels.GetPixels32();
        }
        finally
        {
            _camera.targetTexture = null;
            RenderTexture.active = previous;
            DestroyImmediate(pixels);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private void ProbeFeature(RenderCase input, string feature, Material original, Material candidate, Mesh originalMesh, Mesh candidateMesh)
    {
        var left = new Material(original);
        var right = new Material(candidate);
        var leftMesh = Instantiate(originalMesh);
        var rightMesh = Instantiate(candidateMesh);
        Texture2D texture = null;
        var cameraPosition = _camera.transform.position;
        var lightColor = _light.color;
        var lightRotation = _light.transform.rotation;
        Transform animatedBone = null;
        Quaternion boneRotation = Quaternion.identity;
        try
        {
            switch (feature)
            {
                case "geometry":
                    var vertices = leftMesh.vertices;
                    for (int index = 0; index < vertices.Length; index++) vertices[index] += new Vector3(0, leftMesh.bounds.extents.magnitude * 0.12f, 0);
                    leftMesh.vertices = vertices;
                    rightMesh.vertices = vertices;
                    break;
                case "lighting":
                    _light.color = new Color(0.14f, 0.37f, 0.91f, 1);
                    _light.transform.rotation = Quaternion.Euler(-32, 91, 0);
                    break;
                case "texture":
                    RequireProperty(left, input.textureProperty);
                    RequireProperty(right, input.textureProperty);
                    texture = Checker();
                    left.SetTexture(input.textureProperty, texture);
                    right.SetTexture(input.textureProperty, texture);
                    break;
                case "uv":
                    var uv = leftMesh.uv;
                    if (uv.Length != leftMesh.vertexCount) throw new InvalidOperationException("UV hypothesis lacks original mesh coordinates.");
                    for (int index = 0; index < uv.Length; index++) uv[index] = uv[index] * 0.71f + new Vector2(0.173f, 0.297f);
                    leftMesh.uv = uv;
                    rightMesh.uv = uv;
                    break;
                case "alpha":
                    RequireProperty(left, input.alphaProperty);
                    RequireProperty(right, input.alphaProperty);
                    left.SetFloat(input.alphaProperty, input.alphaValue);
                    right.SetFloat(input.alphaProperty, input.alphaValue);
                    break;
                case "animation":
                    if (input.animationMode == "skin")
                    {
                        animatedBone = ((SkinnedMeshRenderer)_renderer).bones[0];
                        boneRotation = animatedBone.localRotation;
                        animatedBone.localRotation *= Quaternion.Euler(0, 0, 27);
                    }
                    else
                    {
                        RequireProperty(left, input.animationProperty);
                        RequireProperty(right, input.animationProperty);
                        left.SetFloat(input.animationProperty, input.animationValue);
                        right.SetFloat(input.animationProperty, input.animationValue);
                    }
                    break;
                case "stereo":
                    _camera.transform.position += _camera.transform.right * leftMesh.bounds.extents.magnitude * 0.13f;
                    break;
                default: throw new InvalidOperationException("Unknown rendering hypothesis: " + feature);
            }
            _featureReference = Capture(left, leftMesh);
            Compare(input.id, feature, _featureReference, Capture(right, rightMesh), _baseline, true);
        }
        finally
        {
            _camera.transform.position = cameraPosition;
            _light.color = lightColor;
            _light.transform.rotation = lightRotation;
            if (animatedBone != null) animatedBone.localRotation = boneRotation;
            DestroyImmediate(left);
            DestroyImmediate(right);
            DestroyImmediate(leftMesh);
            DestroyImmediate(rightMesh);
            if (texture != null) DestroyImmediate(texture);
        }
    }

    private void Compare(string id, string feature, Color32[] reference, Color32[] candidate, Color32[] baseline, bool required)
    {
        int maximum = 0, differing = 0, foreground = 0, changed = 0;
        for (int index = 0; index < reference.Length; index++)
        {
            int error = Difference(reference[index], candidate[index]);
            maximum = Math.Max(maximum, error);
            if (error > _configuration.allowedByteError) differing++;
            if (Difference(reference[index], _clear[index]) > 4) foreground++;
            if (Difference(reference[index], baseline[index]) > 4) changed++;
        }
        bool passed = differing == 0 && (feature == "baseline" ? foreground >= 16 : changed >= 4);
        var row = new Picture {
            caseId = id, feature = feature, maximumByteError = maximum, differingPixels = differing,
            referenceForegroundPixels = foreground, observedInputChangePixels = changed,
            referenceFile = id + "-" + feature + "-original.rgba", candidateFile = id + "-" + feature + "-candidate.rgba", passed = passed
        };
        File.WriteAllBytes(Path.Combine(_configuration.outputRoot, row.referenceFile), Bytes(reference));
        File.WriteAllBytes(Path.Combine(_configuration.outputRoot, row.candidateFile), Bytes(candidate));
        _pictures.Add(row);
        if (required && !passed)
            throw new InvalidOperationException("Original native picture hypothesis failed: " + id + " / " + feature + ", max error=" + maximum + ", changed=" + changed + ", foreground=" + foreground);
    }

    private void NegativeShader(RenderCase input, Material material)
    {
        var broken = new Material(material) { shader = Shader.Find("Hidden/InternalErrorShader") };
        bool rejected = false;
        try { RejectErrorShader(broken); }
        catch (InvalidOperationException) { rejected = true; }
        finally { DestroyImmediate(broken); }
        _negativeControls.Add(new NegativeControl { caseId = input.id, defect = "native-error-shader", rejected = rejected });
        if (!rejected) throw new InvalidOperationException("Shader validator accepted a native error shader.");
    }

    private void NegativeTexture(RenderCase input, Material original, Material candidate, Mesh originalMesh, Mesh candidateMesh)
    {
        RequireProperty(candidate, input.textureProperty);
        var broken = new Material(candidate);
        var texture = Checker();
        try
        {
            broken.SetTexture(input.textureProperty, texture);
            var reference = Capture(original, originalMesh);
            var pixels = Capture(broken, candidateMesh);
            int changed = reference.Zip(pixels, (a, b) => Difference(a, b)).Count(value => value > _configuration.allowedByteError);
            _negativeControls.Add(new NegativeControl { caseId = input.id, defect = "one-sided-texture-binding", rejected = changed >= 4 });
            if (changed < 4) throw new InvalidOperationException("Native pixel comparison cannot detect a wrong texture binding.");
        }
        finally { DestroyImmediate(broken); DestroyImmediate(texture); }
    }

    private T Original<T>(OriginalAsset input) where T : UnityEngine.Object
    {
        if (input == null || input.bundlePaths == null || input.bundlePaths.Length == 0 || string.IsNullOrEmpty(input.assetName))
            throw new InvalidOperationException("Original asset has no captured native bundle address.");
        AssetBundle root = null;
        foreach (string path in input.bundlePaths) root = Bundle(path);
        if (!root.Contains(input.assetName))
            throw new InvalidOperationException("Original root bundle does not contain its captured exact address: " + input.assetName);
        T value;
        if (input.rootType == "Shader" && _configuration.trialProbe && typeof(T) == typeof(Material))
        {
            var shader = root.LoadAsset<Shader>(input.assetName);
            if (shader == null) throw new InvalidOperationException("Original DXBC shader trial has no exact native shader address.");
            value = new Material(shader) as T;
        }
        else if (input.rootType == "GameObject")
        {
            var gameObject = root.LoadAsset<GameObject>(input.assetName);
            if (gameObject == null) throw new InvalidOperationException("Original private asset route has no native prefab root.");
            var transform = gameObject.transform;
            foreach (int index in input.childIndices ?? new int[0])
            {
                if (index < 0 || index >= transform.childCount) throw new InvalidOperationException("Original private asset child route differs.");
                transform = transform.GetChild(index);
            }
            Type componentType;
            if (input.componentType == "SkinnedMeshRenderer") componentType = typeof(SkinnedMeshRenderer);
            else if (input.componentType == "MeshRenderer") componentType = typeof(MeshRenderer);
            else if (input.componentType == "MeshFilter") componentType = typeof(MeshFilter);
            else throw new InvalidOperationException("Original private asset route uses an unaudited native component.");
            var components = transform.GetComponents(componentType);
            if (input.componentIndex < 0 || input.componentIndex >= components.Length)
                throw new InvalidOperationException("Original private asset native component ordinal differs.");
            var component = components[input.componentIndex];
            if (typeof(T) == typeof(Material))
            {
                var renderer = component as Renderer;
                if (renderer == null || input.materialIndex < 0 || input.materialIndex >= renderer.sharedMaterials.Length)
                    throw new InvalidOperationException("Original private material slot differs.");
                value = renderer.sharedMaterials[input.materialIndex] as T;
            }
            else if (typeof(T) == typeof(Mesh))
            {
                var skin = component as SkinnedMeshRenderer;
                var filter = component as MeshFilter;
                value = (skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null) as T;
            }
            else throw new InvalidOperationException("Original private asset route cannot change its native resource type.");
        }
        else value = root.LoadAsset<T>(input.assetName);
        if (value == null) throw new InvalidOperationException("Original asset has a different native type: " + input.assetName);
        return value;
    }
    private AssetBundle Bundle(string path)
    {
        AssetBundle bundle;
        if (_bundles.TryGetValue(path, out bundle)) return bundle;
        bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) throw new InvalidOperationException("Actual native Windows asset bundle failed to load: " + path);
        _bundles.Add(path, bundle);
        return bundle;
    }
    private static void RequireProperty(Material material, string name)
    {
        if (string.IsNullOrEmpty(name) || !material.HasProperty(name))
            throw new InvalidOperationException("Native picture hypothesis lacks its exact original material property.");
    }
    private static void RejectErrorShader(Material material)
    {
        if (material == null || material.shader == null || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
            throw new InvalidOperationException("Native shader is missing, unsupported or replaced by the Unity error shader.");
    }
    private static Texture2D Checker(int phase = 0)
    {
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var colors = new Color[64];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) colors[y * 8 + x] = (x + y) % 2 == 0 ?
            (phase == 0 ? new Color(0.91f, 0.07f, 0.13f, 1) : new Color(0.8f, 0.68f, 0.12f, 1)) :
            (phase == 0 ? new Color(0.11f, 0.79f, 0.31f, 0.23f) : new Color(0.17f, 0.24f, 0.75f, 0.23f));
        texture.SetPixels(colors); texture.Apply(false, false); return texture;
    }
    private static Mesh Triangle()
    {
        var mesh = new Mesh {
            vertices = new[] { new Vector3(-0.7f, -0.7f, 0), new Vector3(0, 0.7f, 0), new Vector3(0.7f, -0.7f, 0) },
            normals = new[] { new Vector3(0, 0.6f, -0.8f), new Vector3(0, 0.6f, -0.8f), new Vector3(0, 0.6f, -0.8f) },
            tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
            uv = new[] { Vector2.zero, new Vector2(0.5f, 1), Vector2.right }, triangles = new[] { 0, 1, 2 }
        };
        mesh.RecalculateBounds(); return mesh;
    }
    private static int Difference(Color32 a, Color32 b)
    {
        return Math.Max(Math.Max(Math.Abs(a.r - b.r), Math.Abs(a.g - b.g)), Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a)));
    }
    private static byte[] Bytes(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (int index = 0; index < pixels.Length; index++) { bytes[index * 4] = pixels[index].r; bytes[index * 4 + 1] = pixels[index].g; bytes[index * 4 + 2] = pixels[index].b; bytes[index * 4 + 3] = pixels[index].a; }
        return bytes;
    }
    private void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && _errors.Count < 12) _errors.Add(message + "\n" + stack);
    }
    private void WriteReceipt(bool passed)
    {
        File.WriteAllText(Path.Combine(_configuration.outputRoot, "windows-pixels.json"), JsonUtility.ToJson(new Receipt {
            sourceManifestSha256 = _configuration.sourceManifestSha256, unityVersion = Application.unityVersion,
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(), graphicsDeviceName = SystemInfo.graphicsDeviceName,
            originalWindowsDxbcPixelsCompared = _pictures.Count > 0, allCasesPassed = passed,
            pictures = _pictures.ToArray(), negativeControls = _negativeControls.ToArray(), errors = _errors.ToArray()
        }, true) + "\n");
    }
}
