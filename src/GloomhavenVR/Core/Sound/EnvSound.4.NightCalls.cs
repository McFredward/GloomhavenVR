using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// EnvSound part 4 of 5 — see EnvSound.1.Core.cs for the type's own doc
// and for why the parts are digit-prefixed.
internal static partial class EnvSound
{
    // ---- the night calls -------------------------------------------------------------------------
    //
    // USER REQUEST, ModBuild 221 hardware, verbatim:
    //
    //     "Dezenter Wind kann bleiben und ansonsten eventuell hier und da noch ein ruf von tieren
    //      (was man so im Wald in der Nacht hört)"
    //
    // ...AND THE FOLLOW-UP ON ModBuild 222, verbatim, which is what ModBuild 223 answers here:
    //
    //     "Statt generrell durchgehende sounds zu machen lieber die Tierrufe ... Im Wald mal ne Eule
    //      oder ähnliches die ruft (auch aus dem Wald hörbar, hier sollte die Position auch random
    //      wechseln)"
    //
    // TWO THINGS CHANGE AND BOTH ARE IN THAT SENTENCE. First, the calls are now carrying the room
    // rather than decorating it — the two continuous beds are deleted (see THE ROOM TONES, DELETED),
    // so NightCallSkip comes down and the wood calls more often. Second, and this is the whole of
    // the placement work below: THE POSITION MOVES. See NightCallPerch.
    //
    // "HIER UND DA" IS THE WHOLE SPECIFICATION AND IT IS A RATE, so it is set against the rates this
    // file already ships and the user has already judged: the drip is on 2.85 s (and its own doc
    // calls that the worst offender in the room), the rat on 26 s, the apparitions on 83 s. A call
    // roughly every MINUTE sits between the last two, which is where "here and there" lives — you
    // notice it, you do not wait for it, and you never learn its rhythm.
    //
    // THE SCHEDULE IS A PURE FUNCTION OF THE SHARED CLOCK, so every client hears the same call from
    // the same tree on the same frame with ZERO wire bytes. That is the drip's and the rat's property
    // and, SINCE ModBuild 226, the fire crackle's as well — this paragraph used to end by naming the
    // crackle as the one exception in the file, on the argument that a crackle marks no visual. The
    // user's ruling ("Genau wie die Easter-Eggs sollen auch die Sounds mit allen Mitspieler
    // synchronisiert sein") carves out no exceptions, so there is no longer one; see TickFire.
    // The argument for doing it here FIRST still stands and is worth keeping: a call is a much more
    // findable event than a crackle — it is 2.3 s long, it comes from a fixed tree and there is
    // nothing else in the room — so two players standing together hearing owls at different moments
    // would have been an obvious defect rather than a subtle one. Hence a slot index, not a walk.
    //
    // ...AND IT IS STILL A POISSON WAITING TIME, which is the point of doing it this way rather than
    // with the rat's uniform draw. EnvSoundSchedule.PoissonGap turns ONE hash draw into an
    // exponential gap, whose mode is at zero — so calls CLUSTER (two owls close together, then a long
    // quiet) instead of arriving on a wobbly metronome. That distinction is not decoration in this
    // file: the ice sound beat at a fixed 0.45 s, the user called it "super nervig", and the whole
    // cue was deleted rather than re-timed. He has now used the same word about the wood.
    //
    // THE BOUNDS MAKE THE SLOT SAFE BY CONSTRUCTION. PoissonGap is clamped to
    // mean * [0.28, 2.60], so with NightCallMean = 15.5 s the offset is always inside
    // 4.34..40.30 s and therefore always inside the 41 s slot — the call can never land in the next
    // slot's window or before the current one opens, and no branch below has to test for it.

    /// <summary>The night calls' slot, in shared-clock seconds, and the mean of the Poisson offset
    /// inside it. 41 s is prime and non-commensurate with the rat's 26, the drip's 2.85, the haunt's
    /// 83 and every LFO period in this file — item 6 of the class doc. The mean is chosen so that
    /// <see cref="EnvSoundSchedule.PoissonGapMax"/> x mean (40.30 s) still fits inside the slot; see
    /// the block above.</summary>
    private const float NightCallSlot = 41f;
    private const float NightCallMean = 15.5f;

    /// <summary>The share of slots in which no animal calls at all. <b>0.30 -&gt; 0.22 at ModBuild
    /// 223</b>, so the realised mean gap goes from about 41 / 0.70 = 59 s to 41 / 0.78 = <b>53 s</b>.
    /// A skipped slot is not a missing sound: a wood in which something calls every single minute on
    /// the minute is a wood with a clock in it, and the skip is what makes the long quiets long
    /// enough to be quiet.
    ///
    /// <para>IT COMES DOWN BECAUSE THE CALLS ARE NOW THE ROOM. "Statt generrell durchgehende sounds
    /// zu machen lieber die Tierrufe" — with the wash deleted and the insects then intermittent,
    /// these are most of what the wood has, and 59 s of silence between them was authored when there
    /// was a continuous bed underneath. <b>THEY ARE NOW ALL OF IT.</b> ModBuild 226 deleted the
    /// insect chorus too (THE INSECT CHORUS, DELETED), so besides the resting draught and whatever
    /// the elements bring, these two calls are what the wood is. The skip and the mean were NOT
    /// re-tuned in that round on purpose: he wrote "ansonsten finde ich es sehr gut" about the build
    /// these numbers shipped in, and moving them would put a rate he has approved back in play in
    /// the same edit that removes the thing he objected to. <b>THE SLOT ITSELF IS DELIBERATELY NOT TOUCHED</b>: 41 s is what
    /// makes <see cref="EnvSoundSchedule.PoissonGapMax"/> x <see cref="NightCallMean"/> = 40.30 s fit
    /// inside a slot by construction, and moving it would put that proof back in play for a 10%
    /// change in rate. One number for one decision.</para></summary>
    private const float NightCallSkip = 0.22f;

    // =============================================================================================
    //  THE NIGHT CALLS' DECK — ModBuild 241, GROWN AT 242. TEN ANIMALS ON THE RATE TWO USED TO HAVE.
    // =============================================================================================
    //
    //  USER REQUESTS, 2026-08-24, verbatim, and both brackets are acceptance criteria:
    //
    //      "Füge noch mehr verschiedene Tiersounds hinzu die zu einem Wald in der Nacht passen für
    //       mehr Varianz (nicht mehr Häufigkeit)."
    //
    //      "Mach bei den Waldsound gerne auch noch ein paar gruseligere Tiersounds dazu wie man es
    //       aus der Pop-Kultur kennt. Aber auch nicht aufdringlich. Gerne eventuell auch Insekten
    //       Sounds."
    //
    //  ModBuild 242 ANSWERS THE SECOND WITH THE MACHINERY THE FIRST BUILT, and that is the whole of
    //  its change to this file: three more rows in NightCalls, three more cards in NightCallDeck,
    //  and a re-deal of the shares so that ten voices are not two voices in a bigger deck. THE
    //  SCHEDULE IS STILL NOT TOUCHED. 241's report closes with "A ROUND THAT WANTS MORE VARIETY MUST
    //  ADD CARDS, NEVER SLOTS"; this is that round, and that is what it did.
    //
    //  THE THREE NUMBERS THAT SET THE RATE ARE NOT TOUCHED BY THIS ROUND, AND THIS PARAGRAPH IS
    //  HERE SO THE NEXT ONE DOES NOT "IMPROVE" THE VARIETY BY TURNING THEM UP. NightCallSlot (41 s),
    //  NightCallMean (15.5 s) and NightCallSkip (0.22) are byte-for-byte what ModBuild 223 shipped
    //  — a realised mean gap of about 53 s, at most one call per slot, exactly as before. What
    //  changed is one expression in TickNightCall: `Haunt.Hash(slot, ...) < NightCallOwlShare`,
    //  which chose between two clips, is now `EnvSoundSchedule.DeckDraw(slot, NightCallDeck, ...)`,
    //  which chooses between TEN. Same slots, same skips, same waiting time, same one-shot voice,
    //  same number of events per minute — a bigger vocabulary spoken at the same rate.
    //
    //  THE ANIMALS AND WHY EACH IS TELLABLE FROM THE OTHERS are argued in EnvSound.Bank.cs, under
    //  THE WOOD'S VOCABULARY (the 241 five) and THE EERIE REGISTER (the 242 three), with the
    //  measured centroid/spread/attack/flatness table and the rejected candidates — a nightjar, a
    //  vixen's and a hare's scream, a wood pigeon, a bittern, a woodpecker, a wing pass, and a
    //  continuous cricket bed. This file owns the other half: how often, from where, and at what
    //  level.
    //
    //  WHY A DECK RATHER THAN A WEIGHTED DRAW — the full argument, with the measurements, is in
    //  EnvSoundSchedule.DeckDraw. In one line: a weighted draw over these ten shares repeats itself
    //  back-to-back 12.50% of the time, the deck does it 0.0320% of the time, and a repeat is the
    //  single thing that makes a synthesized wood sound synthesized.
    //
    //  IT IS ALSO WHAT MAKES A RARE ANIMAL RARE RATHER THAN ABSENT, and at ModBuild 242 that stopped
    //  being a nicety and became the design. The fox, the roe deer, the WOLF and the BARN OWL are
    //  one card each in twenty: about one per 17.7 minutes of level time and — because a deck is
    //  dealt rather than rolled — never more than 39 calls apart, which the construction bounds at
    //  2m-1 whatever the draws do. "Ein paar gruseligere Tiersounds ... aber auch nicht
    //  aufdringlich" is a request for a rare register, and a weighted draw cannot deliver one: its
    //  drought is unbounded, so a two-hour session either never hears the wolf (and it is reported
    //  as missing) or hears two in three minutes (and it is aufdringlich).

