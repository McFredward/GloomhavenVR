"""An unfinished native graph must never round to a completed scope."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-wizard"))
from state import stage_progress


class ProgressPercentageAccuracyTests(unittest.TestCase):
    def test_one_unfinished_backend_result_is_below100(self):
        value = stage_progress("unity-native-build-plan", 10069, 10070, "actions")
        self.assertLess(value["percent"], 100)
        self.assertGreater(value["percent"], 99.99)
        self.assertEqual((value["done"], value["total"]), (10069, 10070))

    def test_small_observed_advances_are_not_rounded_away(self):
        values = [stage_progress("unity-native-build-plan", count, 10070, "actions")["percent"]
                  for count in (5000, 5001, 5002)]
        self.assertLess(values[0], values[1])
        self.assertLess(values[1], values[2])

    def test_floating_point_near_completion_does_not_claim100(self):
        self.assertLess(stage_progress("content", 2 ** 53 - 1, 2 ** 53, "bytes")["percent"], 100)

    def test_finished_counts_are_exactly100(self):
        for count in (0, 1, 10070, 2 ** 53):
            with self.subTest(count=count):
                self.assertEqual(stage_progress("content", count, count, "actions")["percent"], 100)

    def test_unknown_total_remains_unknown(self):
        self.assertIsNone(stage_progress("content", 1, None, "actions")["percent"])


if __name__ == "__main__":
    unittest.main()
