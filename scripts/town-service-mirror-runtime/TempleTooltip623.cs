#if TEMPLE_TOOLTIP623
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI
{
    // The game HUD supplies these original references. Its gameplay controllers
    // are explicit fixture boundaries, with the native serialized field names.
    internal class UIAttackModifier<T> : MonoBehaviour where T : MonoBehaviour
    {
        [SerializeField] internal Transform countersHolder = null!;
        [SerializeField] internal T attackModCounterPrefab = null!;
    }
    internal sealed class UIPerkAttackModifier : UIAttackModifier<UIPerkAttackModifierCounter> { }
    internal sealed class UIPerkAttackModifierCounter : MonoBehaviour
    {
        internal static int Awakes, Enables;
        private void Awake() => Awakes++;
        private void OnEnable() => Enables++;
    }
    internal sealed class UITempleSlotTooltip : MonoBehaviour
    {
        [SerializeField] internal UIPerkAttackModifier modifier = null!;
    }
    internal static partial class LazyTemplateProbe
    {
        internal static Transform TempleSource = null!;
        private static Transform? Original(string key) => key == "temple.tooltip" ? TempleSource : null;
        internal static IReadOnlyList<Part> PreparedTemple(string key)
        { EnsureTempleTooltip(key); return Parts(key); }
    }
}

public static partial class MirrorProgram
{
    public static IEnumerator RunTempleTooltip623(string output, string variant, string suite)
    {
        _output = Path.Combine(output, variant + "-evidence"); Directory.CreateDirectory(_output); _assertions = 0;
        TownServiceMirror.Shutdown();
        GameObject bank = Go("Inactive original temple provenance"); bank.SetActive(false);
        LazyTemplateProbe.Open(bank);
        try
        {
            Transform source = Go("Native temple hint", bank.transform).transform;
            source.gameObject.AddComponent<CanvasGroup>();
            var tooltip = source.gameObject.AddComponent<UITempleSlotTooltip>();
            Transform content = Go("Content", source).transform;
            content.gameObject.AddComponent<Image>().color = Color.cyan;
            tooltip.modifier = Go("Original perk modifier", source).AddComponent<UIPerkAttackModifier>();
            Transform holder = Go("Native retained counter holder", tooltip.modifier.transform).transform;
            tooltip.modifier.countersHolder = holder;
            GameObject prefab = Go("Original counter prefab", bank.transform);
            prefab.AddComponent<Image>().color = Color.yellow;
            tooltip.modifier.attackModCounterPrefab = prefab.AddComponent<UIPerkAttackModifierCounter>();
            Go("Original counter value", prefab.transform).AddComponent<Image>().color = Color.magenta;
            Go("Original counter overlay", prefab.transform).AddComponent<Image>().color = Color.blue;
            LazyTemplateProbe.TempleSource = source;
            for (int quantity = 0; quantity <= 7; quantity++)
            {
                // Same native NormalizePool topology: it retains earlier counter
                // copies when quantity later decreases. No templated UI is invented.
                if (quantity == 7)
                {
                    // A serialized native prefab reference may point to a child
                    // of the same hint. Clearing the temporary pool must not
                    // destroy the source from which its replacement is copied.
                    prefab.transform.SetParent(holder, false);
                }
                else if (quantity > 0)
                {
                    GameObject original = Object.Instantiate(prefab, holder, false);
                    original.name = prefab.name;
                }
                string key = LazyTemplateProbe.TempleTooltipKey(source);
                var parts = LazyTemplateProbe.PreparedTemple(key);
                Check(parts.Count == 1, "native temple counter topology has one exact original partition");
                Transform frozen = parts[0].Original;
                using (var owner = new TownServiceBinding(source))
                using (var copy = new TownServiceBinding(frozen))
                {
                    Check(owner.Structure == copy.Structure && owner.Nodes.Length == copy.Nodes.Length,
                        "native temple tooltip retains the exact owner's dynamic counter topology");
                    for (int i = 0; i < owner.Bindings.Length; i++)
                        Check(owner.Bindings[i] == copy.Bindings[i], "native temple counter bindings preserve original child order");
                }
                Check(LazyTemplateProbe.PreparedTemple(key)[0].Original == frozen,
                    "repeated native temple tooltip reuses its inactive immutable original");
                Check(frozen.GetComponentsInChildren<UIPerkAttackModifierCounter>(true).Length == 0
                    && frozen.GetComponentsInChildren<UITempleSlotTooltip>(true).Length == 0,
                    "native temple tooltip copy is neutralized before any observer activation");
                Check(UIPerkAttackModifierCounter.Awakes == 0 && UIPerkAttackModifierCounter.Enables == 0,
                    "preparing native temple counter shape never executes native awake or enable callbacks");
            }
            string retained = LazyTemplateProbe.TempleTooltipKey(source);
            for (int i = 2; i < holder.childCount; i++) holder.GetChild(i).gameObject.SetActive(false);
            Check(LazyTemplateProbe.TempleTooltipKey(source) == retained,
                "native retained inactive counters keep their original immutable template basis");
            bool malformed = false;
            try { LazyTemplateProbe.PreparedTemple("temple.tooltip.counters.065"); }
            catch (InvalidDataException) { malformed = true; }
            Check(malformed, "native temple tooltip rejects noncanonical counter identities");
            File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
        }
        finally
        {
            LazyTemplateProbe.Close(); TownServiceMirror.Shutdown();
            foreach (GameObject obj in Objects) if (obj != null) Object.DestroyImmediate(obj);
            Objects.Clear();
        }
        yield break;
    }
}
#endif