    /// <summary>THE DECK. <b>SIXTEEN cards as it ships</b> — it grew to twenty at ModBuild 242 and
    /// came back to sixteen at 246, when the user withdrew four voices by name; the block directly
    /// above the initialiser carries his sentence and the shares that result. Indices into
    /// <see cref="NightCalls"/>, and the multiplicities ARE the shares: the deal is exact, not
    /// statistical. Cards are APPENDED and nothing is ever renumbered — a row inserted in the
    /// middle would deal a fox and play an owl, and a row DELETED is the same hazard read
    /// backwards, which is why the four withdrawn voices keep their rows and their enum values.
    ///
    /// <para><b>THE REST OF THIS SUMMARY IS THE ModBuild 242 TWENTY-CARD DEAL, kept as the BEFORE
    /// column the 246 shares are read against.</b></para>
    ///
    /// <para><b>WHY THOSE TWENTY, AND WHY THE TWO ORIGINALS CAME DOWN.</b> Twenty was chosen so the
    /// shares are exact twentieths, which is also what makes this table readable:</para>
    /// <code>
    ///   Owl         4   20 %      the tawny owl's hoot        one per  4.4 min
    ///   KeWick      3   15 %      the same bird's ke-wick     one per  5.9 min
    ///   NightBird   3   15 %      the small bird further off  one per  5.9 min
    ///   OwletBeg    2   10 %      the owlet's beg             one per  8.8 min
    ///   Raven       2   10 %      the corvid on its roost     one per  8.8 min
    ///   Stridulate  2   10 %      one insect, twice           one per  8.8 min
    ///   Fox         1    5 %      the fox                     one per 17.7 min
    ///   RoeDeer     1    5 %      the roe deer                one per 17.7 min
    ///   Howl        1    5 %      the wolf                    one per 17.7 min
    ///   BarnOwl     1    5 %      the barn owl                one per 17.7 min
    /// </code>
    /// <para>The hoot goes 25% -&gt; 20% and the far bird 18.75% -&gt; 15%, which is the point of a
    /// re-deal: with ten voices, two of them holding 44% of the wood would be the old two-clip
    /// problem wearing a bigger deck. The tawny owl is still the wood's signature bird at 35% across
    /// its two voices (down from 43.75%), because the user named it by name ("Im Wald mal ne Eule
    /// oder ähnliches die ruft") and a wood whose owl has become rare has answered a different
    /// request.</para>
    ///
    /// <para><b>THE TWO EERIE CARDS ARE ONE EACH, AND THE DROUGHT IS THE POINT.</b> "Ein paar
    /// gruseligere Tiersounds ... aber auch nicht aufdringlich" is a request for a register, not for
    /// a frequency: a wolf heard six times in a two-hour session is a wood with a wolf in it, and one
    /// heard every third minute is a soundtrack. Because this is a DECK and not a weighted draw the
    /// rarity is bounded at both ends — 5% exactly, and never more than 39 calls apart (measured over
    /// 200,000 draws; the construction's hard bound is 2m-1 = 39). A weighted draw with the same
    /// share has an unbounded drought, which in one session is the difference between a rare sound
    /// and a sound the user reports as missing.</para>
    ///
    /// <para><b>AND THE INSECT IS TWO.</b> It is the commonest real sound in a summer wood and the
    /// quietest card here, so 10% is the share that makes it present without making it a floor —
    /// 0.72 s of insect about every nine minutes. The argument for it being a CARD at all rather
    /// than a second, faster channel of its own is in <c>EnvSound.Bank.cs</c> under THE INSECT, AS A
    /// CARD, and it is short: a second channel is more events per minute by construction, which is
    /// what he ruled out in brackets one round ago, and at any rate worth having it becomes the bed
    /// this room has now deleted three times.</para></summary>
    // ---- ModBuild 246 — FOUR VOICES WITHDRAWN, BY NAME, AT THE USER'S REQUEST.
    //      "Bei den Waldsounds entferne: BarnOwl, Fox, Howl, RoeDeer."
    //
    //      THEY ARE REMOVED AS CARDS, NOT AS ROWS. Cards 5 (fox), 6 (roe deer), 7 (wolf) and 8
    //      (barn owl) are gone from the deck, so nothing deals them and nothing plays them. Their
    //      ROWS stay where they are in NightCalls and their enum values keep their numbers, because
    //      the deck indexes that table POSITIONALLY — this file's own warning is that "a row
    //      inserted here without the matching card values re-numbered would deal a fox and play an
    //      owl", and deleting rows is the same hazard read backwards. The four Make* calls are also
    //      dropped from the bank build (see EnvSound.Bank.Build), so nothing is synthesised for a
    //      voice nobody can hear.
    //
    //      THE EVENT RATE IS UNCHANGED AND THAT IS THE POINT. The deck went 20 cards -> 16, but the
    //      schedule has never read the deck's length: NightCallSlot (41 s), NightCallMean and
    //      NightCallSkip are byte-for-byte what ModBuild 223 shipped. A shorter deck changes WHICH
    //      animal a slot deals and nothing else — the wood is exactly as talkative as it was, and
    //      "nicht aufdringlich" is untouched.
    //
    //      THE NEW SHARES, since six voices now divide what ten used to: owl 4/16 = 25 %, ke-wick
    //      3/16 = 19 %, small bird 3/16 = 19 %, owlet 2/16 = 12.5 %, crow 2/16 = 12.5 %, insect
    //      2/16 = 12.5 %. The tawny owl is still the wood's signature bird at 44 % across its two
    //      calls, which is the balance ModBuild 222 set and every round since has kept.
    private static readonly byte[] NightCallDeck =
    {
        0, 0, 0, 0,   // the tawny owl's hoot
        1, 1, 1,      // the tawny owl's ke-wick
        2, 2, 2,      // the small bird further off
        3, 3,         // the owlet's beg
        4, 4,         // the crow's caw            — voice replaced at ModBuild 246
        9, 9,         // one insect stridulating   — ModBuild 242
        // 5 fox / 6 roe deer / 7 wolf / 8 barn owl — WITHDRAWN at ModBuild 246, see above.
    };

    /// <summary>Separates this deck's shuffle stream from any other deck's. Arbitrary but FIXED: a
    /// change here re-deals every wood in every session, which is not something to do by accident.
    /// 0x4E43444B is 'NCDK'.</summary>
    private const uint NightCallDeckSalt = 0x4E43444Bu;

