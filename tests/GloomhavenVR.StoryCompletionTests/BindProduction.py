"""Compile lifecycle decisions from their production sites, without Unity rendering stubs."""
from pathlib import Path
import re
import sys

root, output = map(Path, sys.argv[1:])
modal = root / 'src/GloomhavenVR/WorldUI/Modal'
tick = (modal / 'ModalFallback.4.Tick.cs').read_text()
close = (modal / 'ModalFallback.7.Close.cs').read_text()
classification = (modal / 'MandatoryDecision.cs').read_text()
conversion = (modal / 'ModalFallback.8.Convert.cs').read_text()
assert conversion.count('RearmFreshStoryAnchor(window);') == 1
assert re.search(r'RearmFreshStoryAnchor\(window\);\s*if \(PlaceAtHmd\(', conversion), \
    'fresh-story anchor rearm must precede initial placement'
assert 'RearmFreshStoryAnchor' not in (modal / 'ModalFallback.9.Spawn.cs').read_text(), \
    'final-fit replay must not rearm a held or peer-placed story'

# Keep the full boolean expression, including the precedence of stale membership
# and the terminal-story exclusion. Reimplementing that expression missed this bug.
match = re.search(r'bool stillOpen = (.*?);', tick, re.S)
assert match is not None, 'production stillOpen expression missing'
expression = match.group(1)
start = close.index('private static void ReassertStickyVisible(WindowPanel wp)')
body = close.index('{', start) + 1
end = close.index('bool fought = false;', body)
guard = close[body:end]
poll_start = close.index('    private static void AddPollWindow(UIWindow? window)')
poll_end = close.index('\n    }', poll_start) + len('\n    }')
poll = close[poll_start:poll_end]
convert_start = tick.index('UIWindow window = OpenWindows[i];', tick.index('EnterPhase(PhaseConvert);'))
convert_body = tick.index('\n', convert_start)
convert_end = tick.index('if (RendersInsideFloatedAncestor(window))', convert_body)
convert = tick[convert_body:convert_end]
assert convert_end < tick.index('if (IsConverted(window)', convert_end), 'completion check must precede conversion admission'

# Classification is separately source-bound: an enum test alone would pass even
# if its real caller stopped using the native-window identity.
assert re.search(r'if \(NativeStoryWindow.IsStory\(window\)\)\s*\{[^}]*return MandatoryDecisionTerm.NativeStory;',
                 classification, re.S), 'native story identity must feed mandatory classification'
spent_start = tick.index('private static bool StickinessSpentByAnsweredDecision(')
spent_end = tick.index('\n    }', spent_start)
spent = tick[spent_start:spent_end]
assert 'MandatoryDecisionTerms.IdentifiesTheWindow(' in spent and 'ClassifyMandatoryDecision(wp.Window, out why)' in spent

fixture = '''using System.Collections.Generic;
using UnityEngine.UI;
namespace GloomhavenVR.WorldUI;
internal static partial class ModalFallback
{
    private static readonly List<UIWindow> OpenWindows = new();
    internal static readonly List<WindowPanel> Converted = new();
    internal static readonly HashSet<SharedWindowKind> SharedAnchorSpent = new();
    internal static readonly Dictionary<SharedWindowKind, string> SharedAnchorSpentWhy = new();
    internal static void FreshStoryAnchor(UIWindow window) => RearmFreshStoryAnchor(window);
    private static bool ContainsWindow(List<UIWindow> windows, UIWindow window) => windows.Contains(window);
    private static bool ScriptedLevelMessageActive(UIWindow window) => false;
    private static class FloatRefusalTable
    {
        internal static bool Refuses(UIWindow window) => false;
    }
    POLL
    internal static bool Poll(UIWindow? window)
    {
        OpenWindows.Clear();
        AddPollWindow(window);
        return OpenWindows.Count > 0;
    }
    internal static bool WouldConvert(UIWindow window)
    {
        CONVERT
        return true;
    }
    internal static bool Keep(WindowPanel wp, bool tracked)
    {
        OpenWindows.Clear();
        if (tracked) OpenWindows.Add(wp.Window);
        bool alive = true, refused = false, answeredMandatory = false, selectionGone = false;
        // Deliberately force answeredMandatory false: the stronger final-story
        // exclusion must also defeat stale OpenWindows and delayed classification.
        return EXPRESSION;
    }
    internal static bool WouldReassert(WindowPanel wp)
    {
        GUARD
        return true;
    }
}
'''.replace('EXPRESSION', expression).replace('GUARD', guard.replace('return;', 'return false;'))
fixture = fixture.replace('POLL', poll).replace('CONVERT', convert.replace('continue;', 'return false;'))
(output / 'Lifecycle.fixture').write_text(fixture)
(output / 'NativeStoryWindow.fixture').write_text((modal / 'NativeStoryWindow.cs').read_text())
(output / 'MandatoryDecisionTerm.fixture').write_text((modal / 'MandatoryDecisionTerm.cs').read_text())
(output / 'StorySpawnAnchor.fixture').write_text((modal / 'ModalFallback.StorySpawnAnchor.cs').read_text())
print('Story completion: native helper, release expression, visibility guard, poll, conversion guard and mandatory classification bound to production.')
