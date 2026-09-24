using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GloomhavenVR.WorldUI;

/// <summary>Original game decoration, copied as rendering data without any native controllers.</summary>
internal sealed class TownServiceDecor : IDisposable
{
    private sealed class Piece
    {
        internal string Key = string.Empty, Entry = string.Empty;
        internal Vector3 Position;
        internal float Size;
        internal bool Candle, Arcane, FlameOnly, Template;
        internal string Select = string.Empty;
        internal Material[] EffectMaterials = Array.Empty<Material>();
        internal GameObject? Holder;
        internal Vector3 Home;
        internal int LightSlot;
        internal AsyncOperationHandle<ApparanceResourceList> Handle;
        internal GameObject? Source;
        internal bool Inspected, Built;
        internal readonly List<MaterialLoad> Materials = new();
    }
    private const string LegacyCoinMaterialGuid = "2c309731defe50f4d84721fd7f50c5c4";
    private const string NativeCoinMaterialAddress = "coinpile";
    private readonly List<Piece> _pieces = new();
    private static readonly Dictionary<byte, TownServiceDecor> Owners = new();
    private readonly byte _service;

    // Stable sparse indices: loading/failure never renumbers another source. Callers
    // compare Source to invalidate clones after resident recreation, not instance IDs
    // serialized onto the network. Every returned holder contains rendering data only.
    internal static Transform? StaticPropSource(byte service)
        => Owners.TryGetValue(service, out TownServiceDecor owner) && owner._root != null ? owner._root : null;
    internal static int StaticPropCount(byte service)
        => StaticPropSource(service) != null ? Owners[service]._pieces.Count : 0;
    internal static bool TryStaticProp(byte service, int index, out Transform? source, out string address)
    {
        source = null; address = string.Empty;
        if (StaticPropSource(service) == null) return false;
        TownServiceDecor owner = Owners[service];
        if (index < 0 || index >= owner._pieces.Count) return false;
        Piece piece = owner._pieces[index];
        if (piece == owner._workCoin || piece.Arcane || piece.Template || piece.Holder == null) return false;
        source = piece.Holder.transform;
        address = "decor." + service + "." + index;
        return true;
    }
    internal static bool TryPractical(byte service, int index, out Vector3 localPoint, out float rangeScale)
    {
        localPoint = Vector3.zero; rangeScale = 1f;
        if (!TryStaticProp(service, index, out Transform? source, out _) || source == null) return false;
        TownServiceDecor owner = Owners[service];
        Piece piece = owner._pieces[index];
        if (!piece.Candle) return false;
        localPoint = source.InverseTransformPoint(owner._root.TransformPoint(piece.Position + Vector3.up * (piece.Select.Length != 0 ? .18f : piece.Size)));
        rangeScale = Mathf.Abs(owner._root.lossyScale.x) / Mathf.Max(.0001f, Mathf.Abs(source.lossyScale.x));
        return true;
    }

    private sealed class MaterialLoad
    {
        internal object Key = null!;
        internal AsyncOperationHandle<Material> Handle;
        internal bool Held, Ready;
        internal int Attempts;
        internal float Started, RetryAt;
        internal string Failure = string.Empty;
        internal bool Exhausted => !Held && Attempts >= 3;
    }
    private readonly Dictionary<object, MaterialLoad> _loads = new();
    private readonly List<Material> _materials = new();
    private readonly List<Material> _timedMaterials = new();
    private readonly Transform _root;
    private readonly TownServiceLighting _lighting;
    private readonly TownServiceActivityProps _work;
    private Piece? _workCoin, _arcane, _coinTemplate;
    private TownServiceArcaneEffect? _magic;
    internal static Transform? CoinTemplate { get; private set; }
    private Transform? _castGrip;
    private bool _castGripBound;
    private float _visibility;
    private float _clock;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private float _nextLoadTick;
    private uint _coinGeneration = uint.MaxValue;
    private static readonly int Visibility = Shader.PropertyToID("_TownVisibility");
    private static readonly int AnimationTime = Shader.PropertyToID("_TownAnimationTime");

    internal TownServiceDecor(Transform station, byte service, TownServiceLighting lighting)
    {
        _root = station;
        _service = service;
        _lighting = lighting;
        _work = new TownServiceActivityProps(station, service, TownServiceAssets.Shader("townnpc"));
        try { Populate(service); Owners[service] = this; }
        catch { Dispose(); throw; }
    }