    /// <summary>
    /// What the two calls are played at, and how far they carry. <b>ALL SIX NUMBERS MOVED AT ModBuild
    /// 223, AND THEY MOVED TOGETHER SO THAT THE DELIVERED LEVEL DID NOT GO UP.</b>
    ///
    /// <para><b>WHY THE ROLLOFF MINIMUM HAD TO GROW.</b> Until this round both calls sounded from a
    /// welded mesh's transform at the room's local origin, i.e. from the middle of the clearing,
    /// typically 2.2-7.6 perceived metres from the head. They now sound from a RING out in the trees
    /// (see <see cref="NightCallPerch"/>) — 6.1-10.4 perceived m for the owl and 8.7-15.6 for the
    /// bird, before the player's own position is added. On the old 2.5 m minimum an owl at 10 m would
    /// have been 12 dB down, i.e. moved out to a tree and then muted for having moved. The minimum is
    /// what a real distant call HAS: an owl three hundred metres off is loud, and it is DIRECTION and
    /// not level that tells you where it is. 8 m and 10 m put the ring largely inside the flat part
    /// of Unity's curve, so which tree it is comes from the spatialiser's pan and the level stays
    /// roughly constant — which is also what makes "auch aus dem Wald hörbar" true.</para>
    ///
    /// <para><b>AND THE GAINS CAME DOWN BY WHAT THE MINIMA GAVE BACK.</b> Effective level is
    /// <c>gain x min(1, minMeters / distance)</c>, so:</para>
    /// <code>
    ///                 gain    min m    distance     effective       mean
    ///   Owl  MB222   0.075     2.5     2.2..7.6    0.075..0.025     0.041
    ///   Owl  223     0.050     8.0     3.9..12.6   0.050..0.032     0.041
    ///   Bird MB222   0.060     2.0     2.2..7.6    0.055..0.016     0.026
    ///   Bird 223     0.032    10.0     7.5..17.5   0.032..0.018     0.026
    /// </code>
    /// <para>The MEAN is held to two decimal places in both cases and the PEAK comes down by 3.5 dB
    /// (owl) and 4.6 dB (bird) — the loudest a call can now be is quieter than the loudest it could
    /// be before, and it varies far less. That is the honest way to move an emitter's position after
    /// three rounds of rejected levels: nothing gets louder anywhere.</para>
    ///
    /// <para>Both minima are well past <see cref="FireMinMeters"/>'s argument about near-field
    /// minima — a call authored at the candles' 0.6 m would be inaudible two steps away, which is the
    /// fault that doc spells out at length.</para>
    /// </summary>
    private const float OwlGain = 0.050f;
    private const float OwlMinMeters = 8f;
    private const float OwlMaxMeters = 40f;
    private const float BirdGain = 0.032f;
    private const float BirdMinMeters = 10f;
    private const float BirdMaxMeters = 34f;

    /// <summary>
    /// THE FIVE NEW CALLS' LEVELS — ModBuild 241 — AND THE RULE THEY WERE CHOSEN UNDER IS THE
    /// PARAGRAPH ABOVE'S: <b>nothing gets louder anywhere.</b> The owl is and remains the loudest
    /// call the wood can make.
    ///
    /// <para>Measured the way the table above is, over 200,000 realisations of the ring draw plus
    /// the player's own position (<c>.planning/debug</c>, and the model is calibrated by reproducing
    /// the two shipped rows exactly):</para>
    /// <code>
    ///                   gain    min m    ring authored   distance      effective       mean
    ///   Owl   (223)    0.050     8.0        7 .. 12      3.0..15.0    0.050..0.027     0.041
    ///   Bird  (223)    0.032    10.0       10 .. 18      6.3..20.8    0.032..0.015     0.023
    ///   KeWick         0.042     8.0        7 .. 13      3.0..15.9    0.042..0.021     0.033
    ///   Raven          0.038    11.0       12 .. 20      8.1..22.6    0.038..0.019     0.026
    ///   Fox            0.034     8.0        8 .. 16      3.4..17.8    0.034..0.015     0.024
    ///   RoeDeer        0.034     9.0       11 .. 20      5.8..21.3    0.034..0.014     0.021
    ///   OwletBeg       0.030    10.0        9 .. 16      5.2..18.8    0.030..0.016     0.024
    /// </code>
    /// <para>Every new peak is below the owl's 0.050 and every new mean is below its 0.041. The two
    /// with a FAST ATTACK are the two quietest of the five on purpose: the fox (9 ms) and the roe
    /// deer (5 ms) are the only cues in this room the ear can be startled by, and the bank's one
    /// design law is that the frightening sound is never the loud one. They are also seated on the
    /// FAR half of their rings for the same reason.</para>
    ///
    /// <para><b>THE NUMBERS IN THE THREE RIGHT-HAND COLUMNS WERE RE-MEASURED AT ModBuild 242 AND
    /// THEY MOVED, so the older table is not silently left standing beside a new one taken a
    /// different way.</b> The 241 round produced them from a script that did not survive it; the
    /// model is now <c>perch_levels()</c> in <c>.planning/envsound-replica/room.py</c>, which states
    /// its assumptions (player uniform in a 4.6 m disc, ears at 1.60 m, distance in three dimensions
    /// through <c>AuthoredToPerceived</c>) and prints every row from one instrument. It reproduces
    /// the ModBuild 223 owl's mean of 0.041 exactly and the bird's to 0.003. What changed is the
    /// FLOOR column — a 3-D distance with the player free to stand off-centre reaches further than
    /// the older model allowed — and the ORDER of the rows is untouched, which is the only thing
    /// this table is used to decide.</para>
    /// </summary>
    private const float KeWickGain = 0.042f;
    private const float KeWickMinMeters = 8f;
    private const float KeWickMaxMeters = 40f;
    private const float RavenGain = 0.038f;
    private const float RavenMinMeters = 11f;
    private const float RavenMaxMeters = 44f;
    private const float FoxGain = 0.034f;
    private const float FoxMinMeters = 8f;
    private const float FoxMaxMeters = 40f;
    private const float RoeDeerGain = 0.034f;
    private const float RoeDeerMinMeters = 9f;
    private const float RoeDeerMaxMeters = 40f;
    private const float OwletGain = 0.030f;
    private const float OwletMinMeters = 10f;
    private const float OwletMaxMeters = 34f;

    /// <summary>
    /// THE THREE EERIE CALLS' LEVELS — ModBuild 242 — AND THEY ARE THE THREE QUIETEST CARDS IN THE
    /// DECK. <b>Not one of them reaches the level of the quietest call that was already there.</b>
    ///
    /// <para>THAT IS THE WHOLE ANSWER TO "gruseliger, aber auch nicht aufdringlich", together with
    /// the attack times in <c>EnvSound.Bank.cs</c>: the register goes up and the level goes DOWN.
    /// Measured by the same instrument as the table above, over 200,000 realisations of the ring
    /// draw plus the player's own position:</para>
    /// <code>
    ///                   gain    min m    ring authored   distance      effective       mean
    ///   Owl   (223)    0.050     8.0        7 .. 12      3.0..15.0    0.050..0.027     0.041
    ///   OwletBeg (241) 0.030    10.0        9 .. 16      5.2..18.8    0.030..0.016     0.024
    ///   BarnOwl        0.028    12.0       12 .. 20      7.3..22.0    0.028..0.015     0.022
    ///   Howl           0.026    12.0       16 .. 24     10.2..24.7    0.026..0.013     0.017
    ///   Stridulate     0.022     9.0        7 .. 11      2.5..13.5    0.022..0.015     0.021
    /// </code>
    /// <para>The howl is the quietest thing the wood can do at its mean (0.017 against the owl's
    /// 0.041 — 7.6 dB down) and it is the FARTHEST AWAY, which is also what a howl has to be: a wolf
    /// you can place in a particular tree is a wolf that is close, and a wolf that is close is not
    /// eerie, it is an emergency. The stridulation has the lowest gain of all because it is the one
    /// card that can be near — an insect at 2.5 perceived m is normal, an owl at 2.5 m is not — and
    /// its rolloff minimum of 9 m means even that closest realisation is only its own gain.</para>
    ///
    /// <para>THE ROLLOFF MINIMA FOLLOW THE 223 ARGUMENT UNCHANGED: a real distant call is LOUD, and
    /// it is direction and not level that tells you where it is. 12 m for the two birds-of-prey
    /// registers puts their rings largely inside the flat part of Unity's curve, so which tree it is
    /// comes from the spatialiser's pan.</para>
    /// </summary>
    private const float HowlGain = 0.026f;
    private const float HowlMinMeters = 12f;
    private const float HowlMaxMeters = 48f;
    private const float BarnOwlGain = 0.028f;
    private const float BarnOwlMinMeters = 12f;
    private const float BarnOwlMaxMeters = 44f;
    private const float StridGain = 0.022f;
    private const float StridMinMeters = 9f;
    private const float StridMaxMeters = 30f;

