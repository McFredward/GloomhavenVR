using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class NativeTemplateBasis
    {
        internal GameObject Template = null!;
        internal uint Generation, Structure;
        internal ulong Key;
        internal TownServiceNode[] Nodes = Array.Empty<TownServiceNode>();
        internal TownServiceBinding Binding = null!;
    }
    private static readonly Dictionary<string, NativeTemplateBasis> NativeTemplateBases = new(StringComparer.Ordinal);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalModule, NativeTemplateRepair> NativeTemplateRepairs = new();
    private sealed class NativeTemplateRepair { internal float After; }
    private sealed class MageValidatedOriginal
    {
        internal TownServiceFrame Received = null!;
        internal GameObject Template = null!;
        internal uint Generation;
    }
    private static readonly Dictionary<long, MageValidatedOriginal> MageValidatedOriginals = new();
    private static readonly UTF8Encoding NativeTemplateUtf8 = new(false, true);

    /// <summary>Build the native basis once for this actual frozen prefab/asset lifetime.
    /// Only original rendering output is read; no native controller is enabled.</summary>
    private static NativeTemplateBasis? NativeBasis(TownServiceFrame frame)
    {
        string key = TemplateKey(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.TryGetValue(key, out GameObject? template) || template == null)
        {
            if (!(ResolveTemplate?.Invoke(frame.Service, frame.Template, frame.TemplateAddress) ?? false)
                || !Templates.TryGetValue(key, out template) || template == null) return null;
        }
        if (NativeTemplateBases.TryGetValue(key, out NativeTemplateBasis? cached)
            && ReferenceEquals(cached.Template, template) && cached.Generation == Assets.Generation) return cached;
        // A cache key names an actual original lifetime, never a viewer's character,
        // dial or last network image. Dead frozen originals are harmless to reclaim.
        if (NativeTemplateBases.Count >= TownServiceFrame.MaxModules)
        {
            var retired = new List<string>();
            foreach (var pair in NativeTemplateBases)
                if (pair.Value.Template == null || pair.Value.Generation != Assets.Generation) retired.Add(pair.Key);
            foreach (string old in retired) { NativeTemplateBases[old].Binding.Dispose(); NativeTemplateBases.Remove(old); }
            if (NativeTemplateBases.Count >= TownServiceFrame.MaxModules) return null;
        }
        if (cached != null) cached.Binding.Dispose();
        var binding = new TownServiceBinding(template.transform);
        var basis = new NativeTemplateBasis { Template = template, Generation = Assets.Generation,
            Structure = binding.Structure, Nodes = binding.Read(Assets, includeInactiveGraphics: true), Binding = binding };
        basis.Key = NativeBasisKey(basis);
        NativeTemplateBases[key] = basis;
        return basis;
    }

    private static bool NativeTextProperty(ushort key) => key is TownServiceProperty.TmpText
        or TownServiceProperty.LegacyText or TownServiceProperty.TextMaterial;
    private static bool OwnerProperty(int index, ushort key) => index == 0 || NativeTextProperty(key)
        || key == TownServiceProperty.Sibling;

    private static ulong NativeBasisKey(NativeTemplateBasis basis)
    {
        // Owner text, font, style and text-material output always accompanies the
        // patch. Its native template defaults can be localized differently on the
        // observer, so those defaults never participate in the shared basis key.
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, NativeTemplateUtf8, true))
        {
            writer.Write((byte)1); writer.Write(basis.Structure); writer.Write((ushort)basis.Nodes.Length);
            for (int index = 0; index < basis.Nodes.Length; index++)
            {
                TownServiceNode node = basis.Nodes[index]; writer.Write(node.Binding);
                for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                {
                    if (!node.Values.TryGetValue(key, out TownServiceValue? value)) continue;
                    writer.Write(key);
                    if (OwnerProperty(index, key)) continue;
                    writer.Write((ushort)value.Numbers.Length);
                    foreach (float number in value.Numbers) writer.Write(number == 0f ? 0f : number);
                    writer.Write((byte)value.Text.Length);
                    foreach (string text in value.Text) writer.Write(text);
                }
                writer.Write((ushort)0);
            }
        }
        using var sha = SHA256.Create(); byte[] digest = sha.ComputeHash(bytes.ToArray());
        ulong result = 0; for (int i = 0; i < 8; i++) result |= (ulong)digest[i] << (8 * i);
        return result == 0 ? 1UL : result;
    }

    /// <summary>Transmit owner headers and actual differences from the original prefab.
    /// Asset keys remain metadata; image pixels have never been transmitted here.</summary>
    internal static bool TryWriteNativeTemplateState(TownServiceFrame complete, out byte[] packet)
    {
        packet = Array.Empty<byte>();
        if ((complete.Service != 3 && !(complete.Service == 1 && complete.PublicCatalog)) || complete.VisitorStock
            || complete.Module >= TownServiceFrame.VoiceModule || complete.BaseSequence != 0
            || complete.NativeTemplateBasisKey != 0 || complete.Nodes.Length == 0
            || complete.CatalogBank != null || complete.Rack != null) return false;
        try
        {
            NativeTemplateBasis? basis = NativeBasis(complete);
            if (basis == null || basis.Structure != complete.Structure || basis.Nodes.Length != complete.Nodes.Length) return false;
            for (int i = 0; i < complete.Nodes.Length; i++)
            {
                TownServiceNode before = basis.Nodes[i], current = complete.Nodes[i];
                if (before.Binding != current.Binding || before.Values.Count != current.Values.Count) return false;
                foreach (ushort key in before.Values.Keys) if (!current.Values.ContainsKey(key)) return false;
            }
            // An unchanged full original remains a staggered repair for an observer
            // whose real game template differs. It does not sit ahead of the first
            // immediately reconstructable native state in the urgent artwork lane.
            if (Local.TryGetValue(complete.Module, out LocalModule? source))
            {
                NativeTemplateRepair repair = NativeTemplateRepairs.GetValue(source,
                    module => new NativeTemplateRepair { After = Time.unscaledTime + 10f + module.Id % 11 * .09f });
                if (Time.unscaledTime >= repair.After)
                { repair.After = Time.unscaledTime + 10f + source.Id % 11 * .09f; return false; }
            }
            var patch = new List<TownServiceNode>();
            for (int i = 0; i < complete.Nodes.Length; i++)
            {
                TownServiceNode current = complete.Nodes[i], before = basis.Nodes[i];
                TownServiceNode? changed = null;
                foreach (var property in current.Values)
                    if (OwnerProperty(i, property.Key) || !property.Value.Same(before.Values[property.Key]))
                    { changed ??= new TownServiceNode { Binding = current.Binding }; changed.Values.Add(property.Key, property.Value); }
                if (changed != null) patch.Add(changed);
            }
            // The historical TLV78 reader requires one node for a visible complete
            // module. An empty property list is a legitimate unchanged native root.
            if (patch.Count == 0) patch.Add(new TownServiceNode { Binding = complete.Nodes[0].Binding });
            TownServiceFrame sparse = TownServiceDelta.Retain(complete);
            sparse.NativeTemplateBasisKey = basis.Key; sparse.Nodes = patch.ToArray();
            byte[] encoded = TownServiceCodec.Write(sparse);
            // Serialization of the full original is deliberately avoided on the
            // compact path: native materials repeat many long asset descriptors.
            packet = encoded;
            return true;
        }
        catch (Exception error) { Report("native template metadata capture", error); return false; }
    }

    internal static bool TryExpandNativeTemplateState(TownServiceFrame received, out TownServiceFrame complete)
    {
        complete = received;
        if (received.NativeTemplateBasisKey == 0) return true;
        try
        {
            NativeTemplateBasis? basis = NativeBasis(received);
            if (basis == null || basis.Structure != received.Structure || basis.Key != received.NativeTemplateBasisKey)
                throw new InvalidDataException("Original town metadata requires its exact frozen native template.");
            var changes = new Dictionary<uint, TownServiceNode>();
            foreach (TownServiceNode node in received.Nodes)
                if (changes.ContainsKey(node.Binding)) return false;
                else changes.Add(node.Binding, node);
            TownServiceFrame expanded = TownServiceDelta.Retain(received);
            expanded.NativeTemplateBasisKey = 0; expanded.Nodes = new TownServiceNode[basis.Nodes.Length];
            for (int i = 0; i < basis.Nodes.Length; i++)
            {
                TownServiceNode before = basis.Nodes[i];
                var node = new TownServiceNode { Binding = before.Binding };
                foreach (var property in before.Values) node.Values.Add(property.Key, property.Value);
                if (changes.TryGetValue(before.Binding, out TownServiceNode? changed))
                {
                    foreach (var property in changed.Values)
                    {
                        if (!node.Values.ContainsKey(property.Key)) return false;
                        node.Values[property.Key] = property.Value;
                    }
                    changes.Remove(before.Binding);
                }
                // A localized observer must never substitute its default text/font
                // for an omitted owner property. Such a sparse packet is incomplete.
                foreach (ushort key in before.Values.Keys)
                    if (OwnerProperty(i, key) && (changed == null || !changed.Values.ContainsKey(key))) return false;
                expanded.Nodes[i] = node;
            }
            if (changes.Count != 0) return false;
            TownServiceCodec.Validate(expanded); complete = expanded; return true;
        }
        catch (Exception error) { Report("native template metadata playback", error); return false; }
    }

    /// <summary>Admit one complete original enhancement picture. New rows, the ring
    /// and card cannot become separate incremental pictures while dependencies arrive.</summary>
    private static bool MagePictureReady(int peer, TownServiceSessionInfo session,
        Dictionary<ushort, TownServiceFrame> pending)
    {
        if (peer <= 0 || session.Service != 3 || session.Modules.Length == 0) return true;
        foreach (ushort id in session.Modules)
        {
            if (!pending.TryGetValue(id, out TownServiceFrame? received)
                || received.Session != session.Session || received.Service != 3) return false;
            TownServiceFrame? baseline = null;
            if (ReceivedBaselines.TryGetValue(peer, out var baselines)) baselines.TryGetValue(id, out baseline);
            TownServiceFrame? frame = TownServiceDelta.Expand(baseline, received);
            if (frame == null || frame.NativeTemplateBasisKey != 0) return false;
            if (!frame.Visible || frame.ParentAlpha <= 0f) continue;
            try
            {
                NativeTemplateBasis? basis = NativeBasis(frame);
                if (basis == null || basis.Structure != frame.Structure) return false;
                long key = ((long)peer << 16) | id;
                if (MageValidatedOriginals.TryGetValue(key, out var validated)
                    && ReferenceEquals(validated.Received, received)
                    && ReferenceEquals(validated.Template, basis.Template)
                    && validated.Generation == Assets.Generation) continue;
                // Validate a new immutable native revision once, never build or
                // traverse a new prefab binding on every rendering frame.
                basis.Binding.Validate(frame, Assets);
                if (MageValidatedOriginals.Count >= 8 * TownServiceFrame.MaxModules
                    && !MageValidatedOriginals.ContainsKey(key)) MageValidatedOriginals.Clear();
                MageValidatedOriginals[key] = new MageValidatedOriginal { Received = received,
                    Template = basis.Template, Generation = Assets.Generation };
            }
            catch (Exception error) { Report("complete native enhancement picture", error); return false; }
        }
        return true;
    }

    private static void ResetNativeTemplateState()
    {
        foreach (NativeTemplateBasis basis in NativeTemplateBases.Values) basis.Binding.Dispose();
        NativeTemplateBases.Clear(); MageValidatedOriginals.Clear();
    }
}