    private void Populate(byte service)
    {
        // These are the freestanding bodies inside the native wall-lantern prefab.
        // Its separate wall bracket is deliberately not copied onto the counter.
        if (service == 1)
        {
            Lantern(new Vector3(-1.51f, 1.23f, .08f), 0);
            // A small native candle lights the ledger without placing a second oversized
            // lantern in the offered hand's path. The hanging cabinet lantern is the key.
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#2",
                new Vector3(.285f, .957f, .435f), .14f, true, 1);
        }
        else
        {
            Lantern(new Vector3(-.68f, .957f, .20f), 0);
            Lantern(new Vector3(.68f, .957f, service == 3 ? .70f : .20f), 1);
        }
        if (service == 1)
        {
            Add("Treasure", "Treasure.Clutter.FloorSmall#3", new Vector3(.26f, .957f, .26f), .11f);
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#7", new Vector3(-.265f, .957f, .415f), .12f);
            Add("Library", "Library.Clutter.Shelf.Individual#7", new Vector3(-.055f, .957f, .18f), .24f);
            _workCoin = Add("Treasure", "Treasure.Clutter.Shelf.Individual#1", TownServiceActivityMotion.CoinSeat(0, false), .026f);
        }
        else if (service == 2)
        {
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#7", new Vector3(0f, .957f, .18f), .18f);
            Add("Library", "Library.Clutter.Shelf.Individual#7", new Vector3(-.33f, .957f, -.12f), .30f);
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#2", new Vector3(.40f, .957f, .23f), .20f);
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#2", new Vector3(.55f, .957f, .08f), .14f);
            Add("Chapel", "Chapel.Clutter.Shelf.Individual#2", new Vector3(-.54f, .957f, .19f), .16f);
            _coinTemplate = Add("Treasure", "Treasure.Clutter.Shelf.Individual#1", Vector3.zero, .05f);
            _coinTemplate.Template = true;
        }
        else
        {
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#3", new Vector3(-.48f, .957f, .21f), .23f);
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#11", new Vector3(-.34f, .957f, .24f), .12f);
            Add("Library", "Library.Clutter.Shelf.Individual#7", new Vector3(0f, .957f, .22f), .32f);
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#11", new Vector3(.48f, .957f, .26f), .15f);
            Add("AlchemyLab", "AlchemyLab.Clutter.Shelf.Individual#3", new Vector3(.59f, .957f, .06f), .13f);
            _arcane = Add("Tone_Candlelight", "Candlelight.Lighting.Torch.Wall#1", Vector3.zero, .11f, arcane: true);
        }
    }

    private void Lantern(Vector3 position, int slot)
    {
        Piece lantern = Add("Gaslight", "Gaslight.Lighting.Torch.Wall#1", position, .40f, true, slot);
        lantern.Select = "CR_INT_Lantern_01_b";
        Piece flame = Add("Tone_Candlelight", "Candlelight.Lighting.Torch.Wall#1", position + Vector3.up * .15f, .09f);
        flame.Select = "CandlePivot";
        flame.FlameOnly = true;
    }

    private Piece Add(string list, string entry, Vector3 position, float size, bool candle = false, int lightSlot = 0, bool arcane = false)
    {
        string key = "Assets/PCG/PCG_" + list + ".asset";
        var piece = new Piece { Key = key, Entry = entry, Position = position, Size = size, Candle = candle, LightSlot = lightSlot, Arcane = arcane,
            Handle = Addressables.LoadAssetAsync<ApparanceResourceList>(key) };
        _pieces.Add(piece);
        return piece;
    }

    internal bool Ready
    {
        get
        {
            foreach (Piece piece in _pieces) if (!piece.Built) return false;
            return true;
        }
    }

    internal void Tick()
    {
        float now = Time.unscaledTime;
        if (now < _nextLoadTick) return;
        _nextLoadTick = now + .1f;
        if (_coinTemplate?.Holder != null
            && _coinGeneration != GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Generation)
        {
            RegisterCoinTextures(_coinTemplate.Holder.transform);
            _coinGeneration = GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Generation;
        }
        foreach (MaterialLoad load in _loads.Values) Advance(load, now);
        foreach (Piece piece in _pieces)
        {
            if (piece.Built || !piece.Handle.IsDone) continue;
            try
            {
                if (!piece.Inspected)
                {
                    piece.Inspected = true;
                    if (piece.Handle.Status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException("Original decoration list unavailable: " + piece.Key);
                    foreach (ApparanceObjectResource resource in piece.Handle.Result.Objects)
                        if (resource.Name == piece.Entry) { piece.Source = resource.Object as GameObject; break; }
                    if (piece.Source == null) throw new InvalidOperationException("Original decoration missing: " + piece.Entry);
                    foreach (MaterialLoader loader in piece.Source.GetComponentsInChildren<MaterialLoader>(true))
                        foreach (MaterialLoaderData data in loader.LoadersData)
                            foreach (AssetReferenceT<Material> reference in data.MaterialReferences)
                                if (reference != null && reference.RuntimeKeyIsValid())
                                {
                                    object key = MaterialKey(piece, data, reference.RuntimeKey);
                                    if (!_loads.TryGetValue(key, out MaterialLoad load))
                                    {
                                        load = new MaterialLoad { Key = key };
                                        _loads.Add(load.Key, load);
                                        Advance(load, now);
                                    }
                                    if (!piece.Materials.Contains(load)) piece.Materials.Add(load);
                                }
                }
                bool ready = true;
                // A failed lantern material must not suppress an unrelated book, bowl or
                // the other station's light. Only this piece's actual dependencies gate it.
                foreach (MaterialLoad load in piece.Materials)
                {
                    if (load.Exhausted) throw new InvalidOperationException("Original decoration material unavailable: " + load.Key + "; " + load.Failure);
                    if (!load.Ready) ready = false;
                }
                if (!ready) continue;
                Build(piece);
                piece.Built = true;
            }
            catch (Exception e)
            {
                piece.Built = true;
                Report(piece.Entry, e.Message);
            }
        }
    }

    private static object MaterialKey(Piece piece, MaterialLoaderData data, object key)
    {
        // The shipped PCG_coin_heads prefab's coinsingle renderer references an
        // obsolete standalone GUID absent from the game's catalog (build 547 logs).
        // Its own native coinpile.fbx dependency still exports GoldCoinMat, and the
        // catalog explicitly registers the "coinpile" key for Material as well as
        // GameObject/Mesh. Resolve that exact original asset, never a made-up gold
        // shader or a different coin atlas. Do not rewrite unrelated loader keys.
        return piece.Entry == "Treasure.Clutter.Shelf.Individual#1"
            && data.Renderer != null && data.Renderer.name == "coinsingle"
            && key is string guid && guid == LegacyCoinMaterialGuid
            ? NativeCoinMaterialAddress : key;
    }

    private void Advance(MaterialLoad load, float now)
    {
        if (load.Ready || load.Exhausted) return;
        if (!load.Held)
        {
            if (now < load.RetryAt) return;
            load.Attempts++;
            load.Started = now;
            try
            {
                load.Handle = Addressables.LoadAssetAsync<Material>(load.Key);
                load.Held = load.Handle.IsValid();
                if (!load.Held) Failed(load, now, "invalid handle");
            }
            catch (Exception error) { Failed(load, now, error.GetType().Name + ": " + error.Message); }
            return;
        }
        if (!load.Handle.IsValid()) { Failed(load, now, "handle became invalid"); return; }
        if (!load.Handle.IsDone)
        {
            if (now - load.Started > 30f) Failed(load, now, "load timed out");
            return;
        }
        if (load.Handle.Status != AsyncOperationStatus.Succeeded || load.Handle.Result == null)
        { Failed(load, now, load.Handle.OperationException?.Message ?? "original material unavailable"); return; }
        if (load.Key is string key && key == NativeCoinMaterialAddress && load.Handle.Result.name != "GoldCoinMat")
        { Failed(load, now, "native coin material identity does not match GoldCoinMat"); return; }
        load.Ready = true;
    }

    private void Failed(MaterialLoad load, float now, string reason)
    {
        Release(load);
        load.Failure = reason;
        load.RetryAt = now + (load.Attempts == 1 ? 1f : 4f);
        if (load.Attempts == 1) Report(load.Key.ToString() ?? "material", "material load will retry: " + reason);
    }

    private static void Release(MaterialLoad load)
    {
        if (load.Held && load.Handle.IsValid()) Addressables.Release(load.Handle);
        load.Held = false;
        load.Ready = false;
    }

    private void Report(string key, string reason)
    {
        if (_reported.Count < 8 && _reported.Add(key))
            VRLog.Warn("TownServices", "Original town decoration could not be prepared: " + key + "; " + reason);
    }

    private void Build(Piece piece)
    {
        var holder = new GameObject("Original." + piece.Entry);
        holder.SetActive(false);
        try
        {
            holder.transform.SetParent(_root, false);
            holder.transform.localPosition = piece.Position;
            var overrides = new Dictionary<Renderer, Material[]>();
            foreach (MaterialLoader loader in piece.Source!.GetComponentsInChildren<MaterialLoader>(true))
                foreach (MaterialLoaderData data in loader.LoadersData)
                {
                    if (data.Renderer == null) continue;
                    var materials = new List<Material>();
                    foreach (AssetReferenceT<Material> reference in data.MaterialReferences)
                        if (reference != null && _loads.TryGetValue(MaterialKey(piece, data, reference.RuntimeKey), out MaterialLoad load) && load.Ready) materials.Add(load.Handle.Result);
                    if (data.IsSaveExistedMaterials)
                        foreach (Material material in data.Renderer.sharedMaterials) if (material != null) materials.Add(material);
                    overrides[data.Renderer] = materials.ToArray();
                }
            Transform visual = piece.Source.transform;
            if (piece.Select.Length != 0)
            {
                Transform? selected = null;
                foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
                    if (child.name == piece.Select) { selected = child; break; }
                visual = selected ?? throw new InvalidOperationException("Original prop part unavailable: " + piece.Select);
            }
            if (piece.Arcane)
            {
                // Reuse the original candle's soft glow texture/quad as a restrained hand
                // effect. Copy rendering data only; never run native particle controllers.
                Transform? glow = null;
                foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
                    if (child.name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0
                        && child.GetComponent<MeshFilter>() != null) { glow = child; break; }
                if (glow == null) throw new InvalidOperationException("Original candle glow mesh unavailable");
                visual = glow;
            }
            int materialStart = _materials.Count;
            Copy(visual, holder.transform, overrides, piece.FlameOnly);
            if (piece.Arcane)
            {
                piece.EffectMaterials = _materials.GetRange(materialStart, _materials.Count - materialStart).ToArray();
                foreach (Material material in piece.EffectMaterials)
                {
                    material.SetColor("_Color", new Color(.30f, .65f, .90f, .55f));
                    material.SetFloat(Visibility, 0f);
                }
            }
            Bounds bounds = default;
            bool any = false;
            foreach (MeshFilter filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || (!piece.Arcane && !piece.FlameOnly && IsFlame(filter.name))) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = holder.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            if (!any || bounds.size.sqrMagnitude < 1e-8f) { UnityEngine.Object.Destroy(holder); return; }
            float size = piece.Candle ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float factor = piece.Size / Mathf.Max(.0001f, size);
            holder.transform.localScale = Vector3.one * factor;
            holder.transform.localPosition = piece.Position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * factor;
            if (piece.Candle)
                _lighting.SetFlame(_root.TransformPoint(piece.Position + Vector3.up * (piece.Select.Length != 0 ? .18f : piece.Size)), piece.LightSlot);
            piece.Holder = holder;
            piece.Home = holder.transform.localPosition;
            if (piece == _workCoin) _work.BindCoin(holder.transform, piece.Home - piece.Position);
            if (piece.Arcane)
            {
                MeshFilter glow = holder.GetComponentInChildren<MeshFilter>(true);
                _magic = new TownServiceArcaneEffect(_root, glow, piece.EffectMaterials);
            }
            if (piece.Template)
            {
                holder.name = "Town.OriginalCoinTemplate";
                RegisterCoinTextures(holder.transform);
                _coinGeneration = GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.Generation;
                CoinTemplate = holder.transform;
            }
            else holder.SetActive(!piece.Arcane);
        }
        catch { UnityEngine.Object.Destroy(holder); throw; }
    }

    private static void RegisterCoinTextures(Transform root)
    {
        int rendererIndex = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            int materialIndex = 0;
            foreach (Material material in renderer.sharedMaterials)
            {
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null)
                        GloomhavenVR.Net.TownServices.TownServiceMirror.Assets.RegisterOriginal(
                            "town-prop|Treasure.Clutter.Shelf.Individual#1|" + rendererIndex + "|" + materialIndex + "|" + property, texture);
                }
                materialIndex++;
            }
            rendererIndex++;
        }
    }

    private void Copy(Transform source, Transform parent, Dictionary<Renderer, Material[]> overrides, bool forceFlame = false)
    {
        // Reconstruct, do not Instantiate: even disabled MonoBehaviours can execute Awake.
        var obj = new GameObject(source.name) { layer = VRLayers.ModLayer };
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = source.localPosition;
        obj.transform.localRotation = source.localRotation;
        obj.transform.localScale = source.localScale;
        MeshFilter? filter = source.GetComponent<MeshFilter>();
        MeshRenderer? renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && filter.sharedMesh != null && renderer != null)
        {
            obj.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer copy = obj.AddComponent<MeshRenderer>();
            Material[] originals = overrides.TryGetValue(renderer, out Material[] found) ? found : renderer.sharedMaterials;
            var materials = new Material[originals.Length];
            bool flame = forceFlame || IsFlame(source.name);
            bool glass = source.name.EndsWith("_Glass", StringComparison.Ordinal);
            for (int i = 0; i < originals.Length; i++) materials[i] = Adapt(originals[i], flame || glass, flame);
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
        }
        foreach (Transform child in source) Copy(child, obj.transform, overrides, forceFlame);
    }

    private static bool IsFlame(string name) => name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0;

    private Material Adapt(Material source, bool flame, bool billboard)
    {
        Shader? shader = TownServiceAssets.Shader(flame ? "townflame" : "townnpc");
        if (source == null || shader == null) throw new InvalidOperationException("Original decoration material or town shader missing");
        // Preserve original texture data, UV transform, tint and normal map. Change only the
        // render contract (stereo, practical lighting and station dissolve), on an owned copy.
        var material = TownServiceDecorMaterial.Copy(source, shader);
        material.SetFloat(Visibility, _visibility);
        if (flame)
        {
            if (source.HasProperty("_TintColor")) material.SetColor("_Color", source.GetColor("_TintColor"));
            if (material.HasProperty("_Billboard")) material.SetFloat("_Billboard", billboard ? 1f : 0f);
            if (material.HasProperty("_TownAnimationTime")) material.SetFloat("_TownAnimationTime", _clock);
        }
        if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
        if (material.HasProperty(AnimationTime)) _timedMaterials.Add(material);
        _materials.Add(material);
        return material;
    }

    internal void SampleActivity(in TownActivityVisual visual)
    {
        _work.Sample(in visual);
        if (_arcane?.Holder == null) return;
        if (!_castGripBound)
        {
            // The station constructs its immutable activity rig before decoration. Missing
            // grips in an incompatible bundle must not turn into a hierarchy scan per frame.
            _castGripBound = true;
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
                if (child.name == "ActivityOfferingPalm") { _castGrip = child; break; }
        }
        if (_castGrip == null) return;
        float strength = visual.Cast;
        _magic?.Sample(_castGrip.position + _castGrip.up * (.07f + .10f * strength),
            _castGrip.up, visual.EffectClock, strength);
        foreach (Material material in _arcane.EffectMaterials)
            material.SetFloat(Visibility, _visibility * strength);
    }

    internal void SuspendActivity()
    {
        _work.Suspend();
        _magic?.Suspend();
        if (_arcane?.Holder != null) _arcane.Holder.SetActive(false);
    }

    internal void SetClock(float seconds)
    {
        _clock = seconds;
        foreach (Material material in _timedMaterials) material.SetFloat(AnimationTime, seconds);
    }

    internal void SetVisibility(float value)
    {
        if (_visibility == value) return;
        _visibility = value;
        _work.SetVisibility(value);
        foreach (Material material in _materials) material.SetFloat(Visibility, value);
        // Only a successfully sampled activity may expose this optional effect. An absent
        // grip or a disabled cosmetic rig must not leave a bright orphan at station origin.
        if (_arcane != null)
            foreach (Material material in _arcane.EffectMaterials) material.SetFloat(Visibility, 0f);
    }

    public void Dispose()
    {
        if (Owners.TryGetValue(_service, out TownServiceDecor owner) && ReferenceEquals(owner, this)) Owners.Remove(_service);
        if (_coinTemplate?.Holder != null && CoinTemplate == _coinTemplate.Holder.transform) CoinTemplate = null;
        _work.Dispose();
        _magic?.Dispose();
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        foreach (MaterialLoad load in _loads.Values) Release(load);
        foreach (Piece piece in _pieces) if (piece.Handle.IsValid()) Addressables.Release(piece.Handle);
        _materials.Clear(); _timedMaterials.Clear(); _loads.Clear(); _pieces.Clear();
    }
}
