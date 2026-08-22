// Golden vectors for the 3D MAP ROOM record (extension record 20, NetProtocol.ExtIdMapRoom) and
// the MAP ROOM'S SHARED WINDOWS record (extension record 21, NetProtocol.ExtIdSharedWindow), plus
// the pure key hash they both ride on.
//
// The expected bytes below are DERIVED FROM THE RECORDS' SPECS (the doc comments at
// NetProtocol.ExtIdMapRoom and NetProtocol.ExtIdSharedWindow), never pasted from the writer's
// output — a golden vector produced by calling the code it tests proves nothing. The FNV-1a values
// were computed independently from the algorithm's own definition (offset basis 2166136261, prime
// 16777619, byte-at-a-time XOR-then-multiply) and not read out of this codebase. Every float used
// is exactly representable in float32 (halves and quarters), so the position bytes are written by
// hand.
//
// WHAT THESE TWO RECORDS ARE FOR (user request 2026-08-22): "Multiplayer für die 3D-Map: a) Welche
// Map angezeigt wird … b) Welche Quest gerade angeklickt ist … c) Die mouseover Infotafeln … d) …
// das erscheinende Fenster … genauso wie die darauffolgendene Story-Fenster … Klickt einer weiter
// ist es für alle im 3d-Worldmap-Raum weitergeklickt worden."
//
// FIVE PROPERTIES MATTER MORE THAN LATENCY, and each has its own case below:
//
//   ABSENCE IS THE PRE-RECORD PICTURE   a client with the 3D map off must emit ModBuild 221's bytes
//   AN OLD PEER DEGRADES TO "ABSENT"    never to a clamped extreme
//   AN UNKNOWN KIND IS STEPPED OVER     the entries behind it still apply
//   AN UNKNOWN FRAME DROPS THE POSE     and KEEPS the page — the fail-closed direction
//   A TRUNCATED ENTRY KEEPS ITS ELDERS  a lying count can never damage what was already read