    /// <summary>Hash channels for the SIX independent decisions one call needs: whether the slot is
    /// silent, WHEN inside it, its pitch, and — since ModBuild 223 — WHERE, as an azimuth, a radius
    /// and a height on the perch ring.
    ///
    /// <para>A CHANNEL IS NOT AN EXCLUSIVE RESOURCE — <see cref="DripVariantChannel"/>'s doc makes
    /// the argument in full — but draws that are all compared BY THE EAR on one event must not share
    /// one, or (say) the latest call in every slot would forever be the highest-pitched owl in the
    /// nearest tree. These six are distinct FROM EACH OTHER, which is the property that matters.</para>
    ///
    /// <para><b>THE SEVENTH DECISION — WHICH ANIMAL — LEFT THIS TABLE AT ModBuild 241, AND CHANNEL 6
    /// IS FREE AGAIN.</b> The note that stood here through 223-240 said "THAT RANGE IS NOW FULL, and
    /// the next round that wants an eighth per-call decision has to solve that rather than invent a
    /// channel 8", the range being a MIRROR of <c>EnvHaunt.cginc</c>'s channel table (:186-192). The
    /// round that needed an eighth decision solved it the other way round: the animal is now dealt
    /// from a DECK (<see cref="EnvSoundSchedule.DeckDraw"/>) in integer arithmetic that does not use
    /// this cascade at all, because it needs fifteen decorrelated draws per cycle and no hash channel
    /// table should be widened to feed one caller. Channels 5 and 6 are now unused HERE; the warning
    /// stands for anyone who wants to add a channel 8 to the shader's table.</para>
    ///
    /// <para>THE OVERLAPS WITH OTHER SUBSYSTEMS ARE STILL SOUND, BUT ONE HALF OF WHY HAS CHANGED.
    /// Until ModBuild 296 this paragraph said "the DRIP and the RAT are cellar-only and can never run
    /// in the same session's room as these", and that is now FALSE: the cellar's window has a wood
    /// outside it and therefore has these calls (see <see cref="_windowMouth"/>). The conclusion
    /// survives on the OTHER half of the argument, which was always the load-bearing one — the drip
    /// indexes a drip period, the rat a rat slot, the apparitions an 83 s slot and this schedule a
    /// 41 s one, and two values that are never compared cannot be seen to correlate. A channel is
    /// not an exclusive resource; a shared INDEX would be.</para>
    /// </summary>
    private const float NightCallSkipChannel = 3f;
    private const float NightCallWhenChannel = 4f;
    private const float NightCallPitchChannel = 7f;
    private const float NightCallAzimuthChannel = 0f;
    private const float NightCallRadiusChannel = 1f;
    private const float NightCallHeightChannel = 2f;

    // ---- WHERE A CALL COMES FROM -------------------------------------------------------------------
    //
    //  USER, 2026-08-22, verbatim: "Im Wald mal ne Eule oder ähnliches die ruft (auch aus dem Wald
    //  hörbar, hier sollte die Position auch random wechseln)".
    //
    //  THE OBVIOUS IMPLEMENTATION IS NOT AVAILABLE, and finding that out is most of this block. "A
    //  different tree each time" wants a set of tree nodes to draw from, and THE WOOD HAS NONE: the
    //  bake grows every trunk into one of two accumulators and WELDS each into a single mesh —
    //  BuildEnvironmentRooms.cs:16065-16067 — so the room's hierarchy contains 'TrunksNear',
    //  'TrunksFar' and 'Canopy' and nothing per-tree, and all three sit at the room's local origin
    //  with an identity transform. ModBuild 222 seated the owl on 'Canopy' believing that was the
    //  canopy's position; it was the middle of the clearing.
    //
    //  SO THE PERCH IS A POINT ON A RING, DERIVED FROM THE BAKE'S OWN NUMBERS. Three constants are
    //  MIRRORED here exactly as DripPeriod and RatPeriod are, and for the same reason — they are a
    //  contract with the room builder, and a sound that invented its own geometry would drift away
    //  from the picture:
    //
    //      ClearR = 5.4 m      the open ground around the board (BuildEnvironmentRooms.cs:14503,
    //                          "open ground around the board"), i.e. the radius inside which there
    //                          are no trees at all;
    //      trunks out to 27 m  AddForest places each tree at r = Lerp(ClearR, 27, sqrt(u)), which is
    //                          area-uniform over the annulus (:15995);
    //      CanopyY(r) = 6.8 + 0.30 * (r - ClearR)     the canopy's height at radius r (:14763).
    //
    //  HOW THE TWO BOUNDS ARE ENFORCED, which is the question this design has to answer:
    //
    //    * A CALL CAN NEVER COME FROM INSIDE THE PLAYER. The inner radius is 7.0 authored metres,
    //      which is 1.6 m OUTSIDE ClearR — the player and the board are on the clearing floor, whose
    //      radius is 5.4 m, so the closest a call can be to a player standing at the very edge of
    //      the open ground is 1.6 authored m (~1.4 perceived m), and to one at the board about 7 m.
    //      It is a bound on the GEOMETRY rather than a test against the head, and that is deliberate:
    //      see the multiplayer note below. Belt and braces on top of it, the rolloff is FLAT inside
    //      OwlMinMeters = 8 perceived m, so even the closest possible perch cannot be louder than
    //      the farthest — a call has no near field to be inside of.
    //    * A CALL CAN NEVER COME FROM OUTSIDE THE ROOM. The outer radii are 12 m (owl) and 18 m
    //      (bird) against a trunk field that runs to 27 m and a canopy that has closed over by 19 m
    //      (CanopyMask's outer term, :14847), so both rings are inside the visible wood with room to
    //      spare. The height is a fraction of CanopyY at the drawn radius, so a call is always
    //      between the ground and the branches over it and never above the canopy.
    //
    //  MULTIPLAYER: EVERY DRAW IS A PURE FUNCTION OF THE SLOT INDEX AND THE BAKE. Nothing here reads
    //  the head, the rig scale, the zoom or Time.time, so two clients place the same call at the same
    //  point in the same room on the same frame with ZERO wire bytes — which is this file's contract
    //  for every scheduled event and the reason a "pick the nearest tree that is not too close to the
    //  listener" filter was rejected outright: the listener is per-client, so that would have put two
    //  players' owls in different trees, which is exactly the kind of disagreement a 2.3 s call from
    //  a fixed direction makes obvious.
    //
    //  AND IT IS THE GROUND NODE'S FRAME, not the room root's. The bake places 'Ground' at the room's
    //  local origin with identity rotation and unit scale (:15878), so TransformPoint on it converts
    //  authored metres to world units through whatever placement, art scale and yaw the room happens
    //  to have — none of which this file has to know, and all of which would have to be re-derived if
    //  the ring were built in world units.

    /// <summary>The perch rings, in the bake's AUTHORED metres, measured from the clearing's centre.
    /// The owl is the near animal and the bird the far one, which is what the two gain/rolloff pairs
    /// above assume. Both inner radii are outside <see cref="ForestClearRadiusMeters"/>; see the
    /// block above for both bounds.</summary>
    private const float OwlPerchNearMeters = 7f;
    private const float OwlPerchFarMeters = 12f;
    private const float BirdPerchNearMeters = 10f;
    private const float BirdPerchFarMeters = 18f;

    /// <summary>How high up the canopy a call comes from, as a fraction of
    /// <see cref="ForestCanopyY"/> at the drawn radius. The owl sits in the crown of the trunks and
    /// the bird higher and thinner, which is also where their two bands put them. Never 0 (a call
    /// from the ground is a footstep) and never 1 (a call from above the canopy is a call from the
    /// sky).</summary>
    private const float OwlPerchHeightLo = 0.50f;
    private const float OwlPerchHeightHi = 0.80f;
    private const float BirdPerchHeightLo = 0.70f;
    private const float BirdPerchHeightHi = 0.95f;

    // ---- AND WHERE THE FIVE NEW ANIMALS ARE — ModBuild 241 -----------------------------------
    //
    //  "hier sollte die Position auch random wechseln" applies to every card, and so does the rule
    //  that a call comes from a PLAUSIBLE SOURCE. Every ring below is drawn from the same three hash
    //  channels off the same slot index and is therefore identical on every client; what differs per
    //  animal is WHICH ring and how high.
    //
    //  TWO OF THEM ARE ON THE GROUND, AND THAT IS THE ONE THING THIS TABLE ADDS TO THE 223 GEOMETRY.
    //  A fox does not call from the canopy and a roe deer cannot climb, so their heights are AUTHORED
    //  METRES ABOVE THE GROUND PLANE rather than a fraction of ForestCanopyY — a fox's muzzle is
    //  0.30-0.45 m up and a roe deer's is 0.75-1.00 m, which is where those numbers come from and why
    //  they are absolute. Expressing them as a canopy fraction would have made them 0.04 and 0.11 of
    //  a quantity that MOVES if the bake lifts the canopy, i.e. a fox that floats when a tree grows.
    //
    //  THE TWO BOUNDS THE BLOCK ABOVE PROVES STILL HOLD FOR ALL SEVEN, and they were checked rather
    //  than assumed:
    //    * NEVER INSIDE THE PLAYER. The smallest inner radius in the table is the owl's and the
    //      ke-wick's 7 m, which is 1.6 m outside ForestClearRadiusMeters. The fox's 8 m and the roe
    //      deer's 11 m are further out again, which is deliberate on top of the geometric bound: they
    //      are the two with a fast attack, and a bark that can happen at the edge of the clearing is
    //      a bark that can make someone jump.
    //    * NEVER OUTSIDE THE WOOD. The largest outer radius is 20 m (raven, roe deer), against a
    //      trunk field that runs to 27 m and a canopy that has closed over by 19 m.

