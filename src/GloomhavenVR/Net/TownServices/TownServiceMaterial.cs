using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Net.TownServices;

internal static class TownServiceMaterial
{
    private sealed class Sample
    {
        internal int Frame = -1;
        internal Shader? Shader;
        internal string[] Names = Array.Empty<string>();
        internal ShaderPropertyType[] Types = Array.Empty<ShaderPropertyType>();
        internal TownServiceValue Scratch = new();
        internal TownServiceValue Published = new();
    }
    private static ConditionalWeakTable<Material, Sample> _samples = new();
    private static readonly TownServiceValue Empty = new();
    private sealed class Shared
    { internal TownServiceValue Value = null!; internal Material Material = null!; internal int References; }
    private static readonly Dictionary<TownServiceValue, Shared> SharedValues = new(TownServiceValueComparer.Instance);
    private static readonly Dictionary<Material, Shared> SharedMaterials = new();
    internal static void Reset() => _samples = new ConditionalWeakTable<Material, Sample>();
    internal static TownServiceValue Read(Material? material, TownServiceAssets assets)
    {
        if (material == null) return Empty;
        Sample sample = _samples.GetValue(material, _ => new Sample());
        if (sample.Frame == Time.frameCount) return sample.Published;
        Shader shader = material.shader;
        int count = shader.GetPropertyCount();
        if (count > 60) throw new InvalidDataException("Town-service material exceeds the property budget.");
        if (sample.Shader != shader || sample.Names.Length != count)
        {
            sample.Shader = shader; sample.Names = new string[count]; sample.Types = new ShaderPropertyType[count];
            sample.Scratch = new TownServiceValue { Numbers = new float[4 + count * 5], Text = new string[2 + count * 2] };
            for (int i = 0; i < count; i++) { sample.Names[i] = shader.GetPropertyName(i); sample.Types[i] = shader.GetPropertyType(i); }
        }
        float[] numbers = sample.Scratch.Numbers; string[] text = sample.Scratch.Text;
        numbers[0] = material.renderQueue; numbers[1] = material.enableInstancing ? 1 : 0;
        numbers[2] = material.doubleSidedGI ? 1 : 0; numbers[3] = (int)material.globalIlluminationFlags;
        text[0] = assets.Key(shader); text[1] = string.Join("\n", material.shaderKeywords);
        for (int i = 0; i < count; i++)
        {
            string name = sample.Names[i]; ShaderPropertyType type = sample.Types[i];
            Vector4 value = Vector4.zero; string texture = string.Empty;
            switch (type)
            {
                case ShaderPropertyType.Color: value = material.GetColor(name); break;
                case ShaderPropertyType.Vector: value = material.GetVector(name); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: value.x = material.GetFloat(name); break;
                case ShaderPropertyType.Texture:
                    texture = assets.Key(material.GetTexture(name));
                    Vector2 scale = material.GetTextureScale(name), offset = material.GetTextureOffset(name);
                    value = new Vector4(scale.x, scale.y, offset.x, offset.y); break;
                default: throw new InvalidDataException("Unsupported town-service shader property.");
            }
            int n = 4 + i * 5, t = 2 + i * 2;
            numbers[n] = (int)type; numbers[n + 1] = value.x; numbers[n + 2] = value.y; numbers[n + 3] = value.z; numbers[n + 4] = value.w;
            text[t] = name; text[t + 1] = texture;
        }
        if (!sample.Scratch.Same(sample.Published)) sample.Published = new TownServiceValue { Numbers = (float[])numbers.Clone(), Text = (string[])text.Clone() };
        sample.Frame = Time.frameCount; return sample.Published;
    }

    internal static void Validate(TownServiceValue value, TownServiceAssets assets)
    {
        if (value.Text.Length == 0 && value.Numbers.Length == 0) return;
        if (value.Text.Length < 2 || (value.Text.Length - 2) % 2 != 0
            || value.Numbers.Length != 4 + (value.Text.Length - 2) / 2 * 5)
            throw new InvalidDataException("Malformed town-service material.");
        Shader shader = assets.Resolve<Shader>(value.Text[0]) ?? throw new InvalidDataException("Missing native shader.");
        int count = (value.Text.Length - 2) / 2;
        if (shader.GetPropertyCount() != count) throw new InvalidDataException("Native shader differs between peers.");
        for (int i = 0; i < count; i++)
        {
            int n = 4 + i * 5, t = 2 + i * 2;
            if (shader.GetPropertyName(i) != value.Text[t] || (int)shader.GetPropertyType(i) != value.Numbers[n])
                throw new InvalidDataException("Native material property differs between peers.");
            if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                assets.Resolve<Texture>(value.Text[t + 1]);
        }
    }

    internal static Material? Apply(TownServiceValue value, TownServiceAssets assets, Material? owned)
    {
        if (owned != null && SharedMaterials.TryGetValue(owned, out Shared? before) && value.Same(before.Value)) return owned;
        Release(owned);
        if (value.Text.Length == 0) return null;
        if (!SharedValues.TryGetValue(value, out Shared? shared))
        {
            shared = new Shared { Value = value, Material = Build(value, assets)! };
            SharedValues.Add(value, shared); SharedMaterials.Add(shared.Material, shared);
        }
        shared.References++; return shared.Material;
    }
    internal static void Release(Material? material)
    {
        if (material == null) return;
        if (!SharedMaterials.TryGetValue(material, out Shared? shared))
            throw new InvalidOperationException("Town-service material is not owned by the presentation pool.");
        if (--shared.References > 0) return;
        SharedMaterials.Remove(material); SharedValues.Remove(shared.Value); UnityEngine.Object.Destroy(material);
    }
    private static Material? Build(TownServiceValue value, TownServiceAssets assets)
    {
        if (value.Text.Length == 0)
            return null;
        Shader shader = assets.Resolve<Shader>(value.Text[0])!;
        var owned = new Material(shader) { name = "GVR town-service owner material" };
        owned.renderQueue = (int)value.Numbers[0]; owned.enableInstancing = value.Numbers[1] != 0;
        owned.doubleSidedGI = value.Numbers[2] != 0; owned.globalIlluminationFlags = (MaterialGlobalIlluminationFlags)value.Numbers[3];
        owned.shaderKeywords = value.Text[1].Length == 0 ? Array.Empty<string>() : value.Text[1].Split('\n');
        for (int i = 0; i < (value.Text.Length - 2) / 2; i++)
        {
            int n = 4 + i * 5, t = 2 + i * 2; string name = value.Text[t];
            var vector = new Vector4(value.Numbers[n + 1], value.Numbers[n + 2], value.Numbers[n + 3], value.Numbers[n + 4]);
            switch ((ShaderPropertyType)value.Numbers[n])
            {
                case ShaderPropertyType.Color: owned.SetColor(name, vector); break;
                case ShaderPropertyType.Vector: owned.SetVector(name, vector); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: owned.SetFloat(name, vector.x); break;
                case ShaderPropertyType.Texture:
                    owned.SetTexture(name, assets.Resolve<Texture>(value.Text[t + 1]));
                    owned.SetTextureScale(name, new Vector2(vector.x, vector.y));
                    owned.SetTextureOffset(name, new Vector2(vector.z, vector.w)); break;
            }
        }
        return owned;
    }
}
