// Golden vectors for the STORY WINDOW SYNC record (extension record 19,
// NetProtocol.ExtIdStorySync) plus the pure arbitration it rides on.
//
// The expected bytes below are DERIVED FROM THE RECORD'S SPEC (the doc comment at
// NetProtocol.ExtIdStorySync), never pasted from the writer's output — a golden vector produced by
// calling the code it tests proves nothing. Every float used is exactly representable in float32
// (halves and quarters), so the position bytes can be written by hand.
//
// The record exists because a peer who forgot to click the scenario's story dialog through locked a
// whole 3-player session (measured in that session's logs: the host dismissed its box after 1.4 s,
// peer 1 held it 1085.5 s, peer 2 1463.9 s). Three properties therefore matter more than latency,
// and each has its own case below:
//
//   IDEMPOTENCE  applying the same state twice equals applying it once
//   ORDERING     applying an OLDER state is ignored, never applied backwards
//   NO SKIPPING  two players clicking at once must land on ONE page, not two

using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class StoryVectors
{
    internal static void Run(Harness t)
    {
        var ext = new byte[PresenceSerializer.MaxSize];
        int m;

        // ---- the wire ------------------------------------------------------------------------

        t.Case("s1. extras, story record without a pose (nobody has moved the window)");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasStorySync = true,
            StoryFlags = NetProtocol.StoryOpenBit,
            StoryPage = 2,
            StoryPageCount = 5,
            StoryKey = 0xDEADBEEFu,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type (MsgExtras)
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            13 07            // id 19 (story sync), len 7 = the pose-less form
            01               // flags: StoryOpenBit only
            02               // page 2 (ABSOLUTE, 0-based)
            05               // pageCount 5 (diagnostic; the receiver clamps to its OWN list)
            EF BE AD DE      // storyKey 0xDEADBEEF, little-endian
            "), ext, m, "the story record is [id 19][len][flags][page][pageCount][u32 key LE], "
                        + "written LAST, behind every existing record, in append order");
        t.Equal(20, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 7) = 20 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState s1), "and it parses");
        t.True(s1.HasStorySync, "the story record is delivered");
        t.Equal((byte)NetProtocol.StoryOpenBit, s1.StoryFlags, "the sender's box is open");
        t.Equal((byte)2, s1.StoryPage, "the absolute page survives");
        t.Equal((byte)5, s1.StoryPageCount, "…and the sender's own page count");
        t.Equal(0xDEADBEEFu, s1.StoryKey, "…and the dialog identity hash, unvalidated by design");
        t.Equal(NetProtocol.StorySizeDefaultCode, s1.StorySizeCode,
                "with no pose block the size reads as the authored 1.00x, not as garbage");

        t.Case("s2. extras, story record WITH the pose block (a user dragged the window)");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasStorySync = true,
            StoryFlags = (byte)(NetProtocol.StoryOpenBit | NetProtocol.StoryPoseBit),
            StoryPage = 0,
            StoryPageCount = 3,
            StoryKey = 0x00010203u,
            StoryPoseStamp = 7,
            StorySizeCode = 150,
            StoryPose = new RigPose
            {
                Position = new Vector3(0.5f, 0.25f, -1.5f),
                Rotation = Quaternion.identity,
            },
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0
            01               // tail: 1 record
            13 1D            // id 19, len 29 = the pose-carrying form
            03               // flags: open | pose
            00               // page 0
            03               // pageCount 3
            03 02 01 00      // storyKey 0x00010203, little-endian
            07               // poseStamp 7 (bumped once per COMPLETED local move)
            96               // sizeCode 150 = the user's 1.50x grab factor
            00 00 00 3F      // pos.x  0.5   (float32 LE)
            00 00 80 3E      // pos.y  0.25
            00 00 C0 BF      // pos.z -1.5   -- SEAT-ANCHOR-LOCAL REAL METRES, not a world point
            00 00            // rot.x 0
            00 00            // rot.y 0
            00 00            // rot.z 0
            FF 7F            // rot.w 32767 (identity, quantized at QuatScale)
            "), ext, m, "the pose block rides BEHIND the 7-byte head as [poseStamp][sizeCode][pose 20], "
                        + "using the same WritePoseShared encoding every other pose on this wire uses");
        t.Equal(42, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 29) = 42 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState s2), "and it parses");
        t.Equal((byte)7, s2.StoryPoseStamp, "the last-mover stamp survives");
        t.Equal((byte)150, s2.StorySizeCode, "…and the 1.50x size code");
        t.Equal(0.5f, s2.StoryPose.Position.x, "…and an exactly representable x");
        t.Equal(0.25f, s2.StoryPose.Position.y, "…and y");
        t.Equal(-1.5f, s2.StoryPose.Position.z, "…and z");
        t.True(Quaternion.Angle(Quaternion.identity, s2.StoryPose.Rotation) < 0.5f,
               "…and the rotation, inside the quantizer's documented ~0.006 rad worst case");

        t.Case("s3. extras, the FINISHED record — the one that removes the lock");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasStorySync = true,
            StoryFlags = NetProtocol.StoryFinishedBit,
            StoryPage = 4,
            StoryPageCount = 4,
            StoryKey = 0x11223344u,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            13 07            // id 19, len 7
            04               // flags: StoryFinishedBit ALONE -- the box is no longer open here
            04               // page 4 == pageCount: past the last 0-based page
            04               // pageCount 4
            44 33 22 11      // storyKey 0x11223344 LE
            "), ext, m, "a finished announcement carries NO open bit and no pose: it is a statement "
                        + "about a dialog this sender has left, and it is the only record whose "
                        + "delivery unlocks a peer who walked away");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState s3), "and it parses");
        t.True((s3.StoryFlags & NetProtocol.StoryFinishedBit) != 0, "the finished bit survives");
        t.True((s3.StoryFlags & NetProtocol.StoryOpenBit) == 0, "and the open bit stays clear");

        t.Case("s4. story record: NOTHING to say writes no record at all");
        m = PresenceSerializer.Write(new PresenceState(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            00               // flags: no block at all
            00               // handCardCount
            "), ext, m, "a client with no narrative on screen emits the exact bytes build 156 "
                        + "emitted — the record cannot open the extension tail on its own");

        t.Case("s5. story record: undefined flag bits are masked, a wild page becomes 'none'");
        byte[] wild = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            13 07            // id 19, len 7
            F9               // flags with every reserved bit (0xF8) set, plus open (0x01)
            FF               // page 0xFF -- the reserved 'none' value
            00               // pageCount 0 = unknown
            00 00 00 00      // storyKey 0 (a sender with no dialog to name)
            ");
        t.True(PresenceSerializer.TryRead(wild, wild.Length, out PresenceState s5), "it parses");
        t.Equal((byte)NetProtocol.StoryOpenBit, s5.StoryFlags,
                "every bit outside StoryDefinedMask is masked off, so a future sender's extra bit "
                + "can never light a meaning here");
        t.Equal(NetProtocol.StoryPageNone, s5.StoryPage, "and 0xFF stays 'no page'");

        t.Case("s6. story record: a pose bit with a record too short to hold one drops the CLAIM, "
               + "never the record");
        byte[] cut = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            13 07            // id 19, len 7 -- but the flags claim a 29-byte pose form
            06               // flags: pose | finished
            03               // page 3
            03               // pageCount 3
            AA BB CC DD      // storyKey 0xDDCCBBAA LE
            ");
        t.True(PresenceSerializer.TryRead(cut, cut.Length, out PresenceState s6), "it parses");
        t.True(s6.HasStorySync, "the record is DELIVERED, not dropped");
        t.True((s6.StoryFlags & NetProtocol.StoryPoseBit) == 0,
               "the unsupportable pose claim is cleared — the receiver keeps its own placement");
        t.True((s6.StoryFlags & NetProtocol.StoryFinishedBit) != 0,
               "…but the FINISHED bit still lands, which is the whole point: dropping the record "
               + "whole would have re-created the deadlock the feature exists to remove");
        t.Equal((byte)3, s6.StoryPage, "and the page survives with it");

        t.Case("s7. story record: a truncated tail is abandoned and nothing throws");
        byte[] truncated = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            13 07            // id 19 claims 7 payload bytes...
            01 02            // ...but only 2 arrive
            ");
        t.True(PresenceSerializer.TryRead(truncated, truncated.Length, out PresenceState s7),
               "the packet still parses (the tail is abandoned, never trusted)");
        t.True(!s7.HasStorySync, "and nothing of the half-record is believed");

        t.Case("s8. story record: an OLD peer's packet simply has no story record");
        byte[] old = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            03 02 00 01      // id 3 (mod version): a record that predates story sync
            ");
        t.True(PresenceSerializer.TryRead(old, old.Length, out PresenceState s8), "it parses");
        t.True(!s8.HasStorySync,
               "absence is a DEFINED state — 'this peer has no story to sync' — and it is exactly "
               + "what every pre-record build transmits, so a mixed room degrades to the local-only "
               + "dialog every build before this one had");

        t.Case("s9. story record: a lying length cannot damage the record behind it");
        byte[] liar = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            02               // tail: 2 records
            13 07            // id 19, len 7 (honest)
            01 01 02         // flags open, page 1, pageCount 2
            01 00 00 00      // storyKey 1
            03 02 00 01      // id 3 (mod version) behind it, undamaged
            ");
        t.True(PresenceSerializer.TryRead(liar, liar.Length, out PresenceState s9), "it parses");
        t.True(s9.HasStorySync, "the story record lands");
        t.True(s9.HasModVersion, "and the record BEHIND it is untouched — the length walked right");

        // ---- the size quantizer ---------------------------------------------------------------

        t.Case("s10. story size code: the grab handle's own window, and nothing outside it");
        t.Equal((byte)100, NetProtocol.EncodeStorySize(1f), "the authored size is code 100");
        t.Equal((byte)15, NetProtocol.EncodeStorySize(0.15f),
                "PanelGrabHandle.MinScale = 0.15x is the floor");
        t.Equal((byte)200, NetProtocol.EncodeStorySize(2f),
                "PanelGrabHandle.MaxScale = 2.00x is the ceiling");
        t.Equal((byte)15, NetProtocol.EncodeStorySize(0.01f), "below the floor clamps UP to it");
        t.Equal((byte)200, NetProtocol.EncodeStorySize(9f), "above the ceiling clamps DOWN to it");
        t.Equal(NetProtocol.StorySizeDefaultCode, NetProtocol.EncodeStorySize(float.NaN),
                "NaN degrades to a READABLE window, never to a speck");
        t.Equal(1.5f, NetProtocol.DecodeStorySize(150), "150 decodes to 1.50x");
        t.Equal(1f, NetProtocol.DecodeStorySize(0),
                "a code outside the handle's range decodes to the authored size");
        t.Equal(1f, NetProtocol.DecodeStorySize(255), "…at both ends");
        t.Equal(1.23f, NetProtocol.DecodeStorySize(NetProtocol.EncodeStorySize(1.234f)),
                "the round trip is hundredths, i.e. a 1 % window step");

        t.Case("s11. story page code: one byte, one reserved 'none'");
        t.Equal((byte)0, NetProtocol.EncodeStoryPage(0), "page 0 is a real page");
        t.Equal(NetProtocol.StoryPageMax, NetProtocol.EncodeStoryPage(NetProtocol.StoryPageMax),
                "0xFE is the highest nameable page");
        t.Equal(NetProtocol.StoryPageNone, NetProtocol.EncodeStoryPage(-1),
                "a box that has not shown its first line yet is 'no page'");
        t.Equal(NetProtocol.StoryPageNone, NetProtocol.EncodeStoryPage(9999),
                "and anything past the byte is 'no page' rather than a wrapped lie");

        // ---- the arbitration -------------------------------------------------------------------
        // NetProtocol.ResolveStoryPage(localPage, localPageCount, remotePage, remoteFinished)
        // returns the page this client must move to, or -1 for "do nothing".

        t.Case("s12. arbitration: a peer ahead of us pulls us forward");
        t.Equal(3, NetProtocol.ResolveStoryPage(1, 5, 3, false),
                "we are on page 1, a peer is on 3 -> we go to 3");

        t.Case("s13. arbitration: IDEMPOTENCE — applying the same state twice equals once");
        int once = NetProtocol.ResolveStoryPage(1, 5, 3, false);
        t.Equal(3, once, "the first application moves us to 3");
        t.Equal(-1, NetProtocol.ResolveStoryPage(once, 5, 3, false),
                "the SECOND application of the very same state is a no-op — this is what makes the "
                + "5 Hz full-state re-send safe, and what makes a duplicated packet harmless");

        t.Case("s14. arbitration: ORDERING — an older state is ignored, never applied backwards");
        t.Equal(-1, NetProtocol.ResolveStoryPage(4, 6, 2, false),
                "we are on page 4 and a reordered packet claims 2 -> nothing happens; nobody is "
                + "ever dragged back through text they have already read");
        t.Equal(-1, NetProtocol.ResolveStoryPage(4, 6, 4, false), "an equal state is equally a no-op");
        t.Equal(-1, NetProtocol.ResolveStoryPage(0, 6, -1, false),
                "and a peer with no page at all (its box has not painted its first line) says nothing");

        t.Case("s15. arbitration: NO SKIPPING — two players clicking at once land on ONE page");
        // Both players are on page 2 and both click in the same frame. Each publishes the
        // ABSOLUTE page it moved to, which is 3 for both. The room therefore agrees on 3.
        int fromA = 3, fromB = 3;
        int furthest = fromA > fromB ? fromA : fromB;
        t.Equal(3, NetProtocol.ResolveStoryPage(2, 8, furthest, false),
                "an absolute page cannot double-count; an increment protocol would have produced 4 "
                + "here and eaten a page of somebody's text");

        t.Case("s16. arbitration: FINISHED outranks any page and is the terminal state");
        t.Equal(4, NetProtocol.ResolveStoryPage(0, 4, 0, true),
                "a peer that clicked THROUGH the end takes us to OUR OWN page count — the index one "
                + "past our last page, which is what drives the game's own Hide/close chain, and "
                + "that chain is what releases ActionProcessor.LockProcessingAction and sends this "
                + "client's own GameLoadedAndClientReady");
        t.Equal(-1, NetProtocol.ResolveStoryPage(4, 4, 0, true),
                "and having already run it, the same statement is a no-op (idempotent teardown)");

        t.Case("s17. arbitration: the LOCAL page count is the only bound that counts");
        t.Equal(3, NetProtocol.ResolveStoryPage(0, 3, 99, false),
                "a peer naming a page past our list is clamped to our own count, i.e. to 'close' — "
                + "never to an index into a list we do not have");
        t.Equal(-1, NetProtocol.ResolveStoryPage(0, 0, 2, true),
                "and a client with NO dialog is never driven at all, finished bit or not");
        t.Equal(-1, NetProtocol.ResolveStoryPage(-1, 0, 5, false),
                "including one whose box has not painted yet and holds no pages");
    }
}
