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
//
// AND SINCE THE SHARED-SELECTION ROUND, a sixth (user report 13: "Welches Icon ausgewählt ist wird
// nicht richtig synchronisiert. Es soll nur eine einzige Auswahl geben die global alle sehen"):
//
//   A SHORT RECORD 20 IS STILL TRUSTED  its first six bytes are the frozen minimum, and the
//                                       selection fields simply read as 0/0 — "does not
//                                       participate", never "that peer just deselected everything"

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
            14 10            // id 20 (3D map room), len 16
            15               // flags: inRoom(1) | surfaceKnown(4) | pickValid(16) = 0x15
            03               // surfaceStamp 3 -- an EDGE marker, not a level
            19 7C 62 3B      // pickKey FNV-1a('Gloomhaven') = 0x3B627C19, little-endian
            00               // selectStamp 0 -- nobody at this table has selected anything yet
            00 00 00 00      // selectKey 0 = NOTHING is selected
            00 00 00 00      // fanCharacterKey 0 = no map card fan open on this sender
            00               // gazeYaw 0, and the flags carry NO gaze bit: this sender has decided nothing
            "), ext, m, "the map-room record is [id 20][len 16][flags][surfaceStamp][u32 pickKey LE]"
                        + "[selectStamp][u32 selectKey LE][u32 fanCharacterKey LE][gazeYaw], written "
                        + "behind record 19 in append order. The LONG form is always written and "
                        + "readers require only the old 6-byte minimum — that asymmetry IS the "
                        + "additive contract, and it is why this record has now grown THREE times "
                        + "without a version bump");
        t.Equal(29, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 16) = 29 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p2), "and it parses");
        t.True(p2.HasMapRoom, "the map-room record is delivered");
        t.Equal((byte)0x15, p2.MapRoomFlags, "the three set bits survive");
        t.True((p2.MapRoomFlags & NetProtocol.MapRoomSurfaceCityBit) == 0,
               "surfaceKnown WITHOUT surfaceCity is the WORLD map — the two bits are not one");
        t.True((p2.MapRoomFlags & NetProtocol.MapRoomPickStagedBit) == 0,
               "and the pick is a HOVER, not a staged selection");
        t.Equal((byte)3, p2.MapRoomSurfaceStamp, "the surface stamp survives");
        t.Equal(0x3B627C19u, p2.MapRoomPickKey, "…and the pick key, unvalidated by design");
        t.Equal((byte)0, p2.MapRoomSelectStamp, "…and a selection stamp of 0");
        t.Equal(0u, p2.MapRoomSelectKey,
                "…with key 0, which is 'NOTHING is selected' and not 'no field here'. A stamp that "
                + "never changes never instructs anybody, so this record cannot deselect a thing");

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
            MapRoomSelectStamp = 9,
            MapRoomSelectKey = 0x9442487Fu,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 10            // id 20, len 16
            3F               // every defined bit: 1|2|4|8|16|32 = 0x3F = MapRoomDefinedMask
            FF               // surfaceStamp 255 -- it WRAPS; 255 is as ordinary as 3
            7F 48 42 94      // pickKey FNV-1a('BlackBarrow') = 0x9442487F LE
            09               // selectStamp 9 -- the ninth selection change made at that table
            7F 48 42 94      // selectKey: the SAME node, because the pick IS the staged selection
            00 00 00 00      // fanCharacterKey 0: this sender has no map card fan open
            00               // gazeYaw 0, and the flags carry NO gaze bit: this sender has decided nothing
            "), ext, m, "the staged bit rides the same flags byte as the hover, and the SELECTION "
                        + "rides its own stamp+key behind it. The two agree here because a staged "
                        + "pick is the selection; they disagree the moment that player hovers a "
                        + "second icon, which is the only thing the staged bit still says");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p3), "and it parses");
        t.Equal((byte)0x3F, p3.MapRoomFlags,
                "all six of the bits this vector sends survive — 0x3F, not MapRoomDefinedMask, "
                + "because the mask has since grown bit 6 (the shared gaze) and this sender does "
                + "not set it. Comparing against the MASK rather than against the bits actually "
                + "sent is how a vector stops testing the wire and starts testing a constant");
        t.Equal((byte)9, p3.MapRoomSelectStamp, "the selection stamp survives");
        t.Equal(0x9442487Fu, p3.MapRoomSelectKey, "…and the selection key");

        t.Case("m4. map-room record: undefined flag bits are masked off, and an unbacked gaze bit "
               + "with them");
        byte[] wildFlags = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 06            // id 20, len 6
            C1               // flags: bit 7 (undefined) + bit 6 (gaze, DEFINED but unbacked here,
                             //        because this record is 6 bytes and carries no gaze byte)
                             //        + inRoom (0x01)
            00               // surfaceStamp 0
            00 00 00 00      // pickKey 0 = 'pointing at nothing'
            ");
        t.True(PresenceSerializer.TryRead(wildFlags, wildFlags.Length, out PresenceState p4),
               "it parses");
        t.Equal(NetProtocol.MapRoomInRoomBit, p4.MapRoomFlags,
                "every bit outside MapRoomDefinedMask is masked off, AND the gaze bit is stripped "
                + "on a record too short to carry the byte it describes — so a future sender's "
                + "extra bit can never light a meaning here");
        t.Equal(0u, p4.MapRoomPickKey, "and key 0 stays 'nothing'");
        t.Equal((byte)0, p4.MapRoomSelectStamp,
                "THE SIX-BYTE FORM IS STILL TRUSTED — MapRoomRecordBytes is frozen at 6 — and its "
                + "missing selection fields read as 0/0, i.e. 'this peer does not participate in "
                + "the shared selection'. That is the whole additive contract in one assertion");
        t.Equal(0u, p4.MapRoomSelectKey, "…key included");

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

        t.Case("m8b. shared-window record: THREE entries, all with poses — the ModBuild 231 worst "
               + "case, and the ENCOUNTER's pose-only discipline pinned on the wire");
        var three = new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];
        three[0] = both[0];
        three[1] = both[1];
        three[2].Kind = NetProtocol.SharedWindowKindEncounter;
        three[2].Flags = (byte)(NetProtocol.SharedOpenBit | NetProtocol.SharedPoseBit);
        // NO page and NO finished bit, EVER, for this kind: the encounter's page advance is the
        // game's own GameActionType.ContinueRoadEvent (decompiled UIEventPanel.cs:606/610/724 ->
        // ClientContinueRoadEvent :869) and a second channel for it is forbidden. This vector is
        // where that discipline is pinned, because a doc paragraph saying so is what failed before.
        three[2].Page = NetProtocol.StoryPageNone;
        three[2].PageCount = 0;
        three[2].ContentKey = 0x0BADF00Du;
        three[2].PoseStamp = 5;
        three[2].SizeCode = 200;
        three[2].Frame = NetProtocol.SharedFrameParchment;
        three[2].Pose = new RigPose
        {
            Position = new Vector3(1f, -0.5f, 0.25f),
            Rotation = Quaternion.identity,
        };
        m = PresenceSerializer.Write(new PresenceState
        {
            HasSharedWindow = true,
            SharedWindowCount = 3,
            SharedWindowEntries = three,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            15 5E            // id 21, len 0x5E = 94 = 1 + 3 x 31, the raised worst case
            03               // n = 3 entries
            01               // entry 1 kind 1 = MapStory
            03               // flags: open | pose
            00               // page 0
            03               // pageCount 3
            03 02 01 00      // contentKey 0x00010203 LE
            07               // poseStamp 7
            96               // sizeCode 150
            01               // frame 1 = PARCHMENT-LOCAL
            00 00 00 3F      // pos.x  0.5
            00 00 80 3E      // pos.y  0.25
            00 00 C0 BF      // pos.z -1.5
            00 00 00 00      // rot.x/rot.y
            00 00 FF 7F      // rot.z / rot.w 32767 (identity)
            02               // entry 2 kind 2 = QuestConfirm
            03               // flags: open | pose
            FF               // page 0xFF -- no pages
            00               // pageCount 0
            CC F6 0B C4      // contentKey 0xC40BF6CC LE
            01               // poseStamp 1
            64               // sizeCode 100
            00               // frame 0 = seat anchor
            00 00 80 BE      // pos.x -0.25
            00 00 00 00      // pos.y  0
            00 00 40 3F      // pos.z  0.75
            00 00 00 00      // rot.x/rot.y
            00 00 FF 7F      // rot.z / rot.w
            03               // entry 3 kind 3 = ENCOUNTER ('Begegnung', UIEventPanel)
            03               // flags: open | pose -- and NEVER SharedFinishedBit
            FF               // page 0xFF -- the encounter carries NO page, by rule
            00               // pageCount 0
            0D F0 AD 0B      // contentKey 0x0BADF00D LE = FNV-1a of CRoadEvent.ID
            05               // poseStamp 5
            C8               // sizeCode 200 = the 2.00x ceiling PanelGrabHandle clamps to
            01               // frame 1 = PARCHMENT-LOCAL
            00 00 80 3F      // pos.x  1.0
            00 00 00 BF      // pos.y -0.5
            00 00 80 3E      // pos.z  0.25
            00 00 00 00      // rot.x/rot.y
            00 00 FF 7F      // rot.z / rot.w
            "), ext, m, "the encounter is addressed by its OWN kind byte and not by kind 1: kind 1 "
                        + "resolves Singleton<MapStoryController>, and a pose published under it "
                        + "would be applied to the map story box, which is a different window on a "
                        + "different canvas. 94 payload bytes is the raised worst case");
        t.Equal(107, m, "header 7 + count 1 + block 2 + tail 1 + (2 + 94) = 107 bytes");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p8b), "and it parses");
        t.Equal(3, p8b.SharedWindowCount, "all three entries land — the cap raise is end to end");
        t.Equal(NetProtocol.SharedWindowKindEncounter, p8b.SharedWindowEntries![2].Kind,
                "the third is the encounter");
        t.Equal(NetProtocol.StoryPageNone, p8b.SharedWindowEntries[2].Page,
                "which carries NO page — its content is the game's own ContinueRoadEvent action and "
                + "a mod record for it would be the forbidden second source of truth");
        t.True((p8b.SharedWindowEntries[2].Flags & NetProtocol.SharedFinishedBit) == 0,
               "…and no FINISHED bit either, for the same reason: nothing in the mod may advance or "
               + "terminate a road event");
        t.True((p8b.SharedWindowEntries[2].Flags & NetProtocol.SharedPoseBit) != 0,
               "what it DOES carry is the pose — the half the mod owns and the game has no opinion "
               + "about, which is the whole of 'blau' for this window");
        t.Equal((byte)5, p8b.SharedWindowEntries[2].PoseStamp, "with its own last-mover stamp");
        t.Equal((byte)200, p8b.SharedWindowEntries[2].SizeCode, "…and its own 2.00x size code");
        t.Equal(NetProtocol.SharedFrameParchment, p8b.SharedWindowEntries[2].Frame,
                "…in the parchment frame");
        t.Equal(1f, p8b.SharedWindowEntries[2].Pose.Position.x, "and an exactly representable x");
        t.Equal(-0.5f, p8b.SharedWindowEntries[2].Pose.Position.y, "…y");
        t.Equal(0.25f, p8b.SharedWindowEntries[2].Pose.Position.z, "…and z");
        t.Equal(0x0BADF00Du, p8b.SharedWindowEntries[2].ContentKey,
                "…and the event id hash a receiver compares its OWN event against before it moves "
                + "anything");
        t.Equal(NetProtocol.SharedWindowKindMapStory, p8b.SharedWindowEntries[0].Kind,
                "while the two older entries are untouched by the third — each entry's length is "
                + "computed from its own flags byte, which is what lets the walk step over a kind it "
                + "has never seen");
        t.Equal(NetProtocol.SharedWindowKindQuestConfirm, p8b.SharedWindowEntries[1].Kind,
                "…including the quest confirm in the middle");

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
            FF               // n = 255 -- a lie; the cap is SharedWindowMaxEntries = 3
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
            14 10            // id 20 FIRST
            01 00            // flags: in room only; surfaceStamp 0
            00 00 00 00      // pickKey 0 = pointing at nothing
            00               // selectStamp 0
            00 00 00 00      // selectKey 0 = nothing selected
            00 00 00 00      // fanCharacterKey 0 = no map card fan open
            00               // gazeYaw 0, and the flags carry NO gaze bit: this sender has decided nothing
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

        // ---- record 20's SELECTION EDGE (user report 13) -----------------------------------------

        t.Case("m19. map-room record: a SELECTION edge — the room's one selection, on the wire");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = (byte)(NetProtocol.MapRoomInRoomBit
                                  | NetProtocol.MapRoomSurfaceKnownBit),
            MapRoomSurfaceStamp = 1,
            MapRoomPickKey = 0u,
            MapRoomSelectStamp = 4,
            MapRoomSelectKey = 0x3B627C19u,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 10            // id 20, len 16
            05               // flags: inRoom(1) | surfaceKnown(4) -- pointing at NOTHING right now
            01               // surfaceStamp 1
            00 00 00 00      // pickKey 0: the pointer has left the icon...
            04               // selectStamp 4 -- ...but a selection was made, and this is its EDGE
            19 7C 62 3B      // selectKey FNV-1a('Gloomhaven') LE
            00 00 00 00      // fanCharacterKey 0: no map card fan open on this sender
            00               // gazeYaw 0, and the flags carry NO gaze bit: this sender has decided nothing
            "), ext, m, "the selection is INDEPENDENT of the pick: a player selects an icon and then "
                        + "points somewhere else, and the room must keep showing the selection. "
                        + "That is why it is its own stamp and its own key rather than the staged "
                        + "flag on the pick, which vanishes with the pointer");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p19), "and it parses");
        t.Equal((byte)4, p19.MapRoomSelectStamp, "the selection stamp survives");
        t.Equal(0x3B627C19u, p19.MapRoomSelectKey, "…and the selected node's key");
        t.Equal(0u, p19.MapRoomPickKey, "…while the pick stays empty, as sent");

        t.Case("m20. map-room record: a selection edge carrying key 0 is a DESELECTION");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = NetProtocol.MapRoomInRoomBit,
            MapRoomSelectStamp = 5,
            MapRoomSelectKey = 0u,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 10            // id 20, len 16
            01               // flags: in room only
            00               // surfaceStamp 0
            00 00 00 00      // pickKey 0
            05               // selectStamp 5 -- CHANGED, so this is an edge...
            00 00 00 00      // ...naming key 0, which is 'NOTHING is selected'
            00 00 00 00      // fanCharacterKey 0: no map card fan open
            00               // gazeYaw 0, and the flags carry NO gaze bit: this sender has decided nothing
            "), ext, m, "key 0 is a first-class value on this field and not an absence: an edge "
                        + "naming it is how 'es soll nur eine einzige Auswahl geben' holds in BOTH "
                        + "directions — one player closing the quest window clears it for everyone");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p20), "and it parses");
        t.Equal((byte)5, p20.MapRoomSelectStamp,
                "the stamp is what makes this an instruction; the key alone would be "
                + "indistinguishable from a peer who has simply never selected anything");
        t.Equal(0u, p20.MapRoomSelectKey, "and the key is the deselection");

        t.Case("m21. map-room record: an OLD SIX-BYTE record still parses, and instructs nothing");
        byte[] shortForm = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            02               // tail: 2 records
            14 06            // id 20 in the ORIGINAL six-byte form (a ModBuild 222..225 peer)
            15               // flags: inRoom | surfaceKnown | pickValid
            07               // surfaceStamp 7
            7F 48 42 94      // pickKey FNV-1a('BlackBarrow') LE
            03 02 00 01      // id 3 (mod version) BEHIND it, undamaged
            ");
        t.True(PresenceSerializer.TryRead(shortForm, shortForm.Length, out PresenceState p21),
               "it parses");
        t.True(p21.HasMapRoom, "the record is delivered — six bytes is the frozen minimum");
        t.Equal((byte)7, p21.MapRoomSurfaceStamp, "everything that build DID send survives");
        t.Equal(0x9442487Fu, p21.MapRoomPickKey, "…including its pick key");
        t.Equal((byte)0, p21.MapRoomSelectStamp,
                "and the fields it does not know read 0: a stamp that never changes is never an "
                + "edge, so that peer simply does not take part in the shared selection and can "
                + "never be misread as having just deselected everything");
        t.Equal(0u, p21.MapRoomSelectKey, "…key included");
        t.True(p21.HasModVersion,
               "and the record BEHIND the short one is untouched — the walk uses each record's own "
               + "length, which is what makes lengthening record 20 safe in the first place");

        // ---- record 20's SHARED GAZE YAW (user item 17: "im Sichtbereich der Spieler") -----------
        //
        // ONE BYTE, and the properties that matter are: it is the HOST's decision and nobody else's,
        // its meaning is carried by a FLAG and never by a magic value, and its absence is the fixed
        // table axis every build before it used. All three are cases below.

        t.Case("m22. the gaze quantisation is pure, total and exactly invertible to its step");
        t.Equal((byte)0, NetProtocol.EncodeMapRoomGazeYaw(0f), "yaw 0° is step 0");
        t.Equal((byte)64, NetProtocol.EncodeMapRoomGazeYaw(90f),
                "a quarter turn is a quarter of 256 steps — 90 / (360/256) = 64 exactly");
        t.Equal((byte)128, NetProtocol.EncodeMapRoomGazeYaw(180f), "…and a half turn is 128");
        t.Equal((byte)192, NetProtocol.EncodeMapRoomGazeYaw(270f), "…and three quarters is 192");
        t.Equal((byte)0, NetProtocol.EncodeMapRoomGazeYaw(360f),
                "a full turn is step 0 and NOT step 256: the wrap is arithmetic, so the byte can "
                + "never overflow into a different direction");
        t.Equal((byte)192, NetProtocol.EncodeMapRoomGazeYaw(-90f),
                "a NEGATIVE yaw is the same direction as its positive twin — wrapped BEFORE "
                + "quantising, because taking the modulo of a rounded step folds −0.1° onto 255");
        t.Equal((byte)0, NetProtocol.EncodeMapRoomGazeYaw(-0.1f),
                "…which is why −0.1° is step 0 and not step 255");
        t.Equal((byte)0, NetProtocol.EncodeMapRoomGazeYaw(float.NaN),
                "a non-finite input maps to 0 rather than throwing: the wire never carries a NaN, "
                + "and a receiver never has to ask whether the sender's trigonometry went wrong");
        t.Equal((byte)0, NetProtocol.EncodeMapRoomGazeYaw(float.PositiveInfinity), "…infinity too");
        t.Equal(0f, NetProtocol.DecodeMapRoomGazeYaw(0), "step 0 decodes to 0°");
        t.Equal(90f, NetProtocol.DecodeMapRoomGazeYaw(64), "…step 64 to 90°…");
        t.Equal(180f, NetProtocol.DecodeMapRoomGazeYaw(128), "…step 128 to 180°…");
        t.Equal(358.59375f, NetProtocol.DecodeMapRoomGazeYaw(255),
                "…and step 255 to 255 x 1.40625 = 358.59375°, so all 256 values are legal "
                + "directions and NONE of them is a sentinel — which is exactly why validity is "
                + "carried by MapRoomGazeValidBit and never by a reserved number");
        for (int step = 0; step < NetProtocol.MapRoomGazeYawSteps; step++)
        {
            if (NetProtocol.EncodeMapRoomGazeYaw(NetProtocol.DecodeMapRoomGazeYaw((byte)step))
                != (byte)step)
            {
                t.True(false, $"step {step} does not survive a decode/encode round trip, so two "
                              + "clients could read the host's decision as two different directions");
                break;
            }
        }
        t.True(true,
               "every one of the 256 steps survives decode -> encode unchanged, which is the "
               + "property that matters: the host encodes ONCE and every client decodes that same "
               + "byte, so 'the same place for everybody' is arithmetic and not a hope");

        t.Case("m23. record 20 carries the host's gaze decision as flags bit 6 plus one byte");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = (byte)(NetProtocol.MapRoomInRoomBit
                                  | NetProtocol.MapRoomHostBit
                                  | NetProtocol.MapRoomGazeValidBit),
            MapRoomGazeYaw = 64,
        }, ext);
        t.Wire(Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            01               // tail: 1 record
            14 10            // id 20, len 16
            43               // flags: inRoom(1) | host(2) | gazeValid(64) = 0x43
            00               // surfaceStamp 0
            00 00 00 00      // pickKey 0
            00               // selectStamp 0
            00 00 00 00      // selectKey 0
            00 00 00 00      // fanCharacterKey 0
            40               // gazeYaw step 64 = 90 degrees: the party is facing world +X
            "), ext, m, "the gaze yaw is the HOST'S DECISION about where the party is looking, and "
                        + "the bit beside it is what makes the byte mean anything. Every client "
                        + "anchors a shared window's SPAWN from this one number — a client that "
                        + "averaged the peer heads itself would be averaging its own interpolated "
                        + "copies at its own sampling age and would place a SHARED window somewhere "
                        + "nobody else did");
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p22), "and it parses");
        t.Equal((byte)64, p22.MapRoomGazeYaw, "the step survives the round trip");
        t.True((p22.MapRoomFlags & NetProtocol.MapRoomGazeValidBit) != 0,
               "…and so does the bit that says it means something");
        t.True((p22.MapRoomFlags & NetProtocol.MapRoomHostBit) != 0,
               "…beside the host bit, which is the only sender allowed to make this decision");

        t.Case("m24. a record with NO gaze bit says nothing, whatever byte it carries");
        m = PresenceSerializer.Write(new PresenceState
        {
            HasMapRoom = true,
            MapRoomFlags = NetProtocol.MapRoomInRoomBit,
            MapRoomGazeYaw = 200,
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, m, out PresenceState p23), "it parses");
        t.Equal((byte)200, p23.MapRoomGazeYaw,
                "the byte is delivered verbatim — it is not sanitised, because there is nothing to "
                + "sanitise: all 256 steps are legal directions");
        t.True((p23.MapRoomFlags & NetProtocol.MapRoomGazeValidBit) == 0,
               "but the bit is clear, and THAT is what the anchor tests. A non-host sender is "
               + "always in this state, and so is a host before its decision settles or after it "
               + "refused — and all three mean the same thing to a receiver: use the fixed table "
               + "axis, exactly as every build before this one did");

        t.Case("m25. an OLDER peer's 15-byte record still parses and carries no gaze");
        byte[] preGaze = Hex.Bytes(@"
            31 52 56 47      // magic
            03 01            // version, type
            80               // flags
            00               // handCardCount
            80 00            // byte A / byte B
            02               // tail: 2 records
            14 0F            // id 20 in the PREVIOUS fifteen-byte form (a pre-gaze peer)
            43               // flags: inRoom | host | AND bit 6 set, which that build cannot mean
            00               // surfaceStamp
            00 00 00 00      // pickKey
            00               // selectStamp
            00 00 00 00      // selectKey
            00 00 00 00      // fanCharacterKey
            03 02 00 01      // id 3 (mod version) BEHIND it, undamaged
            ");
        t.True(PresenceSerializer.TryRead(preGaze, preGaze.Length, out PresenceState p24),
               "it parses — 15 bytes is well over the frozen 6-byte minimum");
        t.Equal((byte)0, p24.MapRoomGazeYaw,
                "and the gaze byte reads 0 because the record is not long enough to hold one: the "
                + "field is read behind its OWN length test, so a shorter record is not a "
                + "truncated one, it is a record that does not have the field");
        t.True((p24.MapRoomFlags & NetProtocol.MapRoomGazeValidBit) == 0,
               "AND THE BIT IS STRIPPED, which is the case this vector exists for. The flags byte "
               + "lives in the FROZEN six-byte minimum, so a sender of any length can light bit 6 "
               + "in it while carrying no gaze byte — and a receiver that trusted the bit would "
               + "read the 0 left behind as a DECIDED yaw of 0° and seat every shared window along "
               + "world +Z for the whole room visit. The reader clears the bit where the field's "
               + "absence is known. Our own older writers mask it out anyway; 'never trust the "
               + "wire' is not satisfied by trusting our own past builds");
        t.True((p24.MapRoomFlags & NetProtocol.MapRoomHostBit) != 0,
               "…and only that bit: everything else the short record really did say survives");
        t.True(p24.HasModVersion,
               "…and the record BEHIND it is untouched, because the walk steps by the record's own "
               + "length and not by this build's idea of how long record 20 should be");

        // ---- the sizing contract ----------------------------------------------------------------

        t.Case("m18. the record sizes are what MaxSize was raised for");
        t.Equal(6, NetProtocol.MapRoomRecordBytes,
                "record 20's TRUSTED MINIMUM is flags + surfaceStamp + a 4-byte key, and it is "
                + "FROZEN at 6 for ever: raising it would turn every older peer from 'does not "
                + "participate' into 'record dropped'");
        t.Equal(11, NetProtocol.MapRoomRecordBytesWithSelect,
                "…the selection edge adds selectStamp + a 4-byte select key on top of it…");
        t.Equal(15, NetProtocol.MapRoomRecordBytesWithFan,
                "…then the map-fan character key, so a peer's card fan prints the loadout its owner "
                + "NAMED instead of one deduced from the hand's size…");
        t.Equal(16, NetProtocol.MapRoomRecordBytesWithGaze,
                "…and the FULL form this build writes adds ONE byte, the host's quantised decision "
                + "about where the party is looking, so a shared window spawns in front of the "
                + "players and in the same place for all of them. Each field is read behind its own "
                + "length test, which is why all four of these numbers can be true at once");
        t.Equal(256, NetProtocol.MapRoomGazeYawSteps,
                "the full turn in 256 steps = 1.40625° each, a worst-case rounding of 0.703°, "
                + "which is 9.8 mm of arc at the 0.80 m ring radius — an order of magnitude under "
                + "the 0.12 m the separation rule already guarantees between two windows. A float "
                + "would have been four bytes of precision a human neck cannot hold");
        t.Equal(8, NetProtocol.SharedWindowEntryMinBytes,
                "a shared-window entry head is kind + flags + page + pageCount + a 4-byte key");
        t.Equal(31, NetProtocol.SharedWindowEntryBytesWithPose,
                "…plus poseStamp + sizeCode + frame + the 20-byte shared pose");
        t.Equal(3, NetProtocol.SharedWindowMaxEntries,
                "and at most THREE shared windows can stand at once in the map room — raised 2 -> 3 "
                + "in ModBuild 231 when the ENCOUNTER (kind 3) joined, which is exactly what the "
                + "record's own doc said would happen ('a third kind raises the cap in its own "
                + "commit'). The cap is a worst case, not a prediction: the send loop fills entries "
                + "in kind order and a cap below the number of kinds silently DROPS the last one");
        t.Equal(94, NetProtocol.SharedWindowMaxRecordBytes,
                "so the worst-case payload is 1 + 3 x 31 = 94");
        t.Equal(NetProtocol.SharedWindowKindEncounter, NetProtocol.SharedWindowKindMax,
                "and the encounter is the largest kind this build can name, so a kind 4 from a newer "
                + "sender is stepped over by its own computed length");
        t.Equal(7126, PresenceSerializer.MaxSize,
                "Shared window motion77 adds four bytes: worst 6869/buffer 7126 with 257 spare bytes and unchanged 7168 assembly. Durable burn75 adds 2732 bytes: worst 6865/buffer 7122 with bounded 7168 assembly. Reward74 retained a 257-byte margin (worst 4133) with buffer4390 and reassembly cap4352. Reward73 used worst4074/buffer4331. Video72 used worst4044 and buffer4301; actual snapshots remain below4096. MB497 added record71 (worst3840) with buffer4097. MB490 raised the buffer to4096 for exclusive second scenario/map provenance66/67 (worst3837, margin259). MaxSize was raised 1600 -> 1800 when records 20 and 21 landed: the worst case went "
                + "1357 -> 1430 (+8 for record 20 with its TLV header, +65 for record 21 with "
                + "its), and the margin at 1600 would have been 170 — thinner than the largest "
                + "single record (257, board tuning) and therefore a violation of the rule that "
                + "every new record keeps a margin of at least one record's worth. The selection "
                + "edge then took record 20 from 8 to 13 and the worst case to 1435, which needed "
                + "no raise. RAISED AGAIN 1800 -> 1900 on 2026-09-05 by the HELD-PROP record (37): "
                + "its worst case is 56 bytes (two 27-byte slots plus a TLV header), and the margin "
                + "at 1800 would have been 230 - under the 257-byte board-tuning record and "
                + "therefore the same violation again. The SHARED GAZE byte landed in the same "
                + "round and cost one more, so the documented sum went 1513 -> 1570, not 1569; the "
                + "two lanes computed against the same base without seeing each other. This "
                + "constant sizes ONE local send buffer and appears in no packet, header or "
                + "contract, so every raise is invisible to every peer including older builds. "
                + "RAISED AGAIN to 3710 for complete record47 subwidgets (3449 total), after 1900 -> 2100 on 2026-09-05, and this one by an EXISTING term "
                + "rather than a new record: the WALL FADES key cap went 24 -> 63 (99 -> 255 "
                + "bytes, the most that record's TLV length byte can carry), so the worst case "
                + "went 1570 -> 1726 and the margin at 1900 would have been 174 - under the "
                + "257-byte board-tuning record, the same violation a third time. The cap was "
                + "the defect: both clients in the ModBuild-447 forest log were fading 41-44 "
                + "walls at once, and since the sender ships the LOWEST 24 keys and an FNV key "
                + "is stable for the scenario, the same ~40 percent of a causer's walls never "
                + "reached a teammate for the whole session (user 2026-09-05, fackel.jpg)");
        t.True(PresenceSerializer.MaxSize - 1726 >= 257,
               "the margin is larger than the largest single record, which is the stated rule and "
               + "the reason all four raises happened. ModBuild 231 moved the worst case 1435 -> "
               + "1466 (record 21's payload 63 -> 94 for the encounter's entry); the pick-banner "
               + "cap and records 35/36 took it to 1513; the shared-gaze byte and the held-prop "
               + "record together took it to 1570; the wall-fade key cap's 24 -> 63 raise added "
               + "156 more and took it to 1726, which is the number this assertion is measured "
               + "against - a margin of 374 at MaxSize 2100. A lane that computed its own term "
               + "against the 1570 base has to re-add it on top of 1726");
    }
}
