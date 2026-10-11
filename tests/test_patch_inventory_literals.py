"""Exercise the real patch scanner against unmatched brackets inside C# literals."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('patch_inventory_literals', ROOT / 'scripts/patch-inventory.py')
INVENTORY = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = INVENTORY
SPEC.loader.exec_module(INVENTORY)


class LiteralTests(unittest.TestCase):
    def test_real_arch_source_has_balanced_syntax(self):
        declarations = INVENTORY.scan_file(ROOT / 'src/GloomhavenVR/Core/WallFade/WallSegmentFade.ArchMounted.cs')
        self.assertTrue(any(d.name == 'NativeArchName' for d in declarations))

    def test_literals_do_not_hide_later_patch_declarations(self):
        source = '''class Helpers {
            string Name(string name) { return name.LastIndexOf(" (").ToString(); }
            string Text = "[unclosed ; {";
            string Verbatim = @"unbalanced ] ""quote"" (";
            char Bracket = ')';
        }
        [HarmonyPatch(typeof(Target), "Run")]
        class Patch { [HarmonyPrefix] static void Prefix() { } }
        '''
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'Example.cs'
            path.write_text(source)
            declarations = INVENTORY.scan_file(path)
        patch = next(d for d in declarations if d.name == 'Patch')
        prefix = next(d for d in declarations if d.name == 'Prefix')
        self.assertEqual(patch.attrs, ['[HarmonyPatch(typeof(Target), "Run")]'])
        self.assertEqual(prefix.outer, ('Patch',))
        self.assertEqual(prefix.attrs, ['[HarmonyPrefix]'])

    def test_balancing_still_rejects_actual_missing_bracket(self):
        with self.assertRaises(INVENTORY.ParseError):
            INVENTORY._matching('(call(" (")', 0, '(', ')')

    def test_escaped_quotes_and_character_literals(self):
        source = r'''(call("escaped \" (", '('), next())'''
        self.assertEqual(INVENTORY._matching(source, 0, '(', ')'), len(source))

    def test_unclosed_literal_still_fails(self):
        with self.assertRaises(INVENTORY.ParseError):
            INVENTORY._matching('(call("missing)', 0, '(', ')')

    def test_interpolation_walks_nested_quoted_expressions(self):
        source = '''(call($"{{literal}} {Describe("GAME-LIVE(the overlay's population)")} {(!ok ? " (another player's character)" : "")}"))'''
        self.assertEqual(INVENTORY._matching(source, 0, '(', ')'), len(source))

    def test_interpolation_ignores_expression_comments(self):
        source = '''(call($"{(ok ? "yes"
             // the player's remaining reason ( [ {
             : /* unmatched ) } ' */ "no")}"))'''
        self.assertEqual(INVENTORY._matching(source, 0, '(', ')'), len(source))

    def test_both_verbatim_interpolation_prefixes(self):
        for prefix in ('$@', '@$'):
            source = '(call(' + prefix + '"literal ""quote"" ( {Count(" (", 1)}"))'
            self.assertEqual(INVENTORY._matching(source, 0, '(', ')'), len(source))

    def test_comments_after_interpolated_literals_are_really_stripped(self):
        source = 'class A { void Run() { Log($"{Describe("the\'s value")}"); // the\'s comment (\n } }'
        stripped = INVENTORY.strip_comments(source)
        self.assertEqual(len(source), len(stripped))
        self.assertNotIn("comment", stripped)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'Nested.cs'
            path.write_text(source)
            self.assertTrue(any(d.name == 'Run' for d in INVENTORY.scan_file(path)))


if __name__ == '__main__':
    unittest.main()
