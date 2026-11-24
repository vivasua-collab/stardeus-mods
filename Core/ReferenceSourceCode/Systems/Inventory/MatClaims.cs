using System.Collections.Generic;
using Game.Components;
using Game.Constants;

namespace Game.Systems.Inventory {
    // Colony-wide earmarks on scarce materials.
    // See Docs/Architecture/Systems/MaterialClaims.md
    public sealed class MatClaims {
        // A claim never survives a reload - it is derived from what requesters are
        // currently missing, and every doability gate re-stakes it as it runs.
        private struct Claim {
            public IMatRequester Owner;
            public int Amount;
            public int Priority;
            public long StakedTick;
        }

        private readonly Dictionary<MatType, Claim> claims = new();
        private readonly List<MatType> toRelease = new();

        // A claim that stops being re-staked (paused order, cancelled demand we have no
        // hook for) must not hold stock hostage forever. Every gate that can stake also
        // re-stakes on each run, so this only fires for owners that went quiet.
        private const long ExpiryTicks = Consts.TicksPer2Hours;

        // Ties go to the incumbent: that stability is what stops several equal priority
        // demands from taking turns and splitting scarce stock so none of them completes.
        public static bool CanPreempt(int incumbentPriority, int challengerPriority) {
            return challengerPriority > incumbentPriority;
        }

        // Returns true if owner holds the claim on type after this call
        public bool Stake(MatType type, IMatRequester owner, int amount, int priority,
                long ticks) {
            if (type == null || owner == null || amount < 1) { return false; }
            if (priority < 1) {
                // Forbidden tasks release whatever they were holding
                ReleaseIf(type, owner);
                return false;
            }
            if (claims.TryGetValue(type, out var claim)) {
                if (claim.Owner != owner) {
                    if (IsValid(type, claim, ticks)
                            && !CanPreempt(claim.Priority, priority)) {
                        return false;
                    }
                }
            }
            claims[type] = new Claim {
                Owner = owner,
                Amount = amount,
                Priority = priority,
                StakedTick = ticks,
            };
            return true;
        }

        // How much of type the asker must leave alone for a stronger demand
        public int Reserved(MatType type, IMatRequester asker, int askerPriority,
                long ticks) {
            if (type == null) { return 0; }
            if (!claims.TryGetValue(type, out var claim)) { return 0; }
            if (claim.Owner == asker) { return 0; }
            if (!IsValid(type, claim, ticks)) {
                claims.Remove(type);
                return 0;
            }
            if (CanPreempt(claim.Priority, askerPriority)) { return 0; }
            return claim.Amount;
        }

        public bool IsHeldBy(MatType type, IMatRequester owner, long ticks) {
            if (type == null || owner == null) { return false; }
            if (!claims.TryGetValue(type, out var claim)) { return false; }
            if (claim.Owner != owner) { return false; }
            if (!IsValid(type, claim, ticks)) {
                claims.Remove(type);
                return false;
            }
            return true;
        }

        public void ReleaseIf(MatType type, IMatRequester owner) {
            if (type == null || owner == null) { return; }
            if (claims.TryGetValue(type, out var claim) && claim.Owner == owner) {
                claims.Remove(type);
            }
        }

        public void Sweep(long ticks) {
            if (claims.Count == 0) { return; }
            toRelease.Clear();
            foreach (var kv in claims) {
                if (!IsValid(kv.Key, kv.Value, ticks)) {
                    toRelease.Add(kv.Key);
                }
            }
            for (int i = 0; i < toRelease.Count; i++) {
                claims.Remove(toRelease[i]);
            }
            toRelease.Clear();
        }

        public void Clear() {
            claims.Clear();
            toRelease.Clear();
        }

        private static bool IsValid(MatType type, in Claim claim, long ticks) {
            if (ticks - claim.StakedTick > ExpiryTicks) { return false; }
            var owner = claim.Owner;
            var entity = owner?.Entity;
            if (entity == null || !entity.IsActive) { return false; }
            // A claim only means anything while its owner is still waiting for that
            // exact material - it is checked against live demand rather than released
            // by every call site that can satisfy or drop one.
            var missing = owner.MissingMats;
            if (missing == null) { return false; }
            for (int i = 0; i < missing.Count; i++) {
                var m = missing[i];
                if (m == null || m.IsCancelled || m.Amount < 1) { continue; }
                if (m.Type == type) { return true; }
            }
            return false;
        }
    }
}
