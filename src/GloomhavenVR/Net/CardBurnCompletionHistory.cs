using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Durable terminal release with the existing immutable card-pool provenance.
/// Neither card names nor card data IDs cross the wire. Sequence only correlates legacy62;
/// original provenance plus owner time identifies a release across sequence wrap and packet loss.</summary>
internal readonly struct CardBurnCompletion
{
    internal const byte RecentFlightBit = 2;
    internal byte FlightFlags => (byte)(Flags & CardFlightVisibility.CoveredBurnBit);
    internal readonly byte Sequence, Endpoints, Flags, Seat, Count;
    internal readonly float Time;
    internal readonly int ActorId, SourceActorId;
    internal readonly ushort PoolSeat, PoolCount;
    internal CardBurnCompletion(byte sequence, byte endpoints, byte flags, float time,
        int actor, int sourceActor, ushort poolSeat, ushort poolCount, byte seat, byte count)
    {
        Sequence=sequence; Endpoints=endpoints; Flags=flags; Time=time;
        ActorId=actor; SourceActorId=sourceActor; PoolSeat=poolSeat; PoolCount=poolCount;
        Seat=seat; Count=count;
    }
    internal CardFlightSource Source => new(ActorId, Seat, Count);
    internal (int Actor, int Source, ushort Seat, ushort Count) Key => (ActorId, SourceActorId, PoolSeat, PoolCount);
    internal CardBurnCompletion WithSequence(byte sequence) => new CardBurnCompletion(sequence, Endpoints, Flags, Time,
        ActorId, SourceActorId, PoolSeat, PoolCount, Seat, Count);
    internal CardBurnCompletion WithRecentFlight(bool recent) => new CardBurnCompletion(Sequence, Endpoints,
        (byte)(FlightFlags | (recent ? RecentFlightBit : 0)), Time, ActorId, SourceActorId, PoolSeat, PoolCount, Seat, Count);
    internal bool Valid() => !float.IsNaN(Time) && !float.IsInfinity(Time) && Time >= 0f
        && ActorId != 0 && SourceActorId != 0 && PoolCount > 0 && PoolCount <= 32768
        && (PoolSeat & 32767) < PoolCount && Source.Validate()
        && NetCardFx.To(Endpoints) == CardFxAnchor.Burnt && (Endpoints & 15) <= (byte)CardFxAnchor.Active
        && (Flags & ~(CardFlightVisibility.CoveredBurnBit | RecentFlightBit)) == 0;
}

/// <summary>Record75 repeats terminal releases until original recovery or scenario teardown.
/// Up to four complete32-card populations fit128 entries. Repeated75 records preserve the
/// byte-length TLV grammar; every page is independently checked before bounded merge.</summary>
internal sealed class CardBurnCompletionHistory
{
    internal const int CountMax = 128, EntrySize = 21, PageCount = 12;
    internal const int MaxPayloadSize = 2 + PageCount * EntrySize;
    internal const int MaxSize = CountMax * EntrySize + ((CountMax + PageCount - 1) / PageCount) * 4;
    internal readonly byte Sequence;
    internal readonly CardBurnCompletion[] Entries;
    internal CardBurnCompletionHistory(byte sequence, CardBurnCompletion[] entries)
    { Sequence=sequence; Entries=(CardBurnCompletion[])entries.Clone(); }
    internal bool Covers(byte sequence, byte endpoints, byte flags, CardFlightSource? source)
    {
        if (!source.HasValue) return false;
        foreach (var entry in Entries)
            if ((entry.Flags & CardBurnCompletion.RecentFlightBit) != 0
                && entry.Sequence == sequence && entry.Endpoints == endpoints && entry.FlightFlags == flags
                && entry.ActorId == source.Value.ActorId && entry.Seat == source.Value.Seat
                && entry.Count == source.Value.Count) return true;
        return false;
    }
    private bool Valid()
    {
        if (Entries.Length>CountMax) return false;
        var seen=new HashSet<(int,int,ushort,ushort)>();
        foreach(var entry in Entries) if(!entry.Valid() || !seen.Add(entry.Key)) return false;
        return true;
    }
    internal int Write(byte[] buffer, int offset)
    {
        int size=Entries.Length*EntrySize+Math.Max(1,(Entries.Length+PageCount-1)/PageCount)*4;
        if(!Valid() || offset<0 || offset>buffer.Length-size) return 0;
        int at=offset;
        for(int start=0; start<Entries.Length || start==0; start+=PageCount)
        {
            int count=Math.Min(PageCount,Entries.Length-start);
            buffer[at++]=NetProtocol.ExtIdCardBurnCompletion;
            buffer[at++]=(byte)(2+count*EntrySize);
            buffer[at++]=Sequence; buffer[at++]=(byte)count;
            for(int i=start;i<start+count;i++)
            {
                var e=Entries[i]; buffer[at++]=e.Sequence;buffer[at++]=e.Endpoints;buffer[at++]=e.Flags;
                AvatarSerializer.WriteF32(buffer,ref at,e.Time);
                Put32(buffer,ref at,e.ActorId);Put32(buffer,ref at,e.SourceActorId);
                Put16(buffer,ref at,e.PoolSeat);Put16(buffer,ref at,e.PoolCount);
                buffer[at++]=e.Seat;buffer[at++]=e.Count;
            }
        }
        return at-offset;
    }
    internal static bool TryRead(byte[] buffer,int offset,int length,out CardBurnCompletionHistory? history)
    {
        history=null;
        if(offset<0 || length<2 || length>MaxPayloadSize || offset>buffer.Length-length) return false;
        int at=offset;byte sequence=buffer[at++],count=buffer[at++];
        if(count>PageCount || length!=2+count*EntrySize) return false;
        var entries=new CardBurnCompletion[count];
        for(int i=0;i<count;i++)
        {
            byte seq=buffer[at++],ends=buffer[at++],flags=buffer[at++];float time=AvatarSerializer.ReadF32(buffer,ref at);
            int actor=Get32(buffer,ref at), sourceActor=Get32(buffer,ref at);
            ushort seat=Get16(buffer,ref at),pool=Get16(buffer,ref at);
            entries[i]=new CardBurnCompletion(seq,ends,flags,time,actor,sourceActor,seat,pool,buffer[at++],buffer[at++]);
        }
        var result=new CardBurnCompletionHistory(sequence,entries);
        if(!result.Valid())return false;
        history=result;return true;
    }
    internal static CardBurnCompletionHistory? Merge(CardBurnCompletionHistory? previous,CardBurnCompletionHistory next)
    {
        if(previous==null)return next;
        if(previous.Sequence!=next.Sequence || previous.Entries.Length+next.Entries.Length>CountMax)return null;
        var entries=new CardBurnCompletion[previous.Entries.Length+next.Entries.Length];
        Array.Copy(previous.Entries,entries,previous.Entries.Length);
        Array.Copy(next.Entries,0,entries,previous.Entries.Length,next.Entries.Length);
        var result=new CardBurnCompletionHistory(next.Sequence,entries);
        return result.Valid()?result:null;
    }
    private static void Put16(byte[] b,ref int i,ushort value){b[i++]=(byte)value;b[i++]=(byte)(value>>8);}
    private static ushort Get16(byte[] b,ref int i)=>(ushort)(b[i++]|b[i++]<<8);
    private static void Put32(byte[] b,ref int i,int value){for(int s=0;s<32;s+=8)b[i++]=(byte)(value>>s);}
    private static int Get32(byte[] b,ref int i){int value=0;for(int s=0;s<32;s+=8)value|=b[i++]<<s;return value;}
}
