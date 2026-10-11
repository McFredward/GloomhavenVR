#!/usr/bin/env python3
"""Execute production transaction, rollback and native restoration, with binding checks."""
from pathlib import Path
import re, sys
root, output = map(Path, sys.argv[1:3])
core = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs').read_text()
life = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs').read_text()
adopt = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs').read_text()
hide = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.6.Hide.cs').read_text()
modal = (root / 'src/GloomhavenVR/WorldUI/Modal/ModalFallback.8.Convert.cs').read_text()
def code(source):
    return re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/', lambda m: m[0] if m[0].startswith('"') else '', source)
def method(source, signature):
    start = source.index(signature)
    return source[start:source.index('\n    }', start) + len('\n    }')]
c = code(method(core, '    internal static ConvertedPanel? Convert('))
ordered = ['using var transaction = new ConversionTransaction(panel);', 'var hostGo = new GameObject(', 'panel.HostGo = hostGo;', 'var hostRect = hostGo.AddComponent<RectTransform>();', 'panel.HostRect = hostRect;', 'var hostCanvas = hostGo.AddComponent<Canvas>();', 'panel.HostCanvas = hostCanvas;', 'panel.HostRaycaster = raycaster;', 'target.SetParent(hostRect, worldPositionStays: false);', 'AdoptNestedCanvases(panel);', 'SetPanelRenderVisible(panel, visible: false);', 'Active.Add(panel);', 'DiagnoseModal(panel, force: true);', 'transaction.Complete();', 'return panel;']
positions = [c.index(token) for token in ordered]
assert positions == sorted(positions), 'Conversion transaction ownership must precede native mutation and diagnostics must precede commit'
assert c.index('HasFailedConversion(target)') < c.index('using var transaction'), 'Pending rollback must block new adoption'
a = code(method(adopt, '    private static bool AdoptCanvas('))
assert a.index('panel.AdoptedCanvases.Add(record);') < a.index('nested.overrideSorting = true;                    '), 'Canvas original state must be recorded before adoption writes'
assert a.index('panel.AdoptedCanvases[recordIndex] = record;') < a.index('UguiPokeSurfaces.RegisterNested'), 'New raycaster must be owned before registration'
layer_sweep = code(method(adopt, '    private static void ApplyModLayer('))
assert 'if (initial || !IsRelayered(panel, t))' in layer_sweep, 'Fresh panel conversion must not rescan its growing restoration ledger'
assert 'panel.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = t.gameObject.layer });' in layer_sweep
h = code(method(hide, '    private static void HideTree('))
assert h.index('panel.HiddenCanvases.Add(c);') < h.index('c.enabled = false;'), 'Canvas hide must record original visibility before write'
assert h.index('panel.HiddenRenderers.Add(r);') < h.index('r.enabled = false;'), 'Renderer hide must record original visibility before write'
m = code(method(modal, '    private static bool TryConvertWindow('))
assert m.index('ConvertedPanel? panel = null;') < m.index('try\n'), 'Modal must retain panel ownership outside try'
assert m.index('GrabbableModal? grab = null;') < m.index('try\n'), 'Modal must retain grab ownership outside try'
assert m.index('WindowPanel? wp = null;') < m.index('try\n'), 'Modal must retain enrollment ownership outside try'
assert m.index('grab = new GrabbableModal();') < m.index('grab.Build(panel, extraScale, name);'), 'Partial chrome must be owned before build'
assert 'var wp = new WindowPanel' not in m and 'wp = new WindowPanel' in m
start = life.index('    private sealed class ConversionTransaction')
end = life.index('    /// <summary>Restore the panel into its original', start)
helpers = life[start:end]
release = method(life, '    internal static void Release(ConvertedPanel? panel)')
assert code(release).index('RestoreAdoptedCameras("release")') < code(release).index('panel.AdoptedCanvases.Clear();'), 'Failed camera restore must retain original snapshots'
tick = code(method(life, '    internal static void Tick()'))
assert tick.index('ServiceFailedConversions();') < tick.index('ServiceDeferredHosts();'), 'Native home retry must precede deferred host detachment'
assert 'ServiceFailedConversions();' in code(method(life, '    internal static void ReleaseAll()'))
catch_start = m.index('        catch (Exception ex)')
# Bind the production failure handler, not the adjacent conversion-timing finally block.
# The latter samples local stopwatch variables from TryConvertWindow's opening path and has
# no ownership or rollback effect in this fixture's deliberately shortened outer method.
timing_finally = m.find('\n        finally', catch_start)
catch = m[catch_start:timing_finally if timing_finally >= 0 else len(m)]
assert 'if (wp != null) Converted.Remove(wp);' in catch
assert 'CanvasConversion.RollbackFailedConversion(panel, grab);' in catch
# Run the actual outer failure handler against partially completed attachment phases.
outer = '''internal static bool AttachForTest(UIWindow window, ConvertedPanel panel, GrabbableModal? grab, WindowPanel? wp, Action work)
{
string name = window.name;
try { work(); return true; }
''' + (catch if timing_finally >= 0 else catch[:-len('\n    }')]) + '\n}\n'
output.parent.mkdir(parents=True, exist_ok=True)
layer_methods = method(adopt, '    private static void ApplyModLayer(') + '\n' \
    + method(adopt, '    private static bool IsForeignRenderSubtree(') + '\n' \
    + method(adopt, '    private static bool IsRelayered(')
output.write_text('using Object = UnityEngine.Object;\nusing System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class CanvasConversion\n{\n' + helpers + release + '\n' + method(life, '    private static void DestroyHostSafely(') + '\n' + layer_methods + '\n    internal static void SweepForTest(ConvertedPanel panel, bool initial) => ApplyModLayer(panel, initial);\n}\ninternal static partial class ModalFallback\n{\n' + outer + '\n}\n')
print('Rollback production binding: ownership, adoption, reveal, enrollment and retry placement verified.')
