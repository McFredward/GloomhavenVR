using System;

namespace GloomhavenVR.Core.SteamLink
{
    internal enum RegistrationPairRefusal
    {
        None, Stopped, Faulted, InvalidCohort, IncompletePair, StaleCohort,
        PairIdOverflow, ForeignContext, UnknownEye, DuplicateEye, EyeNotBegun,
        AlreadyCompleted, PairAlreadyStarted
    }

    // An immutable APP-owned reservation. EngineFrameCohort is only a Unity
    // scheduling cohort, never a native encoded/video/display frame identity.
    internal sealed class RegistrationPairContext
    {
        public string Session { get; }
        public ulong PairId { get; }
        public long EngineFrameCohort { get; }
        public int Phase { get; }

        internal RegistrationPairContext(string session, ulong pairId, long engineFrameCohort)
        {
            if (!RegistrationSource.IsSession(session) || pairId == 0 || engineFrameCohort < 0)
                throw new ArgumentException("Invalid app registration reservation.");
            Session = session;
            PairId = pairId;
            EngineFrameCohort = engineFrameCohort;
            Phase = RegistrationSource.PhaseForPair(pairId);
        }
    }

    // No Unity, clock, textures or GPU state. Caller serialization is required.
    // Prepare in Update, then begin/commit only the two real eye callbacks.
    // Commit means recorded CPU callback success, NOT GPU/native completion.
    internal sealed class RegistrationPairPolicy
    {
        public string Session { get; }
        public bool IsStopped { get; private set; }
        public bool IsFaulted { get; private set; }
        public RegistrationPairRefusal FirstFailure { get; private set; }
        public ulong LastReservedPairId { get; private set; }
        public RegistrationPairContext? Pending { get; private set; }
        public RegistrationPairContext? LastCompleted { get; private set; }
        private long _lastCohort = -1;
        private int _begun, _completed;

        public RegistrationPairPolicy() : this(Guid.NewGuid(), 0) { }

        // Deterministic portable-control seam. A real new run uses the fresh
        // parameterless constructor, not a stored nonce or resumed pair counter.
        internal RegistrationPairPolicy(Guid session, ulong lastReservedPairId = 0)
        {
            if (session == Guid.Empty)
                throw new ArgumentException("A fresh nonzero app session is required.", nameof(session));
            Session = session.ToString("N");
            LastReservedPairId = lastReservedPairId;
        }

        public bool TryPreparePair(long engineFrameCohort, out RegistrationPairContext? context,
                                   out RegistrationPairRefusal reason)
        {
            context = null;
            if (!Running(out reason)) return false;
            if (engineFrameCohort < 0) return Fail(RegistrationPairRefusal.InvalidCohort, out reason);
            if (Pending != null) return Fail(RegistrationPairRefusal.IncompletePair, out reason);
            if (engineFrameCohort <= _lastCohort) return Fail(RegistrationPairRefusal.StaleCohort, out reason);
            if (LastReservedPairId == ulong.MaxValue) return Fail(RegistrationPairRefusal.PairIdOverflow, out reason);
            context = new RegistrationPairContext(Session, ++LastReservedPairId, engineFrameCohort);
            Pending = context;
            _lastCohort = engineFrameCohort;
            _begun = _completed = 0;
            reason = RegistrationPairRefusal.None;
            return true;
        }

        public bool TryBeginEye(RegistrationPairContext context, long engineFrameCohort, ProbeEye eye,
                                out RegistrationPairRefusal reason)
        {
            if (!Current(context, out reason)) return false;
            if (engineFrameCohort != context.EngineFrameCohort)
                return Fail(RegistrationPairRefusal.StaleCohort, out reason);
            int bit = EyeBit(eye);
            if (bit == 0) return Fail(RegistrationPairRefusal.UnknownEye, out reason);
            if ((_begun & bit) != 0) return Fail(RegistrationPairRefusal.DuplicateEye, out reason);
            _begun |= bit;
            reason = RegistrationPairRefusal.None;
            return true;
        }

        public bool CompleteEye(RegistrationPairContext context, ProbeEye eye, out RegistrationPairRefusal reason)
        {
            if (!Current(context, out reason)) return false;
            int bit = EyeBit(eye);
            if (bit == 0) return Fail(RegistrationPairRefusal.UnknownEye, out reason);
            if ((_completed & bit) != 0) return Fail(RegistrationPairRefusal.AlreadyCompleted, out reason);
            if ((_begun & bit) == 0) return Fail(RegistrationPairRefusal.EyeNotBegun, out reason);
            _completed |= bit;
            if (_completed == 3)
            {
                LastCompleted = context;
                Pending = null;
            }
            reason = RegistrationPairRefusal.None;
            return true;
        }

        // Slot pressure may skip an entire prepared cohort BEFORE any eye starts.
        // Consuming but not presenting an ID is safe; relabeling a half pair is not.
        public bool DiscardPrepared(RegistrationPairContext context, out RegistrationPairRefusal reason)
        {
            if (!Current(context, out reason)) return false;
            if (_begun != 0) return Fail(RegistrationPairRefusal.PairAlreadyStarted, out reason);
            Pending = null;
            reason = RegistrationPairRefusal.None;
            return true;
        }

        public bool IsPairComplete(RegistrationPairContext context) =>
            !IsFaulted && !IsStopped && ReferenceEquals(LastCompleted, context);

        public void Stop() { IsStopped = true; Pending = null; }

        private bool Current(RegistrationPairContext context, out RegistrationPairRefusal reason)
        {
            if (!Running(out reason)) return false;
            if (!ReferenceEquals(Pending, context) || context == null)
                return Fail(RegistrationPairRefusal.ForeignContext, out reason);
            reason = RegistrationPairRefusal.None;
            return true;
        }

        private bool Running(out RegistrationPairRefusal reason)
        {
            reason = IsFaulted ? RegistrationPairRefusal.Faulted :
                     IsStopped ? RegistrationPairRefusal.Stopped : RegistrationPairRefusal.None;
            return reason == RegistrationPairRefusal.None;
        }

        private bool Fail(RegistrationPairRefusal failure, out RegistrationPairRefusal reason)
        {
            if (!IsFaulted) FirstFailure = failure;
            IsFaulted = true;
            Pending = null;
            reason = failure;
            return false;
        }

        private static int EyeBit(ProbeEye eye) => eye == ProbeEye.Left ? 1 : eye == ProbeEye.Right ? 2 : 0;
    }
}
