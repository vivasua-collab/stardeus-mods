using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;

namespace Game.Systems.Inventory {
    // The shared "may this requester haul that material right now" decision, used by
    // every all-or-nothing material gate. See Docs/Architecture/Systems/MaterialClaims.md
    public static class MatClaimGate {
        // Failing gates re-check on this cadence instead of the escalating 30min..2h
        // backoff: a claim clears, or new stock is produced, at unpredictable moments and
        // a task that waited out a 2h backoff would leave that stock sitting unused.
        public const int RetryTicks = Consts.TicksPer5Minutes;

        public static MatClaimVerdict Evaluate(GameState s, IMatRequester requester,
                MatRequest next, int priority) {
            if (next?.Type == null) { return MatClaimVerdict.Lacking; }
            var inv = s.Sys.Inventory;
            var ticks = G.Ticks;
            StakeShortfalls(inv, requester, priority, ticks);

            var type = next.Type;
            if (inv.HasEnoughFor(type, next.Amount, requester, priority)) {
                return MatClaimVerdict.Available;
            }
            // AllowedCount is a cached upper bound on what the HasEnough storage scan
            // can find, so a zero there answers this without scanning at all
            bool hasAny = inv.AllowedCount(type) > 0 && inv.HasEnough(type, 1);
            if (inv.Claims.IsHeldBy(type, requester, ticks)) {
                return hasAny ? MatClaimVerdict.Trickle : MatClaimVerdict.Lacking;
            }
            return hasAny ? MatClaimVerdict.Reserved : MatClaimVerdict.Lacking;
        }

        public static bool CanHaul(GameState s, IMatRequester requester,
                MatRequest next, int priority) {
            var verdict = Evaluate(s, requester, next, priority);
            return verdict == MatClaimVerdict.Available
                || verdict == MatClaimVerdict.Trickle;
        }

        public static AIGoalCheckResult CheckHaulGoal(GameState s,
                IMatRequester requester, MatRequest next, int priority) {
            switch (Evaluate(s, requester, next, priority)) {
                case MatClaimVerdict.Available:
                case MatClaimVerdict.Trickle:
                    return AIGoalCheckResult.Doable;
                case MatClaimVerdict.Reserved:
                    // Not a shortage - the material exists, it is just spoken for - so
                    // this must not feed the colony wide "Lacking Materials" alert
                    return AIGoalCheckResult.Fail(T.AdRejMatReserved, true,
                        IconId.CLocked, RetryTicks);
                default:
                    requester.NotifyMatDeficit(next.Type);
                    return AIGoalCheckResult.Fail(next.Type.LackRejectionT, true,
                        IconId.CLackingMaterials, RetryTicks);
            }
        }

        // Earmark every material this requester is short of, not just the one it is
        // about to fetch, so a blueprint collecting plentiful steel does not lose its
        // hold on the scarce material it will need next.
        private static void StakeShortfalls(InventorySys inv, IMatRequester requester,
                int priority, long ticks) {
            var missing = requester.MissingMats;
            if (missing == null) { return; }
            for (int i = 0; i < missing.Count; i++) {
                var m = missing[i];
                if (m == null || m.IsCancelled || m.Amount < 1 || m.Type == null) {
                    continue;
                }
                // Cached upper bound rather than a storage scan: over-reporting stock
                // here only means we skip an earmark nobody was competing for
                if (inv.AllowedCount(m.Type) >= m.Amount) {
                    // Plenty for everyone - nothing worth earmarking
                    inv.Claims.ReleaseIf(m.Type, requester);
                    continue;
                }
                inv.Claims.Stake(m.Type, requester, m.Amount, priority, ticks);
            }
        }
    }
}
