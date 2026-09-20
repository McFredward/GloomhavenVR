using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Net.TownServices;

internal static class TownServiceMaterial
{
    internal static TownServiceValue Read(Material? material, TownServiceAssets assets)
    {
        if (material == null) return new TownServiceValue();
        Shader shader = material.shader;
        int count = shader.GetPropertyCount();
        if (count > 60) throw new InvalidDataException("Town-service material exceeds the property budget.");
        var numbers = new List<float>(4 + count * 5)
        { material.renderQueue, material.enableInstancing ? 1 : 0, material.doubleSidedGI ? 1 : 0, (int)material.globalIlluminationFlags };
        var text = new List<string>(2 + count * 2) { assets.Key(shader), string.Join("\n", material.shaderKeywords) };
        for (int i = 0; i < count; i++)
        {
            string name = shader.GetPropertyName(i);
            ShaderPropertyType type = shader.GetPropertyType(i);
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
            numbers.Add((int)type); numbers.Add(value.x); numbers.Add(value.y); numbers.Add(value.z); numbers.Add(value.w);
            text.Add(name); text.Add(texture);
        }
        return new TownServiceValue { Numbers = numbers.ToArray(), Text = text.ToArray() };
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
        if (value.Text.Length == 0)
        {
            if (owned != null) UnityEngine.Object.Destroy(owned);
            return null;
        }
        Shader shader = assets.Resolve<Shader>(value.Text[0])!;
        if (owned == null || owned.shader != shader)
        {
            if (owned != null) UnityEngine.Object.Destroy(owned);
            owned = new Material(shader) { name = "GVR town-service owner material" };
        }
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