    /// <summary>The ke-wick's ring. The same bird as <see cref="OwlPerchNearMeters"/>, so the same
    /// inner radius; 13 rather than 12 outside only so the two owl voices are not drawn from
    /// literally the same annulus and cannot sound like one bird with two mouths.</summary>
    private const float KeWickPerchNearMeters = 7f;
    private const float KeWickPerchFarMeters = 13f;
    private const float KeWickPerchHeightLo = 0.50f;
    private const float KeWickPerchHeightHi = 0.80f;

    /// <summary>The corvid's ring — the farthest and the highest in the table. A roost is out in the
    /// wood and near the top of it, which is also what the call's own level assumes.</summary>
    private const float RavenPerchNearMeters = 12f;
    private const float RavenPerchFarMeters = 20f;
    private const float RavenPerchHeightLo = 0.75f;
    private const float RavenPerchHeightHi = 0.95f;

    /// <summary>The owlet's ring. Between the two birds, high and thin — a fledgling begs from a
    /// branch near where it was raised.</summary>
    private const float OwletPerchNearMeters = 9f;
    private const float OwletPerchFarMeters = 16f;
    private const float OwletPerchHeightLo = 0.60f;
    private const float OwletPerchHeightHi = 0.88f;

    /// <summary>THE FOX, ON THE GROUND. The two height numbers are AUTHORED METRES, not a canopy
    /// fraction — see the block above. 0.30-0.45 m is a fox's muzzle when it stops to bark.</summary>
    private const float FoxPerchNearMeters = 8f;
    private const float FoxPerchFarMeters = 16f;
    private const float FoxGroundHeightLo = 0.30f;
    private const float FoxGroundHeightHi = 0.45f;

    /// <summary>THE ROE DEER, ON THE GROUND, AND FURTHER OFF. 0.75-1.00 m is where a roe deer's head
    /// is; the ring starts at 11 m because a deer that has seen you has already put trees between
    /// itself and you before it barks.</summary>
    private const float RoeDeerPerchNearMeters = 11f;
    private const float RoeDeerPerchFarMeters = 20f;
    private const float RoeDeerGroundHeightLo = 0.75f;
    private const float RoeDeerGroundHeightHi = 1.00f;

    // ---- AND WHERE THE THREE EERIE ANIMALS ARE — ModBuild 242 --------------------------------
    //
    //  The same three hash channels off the same slot index, so all ten are placed identically on
    //  every client; what differs is which ring and how high. The two bounds THE 223 BLOCK PROVES
    //  were re-checked for these three rather than assumed:
    //
    //    * NEVER INSIDE THE PLAYER. The smallest inner radius here is the insect's 7 m, which is the
    //      same bound the owl and the ke-wick sit on — 1.6 authored m outside ForestClearRadiusMeters.
    //      The wolf's 16 m and the barn owl's 12 m are far outside it again.
    //    * NEVER OUTSIDE THE WOOD, AND THE WOLF IS THE ONE ROW THAT NEEDED THE ARGUMENT RE-MADE.
    //      Its outer radius is 24 m, past the 20 m that was the table's largest until this round. The
    //      223 block's ceiling is TWO facts, not one: trunks run out to 27 m (AddForest, :15995) and
    //      the canopy has closed over by 19 m (CanopyMask, :14847). The canopy figure bounds a
    //      PERCHED animal, because a call from above the canopy is a call from the sky; the wolf is
    //      on the GROUND at 0.85-1.05 m, so the only fact that binds it is the trunk field, and 24 m
    //      is three metres inside it. A wolf at 24 m is still a wolf between the trees.

    /// <summary>THE WOLF, ON THE GROUND AND FARTHEST OUT. The heights are AUTHORED METRES: a wolf
    /// howls with its head up and its muzzle is then 0.85-1.05 m off the ground. The ring is 16-24 m
    /// — the farthest in the table on purpose, because distance is half of what makes a howl eerie
    /// and because it is the only card whose rolloff minimum (12 m) is far enough out to keep it
    /// audible there.</summary>
    private const float HowlPerchNearMeters = 16f;
    private const float HowlPerchFarMeters = 24f;
    private const float HowlGroundHeightLo = 0.85f;
    private const float HowlGroundHeightHi = 1.05f;

    /// <summary>THE BARN OWL'S RING. Out in the trees and LOWER in them than the other birds
    /// (0.45-0.75 of the canopy against the corvid's 0.75-0.95): a barn owl hunts the edge from a
    /// low stub or a stump, not from a roost in the crown, and a screech that comes from below the
    /// canopy is one that has something under it.</summary>
    private const float BarnOwlPerchNearMeters = 12f;
    private const float BarnOwlPerchFarMeters = 20f;
    private const float BarnOwlPerchHeightLo = 0.45f;
    private const float BarnOwlPerchHeightHi = 0.75f;

    /// <summary>THE INSECT, IN THE LITTER — the NEAREST and LOWEST source the wood has. 0.05-0.35
    /// AUTHORED metres is a stridulating insect on the ground or on a low stem, and the inner radius
    /// is the table's minimum of 7 m for the geometric bound's sake. It is the one card where the
    /// near ring is right rather than merely safe: an insect you hear from thirty metres away is a
    /// chorus, and a chorus is what this room deleted.</summary>
    private const float StridPerchNearMeters = 7f;
    private const float StridPerchFarMeters = 11f;
    private const float StridGroundHeightLo = 0.05f;
    private const float StridGroundHeightHi = 0.35f;

    /// <summary>
    /// ONE ANIMAL: which clip, how loud, how far it carries, which ring it sits on and how high.
    /// A struct rather than seven parallel arrays because these nine numbers are only ever read
    /// together, and a table with one row per animal is the thing a future round will want to add a
    /// row to.
    /// </summary>
    private readonly struct NightCallVoice
    {
        internal readonly EnvSoundClip Clip;
        internal readonly float Gain;
        internal readonly float MinMeters;
        internal readonly float MaxMeters;
        internal readonly float RingNear;
        internal readonly float RingFar;

        /// <summary>True when <see cref="HeightLo"/> and <see cref="HeightHi"/> are AUTHORED METRES
        /// above the ground plane; false when they are a fraction of <see cref="ForestCanopyY"/> at
        /// the drawn radius. The one flag in this table, and it exists because a fox and an owl do
        /// not measure their height from the same thing.</summary>
        internal readonly bool Ground;
        internal readonly float HeightLo;
        internal readonly float HeightHi;

        internal NightCallVoice(EnvSoundClip clip, float gain, float minMeters, float maxMeters,
                                float ringNear, float ringFar,
                                bool ground, float heightLo, float heightHi)
        {
            Clip = clip;
            Gain = gain;
            MinMeters = minMeters;
            MaxMeters = maxMeters;
            RingNear = ringNear;
            RingFar = ringFar;
            Ground = ground;
            HeightLo = heightLo;
            HeightHi = heightHi;
        }
    }

    /// <summary>THE TEN ANIMALS, indexed by the card <see cref="NightCallDeck"/> deals. The order
    /// is the deck's and must stay in step with it — a row inserted here without the matching card
    /// values re-numbered would deal a fox and play an owl, which is exactly the kind of silent
    /// mismatch this project's mirror checks exist for. That is why ModBuild 242's three rows are
    /// APPENDED rather than filed among the birds. Every constant in every row is documented above
    /// its own block.</summary>
    private static readonly NightCallVoice[] NightCalls =
    {
        new NightCallVoice(EnvSoundClip.Owl, OwlGain, OwlMinMeters, OwlMaxMeters,
                           OwlPerchNearMeters, OwlPerchFarMeters,
                           false, OwlPerchHeightLo, OwlPerchHeightHi),
        new NightCallVoice(EnvSoundClip.KeWick, KeWickGain, KeWickMinMeters, KeWickMaxMeters,
                           KeWickPerchNearMeters, KeWickPerchFarMeters,
                           false, KeWickPerchHeightLo, KeWickPerchHeightHi),
        new NightCallVoice(EnvSoundClip.NightBird, BirdGain, BirdMinMeters, BirdMaxMeters,
                           BirdPerchNearMeters, BirdPerchFarMeters,
                           false, BirdPerchHeightLo, BirdPerchHeightHi),
        new NightCallVoice(EnvSoundClip.OwletBeg, OwletGain, OwletMinMeters, OwletMaxMeters,
                           OwletPerchNearMeters, OwletPerchFarMeters,
                           false, OwletPerchHeightLo, OwletPerchHeightHi),
        new NightCallVoice(EnvSoundClip.Raven, RavenGain, RavenMinMeters, RavenMaxMeters,
                           RavenPerchNearMeters, RavenPerchFarMeters,
                           false, RavenPerchHeightLo, RavenPerchHeightHi),
        new NightCallVoice(EnvSoundClip.Fox, FoxGain, FoxMinMeters, FoxMaxMeters,
                           FoxPerchNearMeters, FoxPerchFarMeters,
                           true, FoxGroundHeightLo, FoxGroundHeightHi),
        new NightCallVoice(EnvSoundClip.RoeDeer, RoeDeerGain, RoeDeerMinMeters, RoeDeerMaxMeters,
                           RoeDeerPerchNearMeters, RoeDeerPerchFarMeters,
                           true, RoeDeerGroundHeightLo, RoeDeerGroundHeightHi),
        new NightCallVoice(EnvSoundClip.Howl, HowlGain, HowlMinMeters, HowlMaxMeters,
                           HowlPerchNearMeters, HowlPerchFarMeters,
                           true, HowlGroundHeightLo, HowlGroundHeightHi),
        new NightCallVoice(EnvSoundClip.BarnOwl, BarnOwlGain, BarnOwlMinMeters, BarnOwlMaxMeters,
                           BarnOwlPerchNearMeters, BarnOwlPerchFarMeters,
                           false, BarnOwlPerchHeightLo, BarnOwlPerchHeightHi),
        new NightCallVoice(EnvSoundClip.Stridulate, StridGain, StridMinMeters, StridMaxMeters,
                           StridPerchNearMeters, StridPerchFarMeters,
                           true, StridGroundHeightLo, StridGroundHeightHi),
    };