using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class MapSyncVectors
{
    internal static void Run(Harness t)
    {
        var ext = new byte[PresenceSerializer.MaxSize];
        int m;

        // ---- the key hash ---------------------------------------------------------------------
        // FNV-1a/32 over the campaign YML's own string ids, folded so 0 stays reserved for
        // "nothing". Values computed from the ALGORITHM, independently of this codebase.

        t.Case("m1. HashMapKey is FNV-1a/32 with 0 reserved");
        t.Equal(0x3B627C19u, NetProtocol.HashMapKey("Gloomhaven"),
                "FNV-1a of \"Gloomhaven\" — the identity the GAME itself sends in a LocationToken "
                + "for GameActionType.SelectQuest, hashed rather than sent as a string");
        t.Equal(0x9442487Fu, NetProtocol.HashMapKey("BlackBarrow"), "…and of a second location id");
        t.Equal(0xC40BF6CCu, NetProtocol.HashMapKey("A"), "…and of a single character");
        t.Equal(0u, NetProtocol.HashMapKey(null),
                "a null id is 'nothing' — 0 is the reserved value and never a real key");
        t.Equal(0u, NetProtocol.HashMapKey(""), "…and so is an empty one");
        t.True(NetProtocol.HashMapKey("Gloomhaven") != NetProtocol.HashMapKey("BlackBarrow"),
               "two different locations key differently, which is the whole point of a MATCH GATE: "
               + "the only thing a receiver does with a key it cannot resolve is nothing");

        // ---- record 20: the 3D map room ---------------------------------------------------------

        t.Case("m2. extras, map-room record: in the room, on the WORLD map, hovering a location");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = (byte)(NetProtocol.MapRoomInRoomBit
                                  | NetProtocol.MapRoomSurfaceKnownBit
                                  | NetProtocol.MapRoomPickValidBit),
            MapRoomSurfaceStamp = 3,
            MapRoomPickKey = 0x3B627C19u,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type (MsgExtras)
            80               // flags: FlagPileBrowse ('a BLOCK follows') only
            00               // handCardCount
            80 00            // byte A: extension tail; byte B: browse count 0 -> no fan
            01               // tail: 1 record
            14 06            // id 20 (3D map room), len 6
            15               // flags: inRoom(1) | surfaceKnown(4) | pickValid(16) = 0x15
            03               // surfaceStamp 3 -- an EDGE marker, not a level
            19 7C 62 3B      // pickKey FNV-1a('Gloomhaven') = 0x3B627C19, little-endian
            "), ext, m, "the map-room record is [id 20][len 6][flags][surfaceStamp][u32 pickKey LE], "
                        + "written behind record 19 in append order");
        t.Equal(19, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 6) = 19 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p2), "and it parses");
        t.True(p2.HasMapRoom, "the map-room record is delivered");
        t.Equal((byte)0x15, p2.MapRoomFlags, "the three set bits survive");
        t.True((p2.MapRoomFlags & NetProtocol.MapRoomSurfaceCityBit) == 0,
               "surfaceKnown WITHOUT surfaceCity is the WORLD map — the two bits are not one");
        t.True((p2.MapRoomFlags & NetProtocol.MapRoomPickStagedBit) == 0,
               "and the pick is a HOVER, not a staged selection");
        t.Equal((byte)3, p2.MapRoomSurfaceStamp, "the surface stamp survives");
        t.Equal(0x3B627C19u, p2.MapRoomPickKey, "…and the pick key, unvalidated by design");

        t.Case("m3. extras, map-room record: the HOST, on the CITY map, with a STAGED selection");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = (byte)(NetProtocol.MapRoomInRoomBit
                                  | NetProtocol.MapRoomHostBit
                                  | NetProtocol.MapRoomSurfaceKnownBit
                                  | NetProtocol.MapRoomSurfaceCityBit
                                  | NetProtocol.MapRoomPickValidBit
                                  | NetProtocol.MapRoomPickStagedBit),
            MapRoomSurfaceStamp = 255,
            MapRoomPickKey = 0x9442487Fu,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 06            // id 20, len 6
            3F               // every defined bit: 1|2|4|8|16|32 = 0x3F = MapRoomDefinedMask
            FF               // surfaceStamp 255 -- it WRAPS; 255 is as ordinary as 3
            7F 48 42 94      // pickKey FNV-1a('BlackBarrow') = 0x9442487F LE
            "), ext, m, "the staged bit rides the same flags byte as the hover: 'which icon is lit' "
                        + "is one fact with a qualifier, never a second channel for the COMMITTED "
                        + "selection (which the game itself already sends, host-authoritatively)");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p3), "and it parses");
        t.Equal(NetProtocol.MapRoomDefinedMask, p3.MapRoomFlags, "all six defined bits survive");

        t.Case("m4. map-room record: undefined flag bits are masked off on READ");
        byte[] wildFlags = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 06            // id 20, len 6
            C1               // flags: bits 6 and 7 (0xC0, undefined) plus inRoom (0x01)
            00               // surfaceStamp 0
            00 00 00 00      // pickKey 0 = 'pointing at nothing'
            ");
        t.True(PresenceSerializer.TryRead(wildFlags, wildFlags.Length, out PresenceState p4),
               "it parses");
        t.Equal(NetProtocol.MapRoomInRoomBit, p4.MapRoomFlags,
                "every bit outside MapRoomDefinedMask is masked off, so a future sender's extra bit "
                + "can never light a meaning here");
        t.Equal(0u, p4.MapRoomPickKey, "and key 0 stays 'nothing'");

        t.Case("m5. map-room record: a peer NOT in the room is delivered, not dropped");
        byte[] notInRoom = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 06            // id 20, len 6
            00               // flags: nothing set at all -- the in-room bit is CLEAR
            07               // surfaceStamp 7
            19 7C 62 3B      // and a pick key that must therefore say nothing
            ");
        t.True(PresenceSerializer.TryRead(notInRoom, notInRoom.Length, out PresenceState p5),
               "it parses");
        t.True(p5.HasMapRoom,
               "the record is DELIVERED rather than dropped — the consumer reads a clear in-room "
               + "bit as 'this peer is not at the table', the same picture ABSENCE gives, so the "
               + "two agree by construction instead of one of them being a special case");
        t.Equal((byte)0, p5.MapRoomFlags, "with no bits set");

        t.Case("m6. map-room record: NOTHING to say writes no record at all");
        m = PresenceSerializer.Write(new PresenceState(), ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            00               // flags: no block at all
            00               // handCardCount
            "), ext, m, "a client whose 3D map room is not standing — every scenario session, every "
                        + "flat-map session and every player who has the 3D map switched off — "
                        + "emits the exact bytes ModBuild 221 emitted. The record cannot open the "
                        + "extension tail on its own, and that is the hard property this feature's "
                        + "whole opt-in scoping rests on");

        // ---- record 21: the shared map windows --------------------------------------------------

        t.Case("m7. extras, shared-window record: ONE map-story entry, no pose");
        var oneStory = new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];
        oneStory[0].Kind = NetProtocol.SharedWindowKindMapStory;
        oneStory[0].Flags = NetProtocol.SharedOpenBit;
        oneStory[0].Page = 2;
        oneStory[0].PageCount = 5;
        oneStory[0].ContentKey = 0xDEADBEEFu;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSharedWindow = true,
            SharedWindowCount = 1,
            SharedWindowEntries = oneStory,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 09            // id 21 (shared map windows), len 9 = count byte + one 8-byte entry
            01               // n = 1 entry
            01               // kind 1 = MapStory
            01               // flags: SharedOpenBit only
            02               // page 2 (ABSOLUTE, 0-based)
            05               // pageCount 5 (diagnostic; the receiver clamps to its OWN list)
            EF BE AD DE      // contentKey 0xDEADBEEF LE -- the dialog's content hash
            "), ext, m, "each entry is [kind][flags][page][pageCount][u32 contentKey LE] and the "
                        + "record's LENGTH is computed from the entries' own flags, which is what "
                        + "lets an unknown kind be stepped over by a reader that has never seen it");
        t.Equal(22, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 9) = 22 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p7), "and it parses");
        t.True(p7.HasSharedWindow, "the record is delivered");
        t.Equal(1, p7.SharedWindowCount, "with one entry");
        t.Equal(NetProtocol.SharedWindowKindMapStory, p7.SharedWindowEntries![0].Kind,
                "of kind MapStory");
        t.Equal((byte)2, p7.SharedWindowEntries[0].Page, "the absolute page survives");
        t.Equal(0xDEADBEEFu, p7.SharedWindowEntries[0].ContentKey, "…and the content key");
        t.Equal(NetProtocol.StorySizeDefaultCode, p7.SharedWindowEntries[0].SizeCode,
                "with no pose block the size reads as the authored 1.00x, not as garbage");

        t.Case("m8. extras, shared-window record: TWO entries, BOTH carrying a pose — the worst case");
        var both = new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];
        both[0].Kind = NetProtocol.SharedWindowKindMapStory;
        both[0].Flags = (byte)(NetProtocol.SharedOpenBit | NetProtocol.SharedPoseBit);
        both[0].Page = 0;
        both[0].PageCount = 3;
        both[0].ContentKey = 0x00010203u;
        both[0].PoseStamp = 7;
        both[0].SizeCode = 150;
        both[0].Frame = NetProtocol.SharedFrameParchment;
        both[0].Pose = new RigPose
        {
            Position = new Vector3(0.5f, 0.25f, -1.5f),
            Rotation = Quaternion.identity,
        };
        both[1].Kind = NetProtocol.SharedWindowKindQuestConfirm;
        both[1].Flags = (byte)(NetProtocol.SharedOpenBit | NetProtocol.SharedPoseBit);
        both[1].Page = NetProtocol.StoryPageNone;
        both[1].PageCount = 0;
        both[1].ContentKey = 0xC40BF6CCu;
        both[1].PoseStamp = 1;
        both[1].SizeCode = 100;
        both[1].Frame = NetProtocol.SharedFrameSeatAnchor;
        both[1].Pose = new RigPose
        {
            Position = new Vector3(-0.25f, 0f, 0.75f),
            Rotation = Quaternion.identity,
        };
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSharedWindow = true,
            SharedWindowCount = 2,
            SharedWindowEntries = both,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 3F            // id 21, len 0x3F = 63 = 1 + 2 x 31, the record's stated worst case
            02               // n = 2 entries
            01               // entry 1 kind 1 = MapStory
            03               // flags: open | pose
            00               // page 0
            03               // pageCount 3
            03 02 01 00      // contentKey 0x00010203 LE
            07               // poseStamp 7 (bumped once per COMPLETED local move)
            96               // sizeCode 150 = the user's 1.50x grab factor
            01               // frame 1 = PARCHMENT-LOCAL real metres
            00 00 00 3F      // pos.x  0.5   (float32 LE)
            00 00 80 3E      // pos.y  0.25
            00 00 C0 BF      // pos.z -1.5
            00 00            // rot.x 0
            00 00            // rot.y 0
            00 00            // rot.z 0
            FF 7F            // rot.w 32767 (identity, quantized at QuatScale)
            02               // entry 2 kind 2 = QuestConfirm
            03               // flags: open | pose
            FF               // page 0xFF -- the quest window HAS no pages, and says so
            00               // pageCount 0
            CC F6 0B C4      // contentKey FNV-1a('A') = 0xC40BF6CC LE
            01               // poseStamp 1
            64               // sizeCode 100 = the authored 1.00x
            00               // frame 0 = record 19's seat-anchor real metres
            00 00 80 BE      // pos.x -0.25
            00 00 00 00      // pos.y  0
            00 00 40 3F      // pos.z  0.75
            00 00            // rot.x 0
            00 00            // rot.y 0
            00 00            // rot.z 0
            FF 7F            // rot.w 32767
            "), ext, m, "two entries can stand at once because MapStoryController.ShowImmediately "
                        + "calls ShowOtherGUI(!HideOtherGUI) and several map messages are raised "
                        + "with hideOtherUI: false; 63 payload bytes is the honest worst case and "
                        + "is what the MaxSize sum carries");
        t.Equal(76, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 63) = 76 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p8), "and it parses");
        t.Equal(2, p8.SharedWindowCount, "both entries land");
        t.Equal((byte)7, p8.SharedWindowEntries![0].PoseStamp, "the last-mover stamp survives");
        t.Equal((byte)150, p8.SharedWindowEntries[0].SizeCode, "…and the 1.50x size code");
        t.Equal(NetProtocol.SharedFrameParchment, p8.SharedWindowEntries[0].Frame,
                "…and the PARCHMENT frame byte, which is what makes the numbers mean 'the same "
                + "place' rather than 'the same offset from wherever I am looking'");
        t.Equal(0.5f, p8.SharedWindowEntries[0].Pose.Position.x, "…and an exactly representable x");
        t.Equal(0.25f, p8.SharedWindowEntries[0].Pose.Position.y, "…and y");
        t.Equal(-1.5f, p8.SharedWindowEntries[0].Pose.Position.z, "…and z");
        t.Equal(NetProtocol.SharedWindowKindQuestConfirm, p8.SharedWindowEntries[1].Kind,
                "the second entry is the quest window");
        t.Equal(NetProtocol.StoryPageNone, p8.SharedWindowEntries[1].Page,
                "which carries NO page — it is pose only, because its CONTENT is already synced by "
                + "the game's own host-authoritative SelectQuest action and a second channel for "
                + "that is forbidden");
        t.Equal(NetProtocol.SharedFrameSeatAnchor, p8.SharedWindowEntries[1].Frame,
                "and it names the OTHER frame, proving the byte is read per entry rather than "
                + "assumed per record");
        t.Equal(-0.25f, p8.SharedWindowEntries[1].Pose.Position.x, "with its own position");
        t.Equal(0.75f, p8.SharedWindowEntries[1].Pose.Position.z, "…on both axes that moved");

        t.Case("m9. shared-window record: the FINISHED entry — the one that clears the map halt");
        var finished = new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];
        finished[0].Kind = NetProtocol.SharedWindowKindMapStory;
        finished[0].Flags = NetProtocol.SharedFinishedBit;
        finished[0].Page = 4;
        finished[0].PageCount = 4;
        finished[0].ContentKey = 0x11223344u;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSharedWindow = true,
            SharedWindowCount = 1,
            SharedWindowEntries = finished,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 09            // id 21, len 9
            01               // n = 1
            01               // kind 1 = MapStory
            04               // flags: SharedFinishedBit ALONE -- the box is no longer open here
            04               // page 4 == pageCount: past the last 0-based page
            04               // pageCount 4
            44 33 22 11      // contentKey 0x11223344 LE
            "), ext, m, "a finished announcement carries no open bit and no pose. For the Gloomhaven "
                        + "intro it is not a comfort feature: MapChoreographer.CheckCampaignIntro "
                        + "HALTS the sender's ActionProcessor while that box stands, so this is the "
                        + "statement that lets a party whose peer walked away carry on");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p9), "and it parses");
        t.True((p9.SharedWindowEntries![0].Flags & NetProtocol.SharedFinishedBit) != 0,
               "the finished bit survives");
        t.True((p9.SharedWindowEntries[0].Flags & NetProtocol.SharedOpenBit) == 0,
               "and the open bit stays clear");

        t.Case("m10. shared-window record: an UNKNOWN KIND is stepped over, the entry behind it lands");
        byte[] unknownKind = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 11            // id 21, len 0x11 = 17 = count + two 8-byte entries
            02               // n = 2
            09               // kind 9 -- a kind THIS build has never heard of
            01               // flags: open (no pose, so the entry is 8 bytes)
            01               // page 1
            02               // pageCount 2
            AA BB CC DD      // some content key
            01               // kind 1 = MapStory, BEHIND the unknown one
            01               // flags: open
            03               // page 3
            06               // pageCount 6
            11 22 33 44      // contentKey 0x44332211 LE
            ");
        t.True(PresenceSerializer.TryRead(unknownKind, unknownKind.Length, out PresenceState p10),
               "it parses");
        t.Equal(2, p10.SharedWindowCount,
                "BOTH entries are kept by the parser — the unknown one is stepped over by its own "
                + "computed length rather than ending the walk, which is what makes a third kind "
                + "addable later without breaking this build");
        t.Equal((byte)9, p10.SharedWindowEntries![0].Kind, "the unknown kind is preserved verbatim");
        t.Equal(NetProtocol.SharedWindowKindMapStory, p10.SharedWindowEntries[1].Kind,
                "and the entry behind it is intact");
        t.Equal((byte)3, p10.SharedWindowEntries[1].Page, "with its page — the length walked right");

        t.Case("m11. shared-window record: an UNKNOWN FRAME drops the POSE and KEEPS the page");
        byte[] wildFrame = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 20            // id 21, len 0x20 = 32 = count + one 31-byte pose entry
            01               // n = 1
            01               // kind 1 = MapStory
            03               // flags: open | pose
            02               // page 2
            04               // pageCount 4
            01 00 00 00      // contentKey 1
            05               // poseStamp 5
            96               // sizeCode 150
            07               // frame 7 -- a frame this build cannot decode
            00 00 00 3F      // pos.x 0.5
            00 00 80 3E      // pos.y 0.25
            00 00 C0 BF      // pos.z -1.5
            00 00 00 00      // rot.x/y
            00 00 FF 7F      // rot.z, rot.w = identity
            ");
        t.True(PresenceSerializer.TryRead(wildFrame, wildFrame.Length, out PresenceState p11),
               "it parses");
        t.True(p11.HasSharedWindow, "the record is delivered");
        t.True((p11.SharedWindowEntries![0].Flags & NetProtocol.SharedPoseBit) == 0,
               "the pose CLAIM is cleared: a window placed in a frame this build cannot decode "
               + "would land somewhere nobody chose, and a window where you left it is always "
               + "better than a window in the wrong place");
        t.Equal((byte)2, p11.SharedWindowEntries[0].Page,
                "…and the PAGE survives, which is the half that can release a halted peer");
        t.Equal(NetProtocol.StorySizeDefaultCode, p11.SharedWindowEntries[0].SizeCode,
                "the size falls back to the authored 1.00x with the rest of the block");

        t.Case("m12. shared-window record: a pose claim the record is too short to hold");
        byte[] cutPose = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 09            // id 21, len 9 -- room for the 8-byte head and nothing more
            01               // n = 1
            01               // kind 1 = MapStory
            07               // flags: open | pose | finished -- but no pose can fit
            03               // page 3
            03               // pageCount 3
            AA BB CC DD      // contentKey 0xDDCCBBAA LE
            ");
        t.True(PresenceSerializer.TryRead(cutPose, cutPose.Length, out PresenceState p12),
               "it parses");
        t.True(p12.HasSharedWindow, "the record is DELIVERED, not dropped");
        t.True((p12.SharedWindowEntries![0].Flags & NetProtocol.SharedPoseBit) == 0,
               "the unsupportable pose claim is cleared — the receiver keeps its own placement");
        t.True((p12.SharedWindowEntries[0].Flags & NetProtocol.SharedFinishedBit) != 0,
               "…but the FINISHED bit still lands, which is the whole point: dropping the entry "
               + "would re-create exactly the stall this record exists to remove");
        t.Equal((byte)3, p12.SharedWindowEntries[0].Page, "and the page survives with it");

        t.Case("m13. shared-window record: a lying count is clamped, and a truncated entry keeps "
               + "the entries before it");
        byte[] liar = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 0B            // id 21, len 11 -- one whole entry plus two stray bytes
            FF               // n = 255 -- a lie; the cap is SharedWindowMaxEntries = 2
            01               // kind 1
            01               // flags: open
            00               // page 0
            02               // pageCount 2
            01 00 00 00      // contentKey 1
            01 01            // ...and a second entry that is 6 bytes short of a head
            ");
        t.True(PresenceSerializer.TryRead(liar, liar.Length, out PresenceState p13), "it parses");
        t.Equal(1, p13.SharedWindowCount,
                "the count is clamped to the cap AND the truncated entry ends the walk — what was "
                + "already read is kept, and nothing overran the record or bled into the next one");
        t.Equal((byte)0, p13.SharedWindowEntries![0].Page, "the intact entry is undamaged");

        t.Case("m14. shared-window record: a lying length cannot damage the record behind it");
        byte[] neighbour = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            02               // tail: 2 records
            15 09            // id 21, len 9 (honest)
            01               // n = 1
            01 01 01 02      // kind 1, flags open, page 1, pageCount 2
            07 00 00 00      // contentKey 7
            03 02 00 01      // id 3 (mod version) behind it, undamaged
            ");
        t.True(PresenceSerializer.TryRead(neighbour, neighbour.Length, out PresenceState p14),
               "it parses");
        t.True(p14.HasSharedWindow, "the shared-window record lands");
        t.True(p14.HasModVersion,
               "and the record BEHIND it is untouched — the length walked right");

        t.Case("m15. both new records ride ONE packet, record 20 before record 21");
        var story = new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];
        story[0].Kind = NetProtocol.SharedWindowKindMapStory;
        story[0].Flags = NetProtocol.SharedOpenBit;
        story[0].Page = 1;
        story[0].PageCount = 2;
        story[0].ContentKey = 0x000000FFu;
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = NetProtocol.MapRoomInRoomBit,
            MapRoomSurfaceStamp = 0,
            MapRoomPickKey = 0u,
            HasSharedWindow = true,
            SharedWindowCount = 1,
            SharedWindowEntries = story,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            02               // tail: 2 records
            14 06            // id 20 FIRST
            01 00            // flags: in room only; surfaceStamp 0
            00 00 00 00      // pickKey 0 = pointing at nothing
            15 09            // id 21 SECOND
            01               // n = 1
            01 01 01 02      // kind 1, flags open, page 1, pageCount 2
            FF 00 00 00      // contentKey 0xFF LE
            "), ext, m, "APPEND ORDER IS THE CONTRACT: every record is written behind every record "
                        + "that existed before it, so a reader that knows neither of these two steps "
                        + "over both by their own lengths and parses everything ahead of them "
                        + "exactly as it always did");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p15), "and it parses");
        t.True(p15.HasMapRoom && p15.HasSharedWindow, "both records are delivered");

        t.Case("m16. an OLD peer's packet simply has neither record");
        byte[] old = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            03 02 00 01      // id 3 (mod version): a record that predates both of these
            ");
        t.True(PresenceSerializer.TryRead(old, old.Length, out PresenceState p16), "it parses");
        t.True(!p16.HasMapRoom,
               "absence is a DEFINED state — 'this peer is not standing in a 3D map room' — which "
               + "is what every pre-record build and every player with the 3D map off transmits. "
               + "A peer on an older build therefore degrades to NOT PARTICIPATING and never to a "
               + "clamped extreme");
        t.True(!p16.HasSharedWindow, "…and has no shared map window either");

        t.Case("m17. shared-window record: NOTHING to say writes no record at all");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSharedWindow = true,
            SharedWindowCount = 0,
            SharedWindowEntries = null,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            00               // flags: no block at all
            00               // handCardCount
            "), ext, m, "an EMPTY entry set writes no record, so it cannot open the extension tail "
                        + "— the same idle-packet rule the wall-fade set and the decision state "
                        + "follow, and here it is what makes the 3D-map opt-in free for everyone "
                        + "who did not opt in");

        // ---- the sizing contract ----------------------------------------------------------------

        t.Case("m18. the record sizes are what MaxSize was raised for");
        t.Equal(6, NetProtocol.MapRoomRecordBytes,
                "record 20 is flags + surfaceStamp + a 4-byte key");
        t.Equal(8, NetProtocol.SharedWindowEntryMinBytes,
                "a shared-window entry head is kind + flags + page + pageCount + a 4-byte key");
        t.Equal(31, NetProtocol.SharedWindowEntryBytesWithPose,
                "…plus poseStamp + sizeCode + frame + the 20-byte shared pose");
        t.Equal(2, NetProtocol.SharedWindowMaxEntries,
                "and at most two shared windows can stand at once in the map room");
        t.Equal(63, NetProtocol.SharedWindowMaxRecordBytes,
                "so the worst-case payload is 1 + 2 x 31 = 63");
        t.Equal(1800, PresenceSerializer.MaxSize,
                "MaxSize was raised 1600 -> 1800 in the same commit: the worst case went "
                + "1357 -> 1430 (+8 for record 20 with its TLV header, +65 for record 21 with "
                + "its), and the margin at 1600 would have been 170 — thinner than the largest "
                + "single record (257, board tuning) and therefore a violation of the rule that "
                + "every new record keeps a margin of at least one record's worth");
        t.True(PresenceSerializer.MaxSize - 1430 >= 257,
               "the restored margin (370) is larger than the largest single record, which is the "
               + "stated rule and the reason the two earlier raises happened");
    }
}
