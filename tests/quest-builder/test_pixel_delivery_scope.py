"""Data-only pixel repairs retain B691 native producers, never runtime changes."""
from pathlib import Path
import sys
import unittest
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/quest-builder'))
import update_driver as driver
import preparation_identity as identity
import storage

class PixelDeliveryScopeTests(unittest.TestCase):
    def test_complete_reviewed_controller_bytes_and_both_pixel_revisions_keep_compiler_rows(self):
        current=storage.record_file(ROOT/'tools/quest-builder/update_driver.py','tools/quest-builder/update_driver.py')
        self.assertEqual(current,identity.INSTALLED_UPDATE_DELIVERY_ROWS[current["path"]][0])
        runtime={'path':'src/Runtime.cs','size':10,'sha256':'1'*64}
        before=[identity.REBASE_MEDIA_DELIVERY_ROWS[current["path"]][1],runtime,{'path':'tools/quest-builder/quest_card_pixels.py','size':10,'sha256':'2'*64}]
        after=[current,runtime,{'path':'tools/quest-builder/quest_card_pixels.py','size':99,'sha256':'3'*64}]
        self.assertEqual(driver._code_rows(before,normalizer=identity),driver._code_rows(after,normalizer=identity))
        self.assertEqual(driver._native_producer_rows(before),driver._native_producer_rows(after))
        # The older pixel-only correction remains independently qualified too.
        for left, right in ((identity.UPDATE_PIXEL_DELIVERY_PREVIOUS, identity.UPDATE_PIXEL_DELIVERY_FIXED),):
            self.assertEqual(driver._code_rows([left, runtime], normalizer=identity),
                             driver._code_rows([right, runtime], normalizer=identity))
        after[0]={**current,'sha256':'f'*64}
        self.assertNotEqual(driver._code_rows(before,normalizer=identity),driver._code_rows(after,normalizer=identity))
        after[0]=current;after[1]={**runtime,'sha256':'4'*64}
        self.assertNotEqual(driver._code_rows(before,normalizer=identity),driver._code_rows(after,normalizer=identity))

if __name__=='__main__':unittest.main()