    /// <summary>
    /// WHAT A STONE WALL WITH A SLOT IN IT DOES TO A FOX — ModBuild 296, and it is DERIVED rather
    /// than dialled, because "im Keller hört man sie weniger" deserves a mechanism and not a knob.
    ///
    /// <para><b>THE MODEL IS ISO 12354-3's, reduced to the one term that matters here.</b> The
    /// sound pressure level indoors from an outdoor source is the level at the facade, minus the
    /// facade's sound reduction index, plus <c>10 log10(S / A)</c> — the ratio of the OPENING's area
    /// to the receiving room's ABSORPTION area. For this window the first two terms collapse:
    /// 0.55 m of rubble stone has an R of 55-60 dB, i.e. the wall transmits nothing at all, so
    /// every decibel that gets in comes through the hole and the hole's R is zero.</para>
    ///
    /// <code>
    ///   S  the OUTER opening   1.2283 x 0.6427 m                         =  0.789 m2
    ///   A  the cellar's absorption, Sabine, at mid frequencies:
    ///        flagstone floor    10.5 x 9.0 m       alpha 0.03            =  2.835
    ///        plank ceiling      10.5 x 9.0 m       alpha 0.10            =  9.450
    ///        rubble stone walls 2(10.5+9.0) x 3.3  alpha 0.03            =  3.861
    ///                                                             A      = 16.15 m2 sabins
    ///   10 log10(0.789 / 16.15) = -13.1 dB, i.e. an AMPLITUDE factor of 0.221
    /// </code>
    ///
    /// <para><b>WHY THE STRICTER MODEL WAS REJECTED, stated as a decision.</b> A pure ray/aperture
    /// model — capture the wavefront that hits the opening, re-radiate it into a hemisphere — gives
    /// <c>sqrt(A_opening x cos(theta) / 2pi) / d</c>, which at a ten-metre perch is about -30 dB and
    /// would delete the feature the user asked for. It is wrong for the same two reasons every ray
    /// model is wrong at an aperture: it ignores diffraction, which at the wavelengths an owl and a
    /// fox live at (0.3-3 m against a 1.2 m slot) is most of the transmission, and it ignores the
    /// reverberant build-up in a small hard room, which is precisely the <c>S/A</c> term above. The
    /// building-acoustics formula is the one that models both, and it is also the one whose inputs
    /// are things this room really has.</para>
    ///
    /// <para><b>AND WHAT IS DELIBERATELY NOT MODELLED, so it reads as a decision and not an
    /// omission.</b> A fox heard through a slot is also FILTERED — the aperture and the wall roll
    /// the high end off, and the stone room rings underneath it. Neither is here:</para>
    /// <list type="bullet">
    /// <item><b>No low-pass on the call.</b> Every clip in this bank is BAND-LIMITED WHEN IT IS
    /// SYNTHESISED (see EnvSound.Bank.cs — each voice's formants and its two-to-four-pole envelope
    /// are baked into the buffer), so a runtime filter would be a second, coarser copy of a shaping
    /// decision that has already been made once, with taste, per animal. This file makes that
    /// argument twice already, for the candle flutter and for the fire, and both times the runtime
    /// filter was REMOVED rather than added. A per-shot <c>AudioLowPassFilter</c> would also cost a
    /// component on the shared one-shot pool, which is the one place in this file where allocation
    /// per event was deliberately designed out.</item>
    /// <item><b>No reverb.</b> This project ships none anywhere and sets <c>bypassReverbZones</c> on
    /// every source it owns (the game's zones are sized for the game's world, not for a 20x
    /// diorama). A reverb added for this one cue would be the only one in the mod, and it would be
    /// the loudest thing in a room whose whole resting ambience is a draught at 0.00076.</item>
    /// </list>
    /// <para>What IS modelled is the level and the direction, which are the two things the user's
    /// sentence actually names. If a future round wants the muffling, the honest place for it is a
    /// second BAKED variant of each clip, not a filter on the live one.</para>
    /// </summary>
    private const float CellarWidthMeters = 10.5f;    // BuildEnvironmentRooms.CW
    private const float CellarDepthMeters = 9.0f;     // BuildEnvironmentRooms.CD
    private const float CellarHeightMeters = 3.3f;    // BuildEnvironmentRooms.CH
    /// <summary>The OUTER opening, in square metres: the snapped hole (1.4318 x 0.7857) less the
    /// jamb splay (2 x 0.55 x 0.185) and the cill rise (0.55 x 0.260). MIRRORED from the bake, which
    /// prints exactly these numbers in its "Cellar window opening (snapped)" line.</summary>
    private const float CellarWindowOpeningM2 = 1.2283f * 0.6427f;
    private static readonly float CellarAbsorptionM2 =
        CellarWidthMeters * CellarDepthMeters * 0.03f                                 // floor
        + CellarWidthMeters * CellarDepthMeters * 0.10f                               // plank ceiling
        + 2f * (CellarWidthMeters + CellarDepthMeters) * CellarHeightMeters * 0.03f;  // walls
    /// <summary>The amplitude factor a call loses getting in: <c>sqrt(S / A)</c>, i.e. the
    /// <c>10 log10(S/A)</c> above expressed as a gain. About 0.221, or -13.1 dB.</summary>
    private static readonly float WindowInsertion =
        Mathf.Sqrt(CellarWindowOpeningM2 / CellarAbsorptionM2);

    /// <summary>MIRRORED from <c>BuildEnvironmentRooms.cs</c>: <c>ClearR</c> (:14503, "open ground
    /// around the board") and <c>CanopyY</c> (:14763). They are a CONTRACT with the room builder in
    /// exactly the sense <see cref="DripPeriod"/> is — if the bake opens the clearing up or lifts the
    /// canopy, these move with it or the owl ends up in a tree that is not there.</summary>
    private const float ForestClearRadiusMeters = 5.4f;
    private static float ForestCanopyY(float radiusMeters) =>
        6.8f + 0.30f * (radiusMeters - ForestClearRadiusMeters);

