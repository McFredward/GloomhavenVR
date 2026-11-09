using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class ProgressionTests
{
    static void NativeTexturePolicyCensus()
    {
        var scoped = new TownServiceAssets(ScenarioResultsOriginalTextures.Contains);
        var originals = NativeMaterials.StaticOriginalTextures().ToArray();
        var duplicates = new List<Texture2D>();
        Check(originals.Length == NativeMaterials.StaticTextureCount && originals.Length > 14,
            "late native dependency census covers every exported static original beyond the old fourteen textures");
        Check(NativeMaterials.DeferredTextureCount == 13,
            "thirteen original unpopulated legacy font textures remain deferred rather than fabricated uploads");
        int admitted = 0, refused = 0, defaultRefused = 0;
        foreach (var texture in originals)
        {
            NativeMaterials.CheckStaticTexture(texture, Check);
            string key = NativeMaterials.Descriptor(texture);
            bool expected = CatalogExpectations.Contains(key);
            Check(ScenarioResultsOriginalTextures.Contains(key) == expected,
                "Results-only catalog keeps conflicting and mutable original groups excluded");
            bool known = expected || TownServiceAssets.SameKnownNativeIdentity(texture);
            var copy = NativeMaterials.DuplicateOriginal(texture);
            duplicates.Add(copy);
            if (known)
            {
                scoped.Key(texture); scoped.Key(copy); admitted++;
                if (!TownServiceAssets.SameKnownNativeIdentity(texture))
                {
                    var ordinary = new TownServiceAssets(); ordinary.Key(texture);
                    bool blocked = false;
                    try { ordinary.Key(copy); } catch (InvalidDataException) { blocked = true; }
                    Check(blocked, "ordinary non-Results registries retain their exact strict descriptor collision policy");
                    ordinary.Clear(); defaultRefused++;
                }
            }
            else
            {
                // Each actual original is isolated here: distinct payloads can
                // share a descriptor, and mutable FontTexture is never a static
                // catalog identity even when this serialized payload is nonempty.
                var unverified = new TownServiceAssets(ScenarioResultsOriginalTextures.Contains);
                unverified.Key(texture); bool blocked = false;
                try { unverified.Key(copy); } catch (InvalidDataException) { blocked = true; }
                Check(blocked, "conflicting or mutable native descriptor groups remain refused inside Results too");
                blocked = false;
                try { unverified.Resolve<Texture2D>(key); } catch (InvalidDataException) { blocked = true; }
                Check(blocked, "a refused native group cannot resolve an arbitrary same-descriptor original");
                unverified.Clear(); refused++;
            }
        }
        typeof(TownServiceAssets).GetField("_nextScan", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scoped, 0f);
        scoped.Scan();
        foreach (var texture in originals)
        {
            string key = NativeMaterials.Descriptor(texture);
            if (!ScenarioResultsOriginalTextures.Contains(key) && !TownServiceAssets.SameKnownNativeIdentity(texture)) continue;
            var resolved = scoped.Resolve<Texture2D>(key);
            Check(resolved != null && NativeMaterials.Descriptor(resolved) == key,
                "every admitted static material dependency still resolves after the complete duplicate Resources census");
            Check(resolved!.GetRawTextureData().SequenceEqual(texture.GetRawTextureData()),
                "verified equivalent originals retain their complete native pixel payload after late discovery");
        }
        Check(admitted > 14 && refused > 0 && defaultRefused > 0,
            "the complete native census exercises new scoped admission, strict exclusions and unchanged default policy");
        var unknown = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "NotAnOriginalResultsDependency" };
        var other = Object.Instantiate(unknown); other.name = unknown.name;
        var unknownRegistry = new TownServiceAssets(ScenarioResultsOriginalTextures.Contains); unknownRegistry.Key(unknown);
        bool rejected = false;
        try { unknownRegistry.Key(other); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "unknown same-shape textures are never admitted by the Results-only original catalog");
        unknownRegistry.Clear(); scoped.Clear(); Object.DestroyImmediate(unknown); Object.DestroyImmediate(other);
        foreach (var copy in duplicates) Object.DestroyImmediate(copy);
    }

    // Keep both native windows continuously active, as they are in two separate
    // peers. Render isolation must not invent Selectable disable/enable cycles.
    static void Layer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) Layer(child, layer);
    }

    static ScenarioResultsState DeliverResults(ScenarioResultsPresentation receiver, ScenarioResultsState state)
    {
        var packet = ScenarioResultsCodec.Write(state);
        Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var decoded) && decoded != null,
            "runtime native button state traverses the unchanged complete Results codec");
        receiver.Apply(decoded!); ResultsClock.Frame++; receiver.Tick();
        ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
        return decoded!;
    }

    static void RuntimeNoiseState(Camera camera)
    {
        footerProof = true; FFSNetwork.IsClient = false;
        int previousMask = camera.cullingMask;
        const int authorLayer = 28, observerLayer = 29;
        var events = new GameObject("RuntimeNoiseEvents", typeof(EventSystem));
        var author = Make(camera, "RuntimeNoiseAuthor"); RemoveRows(author); WireScroll(author, 16);
        NativeOptions(author, false, () => { }, () => { });
        author.Manager.statsPanel.SetActive(false); author.Manager.buttonsContainer.SetActive(false);
        author.Ready.gameObject.SetActive(false); author.Ready.readyUpToggleState = EReadyUpToggleStates.Quests;
        Layer(author.Canvas, authorLayer); camera.cullingMask = 1 << authorLayer;
        Select(author); var source = new ScenarioResultsPresentation();
        ResultsClock.Now += 1f; ResultsClock.Frame++;
        var heading = State(331, 1, ResultsClock.Now); source.Capture(heading);
        var headingPixels = Image(camera, "runtime-noise-heading-author.png");
        var observer = Make(camera, "RuntimeNoiseObserver"); RemoveRows(observer); WireScroll(observer, 16);
        NativeOptions(observer, false, () => { }, () => { });
        observer.Manager.statsPanel.SetActive(false); observer.Manager.buttonsContainer.SetActive(false);
        observer.Ready.gameObject.SetActive(false); observer.Ready.readyUpToggleState = EReadyUpToggleStates.Quests;
        Layer(observer.Canvas, observerLayer); camera.cullingMask = 1 << observerLayer;
        Select(observer); FFSNetwork.IsClient = true;
        var receiver = new ScenarioResultsPresentation();
        var assets = (TownServiceAssets)typeof(ScenarioResultsPresentation).GetField("_assets",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver)!;
        var rootImage = observer.Root.GetComponent<Image>(); var originalRootSprite = rootImage.sprite;
        var alternativeTexture = NativeMaterials.OtherRootTexture(originalRootSprite.texture);
        Check(!alternativeTexture.GetRawTextureData().SequenceEqual(originalRootSprite.texture.GetRawTextureData()),
            "the real alternate native root backdrop has a genuinely different original pixel payload");
        // Original sprites904/1120 have identical name/rect/pivot/border/UV geometry
        // but point at native textures62/282. Preseed the other actual payload so
        // discovery order cannot accidentally make an unproven SpriteKey work.
        var alternativeSprite = Sprite.Create(alternativeTexture, originalRootSprite.rect,
            originalRootSprite.pivot / originalRootSprite.rect.size, originalRootSprite.pixelsPerUnit,
            0, SpriteMeshType.FullRect, originalRootSprite.border);
        alternativeSprite.name = originalRootSprite.name; assets.Key(alternativeSprite);
        DeliverResults(receiver, heading);
        Check(observer.Panel.ExtraRenderRoots.Count == 1,
            "cold Results observer retains its exact native root backdrop despite conflicting original discovery");
        var commonImage = observer.Panel.ExtraRenderRoots.Single().GetChild(0).GetComponent<Image>();
        Check(commonImage.sprite == originalRootSprite && commonImage.sprite.texture == originalRootSprite.texture,
            "the common Results root binds the original904 sprite rather than the colliding1120 alternative");
        CheckPixels(headingPixels, Image(camera, "runtime-noise-heading-observer.png"),
            "the initial runtime-noise heading has matching original source and observer pixels");

        var noise = NativeMaterials.NamedTexture("noise_01");
        var duplicateNoise = NativeMaterials.DuplicateOriginal(noise);
        noise.Apply(false, true); duplicateNoise.Apply(false, true);
        Check(!noise.isReadable && !duplicateNoise.isReadable,
            "the reported runtime noise dependency remains non-readable during actual capture resolution and rendering");
        typeof(TownServiceAssets).GetField("_nextScan", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(assets, 0f);
        assets.Scan();

        camera.cullingMask = 1 << authorLayer;
        Select(author); FFSNetwork.IsClient = false;
        author.Manager.statsPanel.SetActive(true); author.Manager.buttonsContainer.SetActive(true);
        author.Manager.returnToMapOption!.gameObject.SetActive(true);
        author.Manager.retryOption!.gameObject.SetActive(true);
        // Native UI effect initialization assigns a new instance of its original
        // button-state material. Gameplay/bootstrap is a declared boundary; the
        // actual original material, shader ABI and dependency pixels execute.
        var target = author.Manager.returnToMapOption!.GetComponentsInChildren<Image>(true).First();
        var runtime = new Material(NativeMaterials.ReportedNoiseMaterial); target.material = runtime;
        Canvas.ForceUpdateCanvases(); ScenarioResultsReadyPark.Tick();
        ResultsClock.Now += .3f; ResultsClock.Frame++;
        var full = State(331, 2, ResultsClock.Now); source.Capture(full);
        string dependency = NativeMaterials.Descriptor(noise);
        Check(full.Parts.SelectMany(p => p.Nodes).SelectMany(n => n.Values.Values).Any(v => v.Text.Contains(dependency)),
            "the actual runtime GUI button state requires the exact noise_01 descriptor from the paired hardware log");
        var sourcePixels = Image(camera, "runtime-noise-full-author.png");
        camera.cullingMask = 1 << observerLayer;
        Select(observer); FFSNetwork.IsClient = true;
        var basis = DeliverResults(receiver, full);
        var displayed = (ScenarioResultsState?)typeof(ScenarioResultsPresentation).GetField("_displayed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver);
        string failure = (string)typeof(ScenarioResultsPresentation).GetField("_lastFailure",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver)!;
        Check(displayed?.Revision == full.Revision,
            "runtime GUI noise dependency does not freeze the persistent observer after Victory; failure: " + failure);
        CheckPixels(sourcePixels, Image(camera, "runtime-noise-full-observer.png"),
            "runtime native button material and complete statistics replace Victory with matching final observer pixels");

        var scroll = author.Root.GetComponentInChildren<ScrollRect>(true); scroll.scrollSensitivity = 300;
        for (int step = 0; step < 2; step++)
        {
            camera.cullingMask = 1 << authorLayer;
            Select(author); FFSNetwork.IsClient = false;
            ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) }, ExecuteEvents.scrollHandler);
            Canvas.ForceUpdateCanvases(); ScenarioResultsReadyPark.Tick();
            ResultsClock.Now += .3f; ResultsClock.Frame++;
            var state = State(331, (ulong)(3 + step), ResultsClock.Now); source.Capture(state);
            var delta = ScenarioResultsDelta.Create(basis, state);
            Check(delta != null, "both runtime-native scroll changes retain an explicitly based sparse native update");
            var packet = ScenarioResultsCodec.Write(delta!);
            Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var sparse) && sparse != null,
                "both runtime-native scroll updates cross the unchanged wire codec");
            var expanded = ScenarioResultsDelta.Expand(basis, sparse!);
            Check(expanded != null && ScenarioResultsCodec.Write(expanded).SequenceEqual(ScenarioResultsCodec.Write(state)),
                "the runtime-native scroll delta retains every original GUI material and widget property");
            var expected = Image(camera, "runtime-noise-scroll-" + step + "-author.png");
            camera.cullingMask = 1 << observerLayer;
            Select(observer); FFSNetwork.IsClient = true; DeliverResults(receiver, expanded!);
            displayed = (ScenarioResultsState?)typeof(ScenarioResultsPresentation).GetField("_displayed",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver);
            Check(displayed?.Revision == state.Revision,
                "host and delivered guest scroll update the full observer after runtime material initialization and late texture discovery");
            CheckPixels(expected, Image(camera, "runtime-noise-scroll-" + step + "-observer.png"),
                "both runtime-native scrolls preserve complete matching observer statistics and controls");
        }
        receiver.Reset(); source.Reset(); author.Manager.myWindow.Hide(); Select(author); ScenarioResultsReadyPark.Tick();
        Object.DestroyImmediate(author.Canvas.gameObject); Object.DestroyImmediate(observer.Canvas.gameObject);
        Object.DestroyImmediate(runtime); Object.DestroyImmediate(alternativeSprite); Object.DestroyImmediate(events);
        camera.cullingMask = previousMask; footerProof = false;
    }

    static void LateRootSpriteBinding(Camera camera)
    {
        int previousMask = camera.cullingMask;
        const int authorLayer = 28, observerLayer = 29;
        footerProof = true; FFSNetwork.IsClient = false;
        var author = Make(camera, "LateRootAuthor"); RemoveRows(author);
        author.Manager.statsPanel.SetActive(false); author.Manager.buttonsContainer.SetActive(false);
        author.Ready.gameObject.SetActive(false); author.Ready.readyUpToggleState = EReadyUpToggleStates.Quests;
        var authorImage = author.Root.GetComponent<Image>(); var authorSprite = authorImage.sprite; authorImage.sprite = null;
        Layer(author.Canvas, authorLayer); camera.cullingMask = 1 << authorLayer;
        Select(author); var source = new ScenarioResultsPresentation(); ResultsClock.Now += 1f; ResultsClock.Frame++;
        var initial = State(442, 1, ResultsClock.Now); source.Capture(initial);
        var before = Image(camera, "late-root-initial-author.png");

        var observer = Make(camera, "LateRootObserver"); RemoveRows(observer);
        observer.Manager.statsPanel.SetActive(false); observer.Manager.buttonsContainer.SetActive(false);
        observer.Ready.gameObject.SetActive(false); observer.Ready.readyUpToggleState = EReadyUpToggleStates.Quests;
        var nativeImage = observer.Root.GetComponent<Image>(); var nativeSprite = nativeImage.sprite; nativeImage.sprite = null;
        Layer(observer.Canvas, observerLayer); camera.cullingMask = 1 << observerLayer;
        Select(observer); FFSNetwork.IsClient = true; var receiver = new ScenarioResultsPresentation(); DeliverResults(receiver, initial);
        Check(observer.Panel.ExtraRenderRoots.Count == 1,
            "a heading can be accepted before its original root sprite has finished binding");
        var picture = observer.Panel.ExtraRenderRoots.Single();
        CheckPixels(before, Image(camera, "late-root-initial-observer.png"),
            "the early sprite-pending native heading retains complete matching observer pixels");

        authorImage.sprite = authorSprite; nativeImage.sprite = nativeSprite;
        camera.cullingMask = 1 << authorLayer; Select(author); FFSNetwork.IsClient = false;
        ResultsClock.Now += .3f; ResultsClock.Frame++;
        var updated = State(442, 2, ResultsClock.Now); source.Capture(updated);
        Check(updated.Parts.Length == initial.Parts.Length && updated.Parts.All(part =>
            initial.Parts.Any(old => old.Id == part.Id && old.Address == part.Address && old.Structure == part.Structure)),
            "a late native root sprite changes values without rebuilding the native results hierarchy");
        var expected = Image(camera, "late-root-bound-author.png");
        camera.cullingMask = 1 << observerLayer; Select(observer); FFSNetwork.IsClient = true; DeliverResults(receiver, updated);
        var displayed = (ScenarioResultsState?)typeof(ScenarioResultsPresentation).GetField("_displayed",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver);
        Check(displayed?.Revision == updated.Revision && observer.Panel.ExtraRenderRoots.Single() == picture,
            "late original root sprite resolves on the existing observer without a structural rebuild");
        Check(picture.GetChild(0).GetComponent<Image>().sprite == nativeSprite,
            "late root refresh binds the exact current original widget sprite");
        CheckPixels(expected, Image(camera, "late-root-bound-observer.png"),
            "the late-bound root sprite reaches matching final pixels on the same persistent picture");
        receiver.Reset(); source.Reset(); author.Manager.myWindow.Hide(); Select(author); ScenarioResultsReadyPark.Tick();
        Object.DestroyImmediate(author.Canvas.gameObject); Object.DestroyImmediate(observer.Canvas.gameObject);
        camera.cullingMask = previousMask; footerProof = false;
    }
}