    /// <summary>
    /// WHERE THIS SLOT'S CALL COMES FROM, in world space — a point on the perch ring, drawn from
    /// three hash channels and therefore identical on every client. See WHERE A CALL COMES FROM.
    /// </summary>
    /// <param name="slot">The 41 s call slot. The ONLY input, which is what makes this shared.</param>
    /// <param name="voice">The animal this slot dealt — see <see cref="NightCalls"/>. Its ring and
    /// its two height numbers are the only thing that differs from animal to animal.</param>
    /// <param name="where">The world position, valid only when this returns true.</param>
    /// <returns>False when the wood has no frame to measure from, in which case
    /// <see cref="BuildSwamp"/> has already said so loudly and the caller must not sound the
    /// call — a call at the world origin would be somewhere under the table.</returns>
    private static bool NightCallPerch(long slot, in NightCallVoice voice, out Vector3 where)
    {
        where = Vector3.zero;
        if (_perchFrame == null)
            return false;

        // AREA-UNIFORM IN THE ANNULUS, exactly as the bake seats the trees themselves
        // (`r = Lerp(ClearR, 27, sqrt(u))`, :15995). A linear draw would crowd the calls into the
        // near ring, because an annulus has more area the further out you go — and the audible
        // consequence would be that the wood's animals all sound close, which is the opposite of
        // "auch aus dem Wald hörbar".
        float u = Haunt.Hash(slot, NightCallRadiusChannel);
        float radius = Mathf.Lerp(voice.RingNear, voice.RingFar, Mathf.Sqrt(Mathf.Clamp01(u)));

        // THE BEARING. In the wood it is the whole circle; through the cellar's window it is the
        // OUTWARD half-plane only. The frame's axes are the room's, and the bake places the marker
        // with the wall's outward normal on +Z — so (r cos az, h, r sin az) with az in (0, pi) is
        // everything on the far side of the masonry and nothing on this side. It changes the
        // DISTANCE the aperture leg is computed over; the direction the player hears is the
        // window's either way. See _windowMouth.
        float azimuth = (_perchOutwardOnly ? Mathf.PI : 2f * Mathf.PI)
                        * Haunt.Hash(slot, NightCallAzimuthChannel);

        // THE HEIGHT, AND THE ONE BRANCH IN THIS FUNCTION. A perched animal's height is a fraction
        // of the canopy AT THE RADIUS IT WAS DRAWN AT, so it moves with the bake; a fox's and a roe
        // deer's is an absolute distance off the ground plane, because a muzzle is a muzzle whatever
        // the trees over it are doing. See AND WHERE THE FIVE NEW ANIMALS ARE.
        float h = Mathf.Lerp(voice.HeightLo, voice.HeightHi,
                             Haunt.Hash(slot, NightCallHeightChannel));
        float height = voice.Ground ? h : ForestCanopyY(radius) * h;

        // AUTHORED METRES IN, WORLD UNITS OUT. The frame carries the room's placement, its yaw and
        // its art scale, so nothing above had to know any of the three — see the block's last
        // paragraph.
        where = _perchFrame.TransformPoint(new Vector3(radius * Mathf.Cos(azimuth),
                                                       height,
                                                       radius * Mathf.Sin(azimuth)));
        return true;
    }

    /// <summary>The last slot this client has already answered, so a call fires once and not once per
    /// frame. <c>long.MinValue</c> is "not observing yet" — and the FIRST slot observed is deliberately
    /// swallowed, exactly as the drip's and the rat's are: the call that belongs to the slot the
    /// player walked in during has already happened.</summary>
    private static long _lastNightCallSlot = long.MinValue;

    /// <summary>
    /// THE NIGHT CALLS — ten animals in, under and between the trees, "hier und da". See the block
    /// above for the rate, the clock and why this is a slot index rather than the fire crackle's
    /// walk, and THE NIGHT CALLS' DECK for why ten animals do not mean ten times the events.
    /// </summary>
    private static void TickNightCall(float clock)
    {
        // THE CALL SLOT, through EnvSoundSchedule.TrySlot rather than an inline cast — see TickDrip
        // for the sentinel collision that motivated moving this arithmetic into one place.
        if (!EnvSoundSchedule.TrySlot(clock, NightCallSlot, out long slot))
            return;
        if (slot == _lastNightCallSlot)
            return;

        // THE CALL'S INSTANT INSIDE THIS SLOT. An exponential waiting time from one hash draw, and
        // bounded by PoissonGap's own clamp to 4.34..40.30 s — inside the 41 s slot by construction,
        // so this cannot schedule into the next slot and cannot fire on the slot boundary.
        float at = NightCallSlot * slot
                   + EnvSoundSchedule.PoissonGap(NightCallMean,
                                                 Haunt.Hash(slot, NightCallWhenChannel));
        if (clock < at)
            return;

        bool first = _lastNightCallSlot == long.MinValue;
        _lastNightCallSlot = slot;
        if (first)
            return;   // that call already happened before we started listening

        // A SKIPPED SLOT IS A SLOT NOTHING CALLED IN. Silence is the correct sound, and it is drawn
        // AFTER the slot is latched so a skipped slot still advances the schedule.
        if (Haunt.Hash(slot, NightCallSkipChannel) < NightCallSkip)
            return;

        // WHICH ANIMAL — ONE CARD OFF THE DECK, AND THIS LINE IS THE WHOLE OF ModBuild 241's CHANGE
        // TO THE SCHEDULE. It replaces `Haunt.Hash(slot, NightCallWhichChannel) < NightCallOwlShare`,
        // which chose between two clips; nothing above it moved, so the wood still sounds at most one
        // call per 41 s slot and still skips 22% of them. "mehr Varianz (nicht mehr Häufigkeit)" is
        // exactly this substitution and nothing else. ModBuild 242 did not touch this line either —
        // it added three CARDS, which is the same statement made twice. See THE NIGHT CALLS' DECK.
        // The card is a byte, so it cannot be negative; the ONE bound worth testing is the upper one,
        // because an edited deck that names a row this table does not have must be silence and never
        // an index. (Silence, not a clamp: a clamp would quietly play the wrong animal forever.)
        int card = EnvSoundSchedule.DeckDraw(slot, NightCallDeck, NightCallDeckSalt);
        if (card >= NightCalls.Length)
            return;
        NightCallVoice voice = NightCalls[card];

        // A DIFFERENT TREE EVERY CALL — "hier sollte die Position auch random wechseln". Drawn from
        // the slot index and the bake's own geometry and from nothing else, so every client puts this
        // call in the same place on the same frame with no wire bytes. See WHERE A CALL COMES FROM
        // for the two bounds (never inside the player, never outside the wood).
        if (!NightCallPerch(slot, voice, out Vector3 from))
            return;   // BuildSwamp already warned, loudly, once — this path must not warn per event

        // +-4% of pitch, off a channel of its own so the quietest realisation is not locked to the
        // lowest note forever. Narrow, for MakeDrips' reason: AudioSource.pitch resamples the WHOLE
        // clip, and a wide setting transposes an owl into a pigeon.
        float pitch = 0.96f + 0.08f * Haunt.Hash(slot, NightCallPitchChannel);

        // ---- AND WHERE IT IS HEARD FROM, WHICH IS NOT ALWAYS WHERE IT IS -------------------------
        // In the wood the animal IS the source and this is one call. In the cellar the WINDOW is the
        // source — see _windowMouth for the user's sentence and for why an emitter out in the trees
        // fails it by fifty-five degrees. Two numbers change and NOTHING ELSE does: the position
        // becomes the opening's, and the gain picks up the two legs of the path the sound really
        // travels. The clip, the slot, the deck, the skip, the pitch and the authored per-animal
        // Gain are byte-for-byte the wood's, which is "in der selben Intensität wie im Wald"
        // ("at the same intensity as in the forest").
        Vector3 heardAt = from;
        float heardGain = voice.Gain;
        if (_windowMouth != null)
        {
            heardAt = _windowMouth.position;

            //  LEG 1 — THE OPEN AIR OUTSIDE, from the animal to the wall. It is the voice's OWN
            //  rolloff curve evaluated over that distance: Unity's logarithmic mode is flat inside
            //  minDistance and falls as min/d beyond it, and this is that expression. Not a new
            //  model, not a new constant — the same curve the same call would get in the wood at the
            //  same distance. The distance is in PERCEIVED metres, which is what MinMeters is
            //  authored in; _builtScale is world units per perceived metre (see THE SCALE PROBLEM),
            //  and the two positions are both world.
            float legMeters = Vector3.Distance(from, heardAt) / Mathf.Max(_builtScale, 1e-4f);
            float leg = Mathf.Min(1f, voice.MinMeters / Mathf.Max(legMeters, 1e-3f));

            //  LEG 2 — GETTING IN, which is the wall. See WindowInsertion: -13.1 dB, from the
            //  opening's area against the room's absorption area, by the standard facade formula.
            heardGain = voice.Gain * leg * WindowInsertion;

            //  ...and LEG 3, from the opening to the ear, is Unity's, off this source's own
            //  minDistance/maxDistance — unchanged from the wood's. It is FLAT across this whole
            //  room by construction (the smallest MinMeters in the table is the owl's 8 perceived m
            //  and the cellar is about 9 perceived m corner to corner), which is not a rounding
            //  convenience: the S/A term above is a DIFFUSE level, the same everywhere in the
            //  receiving room, so a call that did not attenuate across the cellar is what the model
            //  actually says.
        }

        PlayShot(EnvSoundBank.Bank(voice.Clip), heardAt,
                 heardGain, voice.MinMeters, voice.MaxMeters, pitch);
    }

    /// <summary>
    /// THE APPARITIONS. One cue per event, resolved from <see cref="Haunt.Resolve"/> — the shared
    /// schedule, not a timer — so the cue belongs to the apparition on every client at once, and a
    /// FORCED event from the Advanced-menu test buttons makes its sound too (the user will judge
    /// this feature by pressing those buttons).
    ///
    /// <para><b>THE CUES ARE NOT LOCKED TO THE VISUAL, and that is the design.</b> Each card carries
    /// a LEAD: a negative one puts the sound before the apparition, a positive one after. A cue that
    /// lands exactly on the reveal announces the event and converts a doubt into a fact; one that
    /// arrives a moment early makes the player look, and one that arrives after makes them doubt
    /// what they just saw. Both serve "something you are not sure you saw", which is what these
    /// apparitions are built to be. No stingers, no impacts, nothing with a fast attack — see
    /// <see cref="EnvSoundBank"/>'s note on why the frightening sound is never the loud one.</para>
    /// </summary>
    private static void TickHaunt(SkyStyle style, float clock)
    {
        // The bookshelf's own contacts — its arrival on the floor, the rebound, and the righting —
        // fire from here, BEFORE the gate below, so a schedule that was written while the switch
        // was on completes even if the player turns the easter eggs off mid-event. An apparition
        // that is already lying on the floor has to be allowed to get up again.
        TickDeferredCues(clock);

        // THE LATCH OVERRIDES THE SWITCH, and until ModBuild 148 this line did not know that. The
        // Advanced-menu test buttons latch one apparition on regardless of `EasterEggs`
        // (Haunt.Force: "IT OVERRIDES EasterEggs FOR AS LONG AS IT STANDS"), and Haunt.Resolve
        // returns a forced slot as Live whatever the setting says — so a tester with the setting
        // off, which is precisely the tester who is using the buttons to decide whether to turn it
        // on, saw the apparition and heard NOTHING. Pre-existing, cheap, and fixed here rather than
        // filed: `Forcing` is one field read on a path that already reads one.
        //
        // The gate is still worth having with the latch folded in: with the setting off and nothing
        // latched, Resolve would return Live = false anyway (it multiplies by a master of 0), so
        // this is purely the cheap path — one bool read instead of a slot resolution per frame.
        if (!Haunt.EasterEggs.Value && !Haunt.Forcing)
            return;

        Haunt.Slot slot = Haunt.Resolve(clock, style);
        if (!slot.Live)
            return;

        bool audible = CueFor(style, slot.Card, out EnvSoundClip clip, out float gain,
                              out float lead, out float minM, out float maxM);

        float at = slot.StartClock + lead;
        if (clock < at)
            return;

        // Fire once per (start, card), and on TWO SEPARATE LATCHES — one for the events the shared
        // schedule produces and one for the Advanced menu's forced ones.
        //
        // WHY TWO, AND IT IS THE ANSWER TO A REPORTED DEFECT. Player.log:8629 and :8691 both schedule
        // the SAME shelf event (start 432.56s), 15 s apart, and both then have every contact dropped
        // as stale. The single latch had not failed on its own terms — it had been OVERWRITTEN. The
        // tester was using the test buttons: he latched apparition 0 (:8563, :8598), which wrote
        // (430.45, card 0) and then (437.35, card 0) over the latch; released it at 441.28, at which
        // point Resolve went back to the real schedule and returned card 5 — a different pair, so it
        // fired; then latched apparition 1 at 447.73 (another overwrite) and released THAT at 456.03,
        // at which point card 5 was still running and was, by the same argument, a different pair
        // again. A forced event is a different STREAM of events and must not be able to make the
        // scheduled stream forget what it has already played.
        //
        // ONE LATCH PER STREAM IS ENOUGH, and that is a property of the schedule rather than an
        // assumption: scheduled events never overlap (the bake measures the shortest quiet gap
        // between the end of one and the start of the next at 27 s), so between two visits to the
        // same scheduled event there can be no OTHER scheduled event to evict it.
        bool seen = slot.Forced
            ? Mathf.Approximately(_lastForcedStart, slot.StartClock) && _lastForcedCard == slot.Card
            : Mathf.Approximately(_lastHauntStart, slot.StartClock) && _lastHauntCard == slot.Card;
        if (seen)
            return;
        // ...and never fire for an event that has already finished — which is what would otherwise
        // happen on the frame the environment stands up in the middle of a slot.
        float runs = Haunt.CardSeconds(style, slot.Card) * slot.DurationMul;
        if (clock > slot.StartClock + runs + 1.5f)
            return;

        if (slot.Forced)
        {
            _lastForcedStart = slot.StartClock;
            _lastForcedCard = slot.Card;
        }
        else
        {
            _lastHauntStart = slot.StartClock;
            _lastHauntCard = slot.Card;
        }

        // A SILENT CARD IS DEBOUNCED LIKE ANY OTHER and then simply makes no sound — the lines above
        // have already run. Doing it in that order rather than returning early is what keeps one
        // apparition equal to at most one visit to this code, so a silent card cannot re-enter on the
        // next frame and cannot schedule anything twice.
        if (!audible)
            return;

        // ================================ THE MID-FLIGHT JOIN =========================================
        //
        //  ALL OF THE EVENT OR NONE OF IT. Until ModBuild 150 this method would fire a cue up to the
        //  event's whole run plus 1.5 s late, on the reasoning that a cue is better than silence when
        //  the environment stands up in the middle of a slot. The bookshelf disproved it, and the
        //  proof is in the user's log rather than in an argument: at Player.log:8630 the creak fired
        //  8.7 s into a 25 s event, and the contacts it then scheduled were ABSOLUTE times on the
        //  shared clock (which is right — see ScheduleShelfContacts) that had ALREADY PASSED. Three
        //  lines later all of them were dropped as stale (:8631, :8632). So the player heard the
        //  bookcase begin to lean and then never heard it land, twice, which is worse than either
        //  hearing the whole thing or hearing nothing: a cue with no consequence is a cue that says
        //  the feature is broken.
        //
        //  So the cue's own lateness is now BUDGETED, and the budget is DeferredStaleSeconds — the
        //  same constant the deferred queue drops on — because that is what makes the whole event
        //  coherent by construction rather than by coincidence. If the lead cue is inside its budget,
        //  every contact behind it is at least (its own phase offset - the budget) ahead of the
        //  clock: for the shelf the nearest is the arrival at +4.5 s, so it is still 2.4 s in the
        //  future and the queue cannot drop it. If the lead cue is outside the budget, nothing at all
        //  is played for this event.
        //
        //  IT IS DEBOUNCED FIRST, deliberately: the event is latched above, so a skipped event is
        //  skipped ONCE and cannot be reconsidered on the next frame as the clock walks further past
        //  it. And it is logged once, because "the shelf fell and I heard nothing" has to be
        //  attributable to a decision rather than to a hole.
        if (clock > at + DeferredStaleSeconds)
        {
            VRLog.Info("Core", $"ENV SOUND {style} card {slot.Card} SKIPPED ENTIRELY — this client " +
                               $"reached the event {clock - at:F2}s after its cue was due (cue at " +
                               $"{at:F2}s, event starts {slot.StartClock:F2}s and runs {runs:F2}s, " +
                               $"clock {clock:F2}s), which is past the {DeferredStaleSeconds:F1}s " +
                               "budget. Joining an event in flight means every contact behind the " +
                               "lead cue is already in the past, and a creak with no landing behind " +
                               "it is worse than silence — so the WHOLE event is silent. Nothing is " +
                               "broken; the next event plays in full. This is normally the " +
                               "environment standing up mid-slot, or an Advanced-menu latch being " +
                               "released over a scheduled event that was already running.");
            return;
        }

        Vector3 pos = HauntPosition(slot.Card);

        // The cellar's bookshelf (card 5) is the one apparition whose sound is not a hint but a
        // physical consequence — it tips over, hits a stone floor and later rights itself. Its cues
        // ALSO come off the shelf's own node rather than off the apparition catalogue: the creak is
        // the carcass taking the lean, so it sounds from the middle of the standing body, and the
        // contacts sound from the floor (ScheduleShelfContacts). Before ModBuild 150 both came from
        // the catalogue's bounds centre, i.e. from the middle of the room.
        bool shelf = style == SkyStyle.Cellar && slot.Card == 5;
        Vector3 cueAt = shelf ? ShelfCarcass(pos) : pos;

        PlayShot(EnvSoundBank.Bank(clip), cueAt, gain, minM, maxM, 1f);

        if (shelf)
            ScheduleShelfContacts(slot.StartClock, runs, clock, pos);

        VRLog.Info("Core", $"ENV SOUND haunt cue: {style} card {slot.Card} -> {clip} at shared clock " +
                           $"{clock:F2}s, lead {lead:+0.00;-0.00}s on an event that starts " +
                           $"{slot.StartClock:F2}s and runs {runs:F2}s" +
                           (slot.Forced ? " — FORCED from the Advanced menu" : " — scheduled") +
                           $". Gain {gain:F3} before master; position {cueAt:F2}" +
                           (shelf ? " (the SHELF's own carcass, not the apparition catalogue)" : "") +
                           ".");
    }

}
