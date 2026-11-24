using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Trade;
using Game.Systems.AI.Combat;
using Game.Story.Events;
using Game.Data.Factions;

namespace Game.Systems.Story {
    public sealed class SignalExtortMoneyWithRaidStoryEvent : BaseSignalStoryEvent {

        public const string EventId = "SignalExtortMoneyWithRaid";
        public override string Id => EventId;
        private const int MaxPortalRaiders = 16;

        [SelfInit]
        private static void Register() {
            Register(new SignalExtortMoneyWithRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }

        protected override string ProcessReq(StoryContext ctx, StoryEventData data) {
            var so = data.TargetSO;
            ResolveReqRew(so, out var reqMoney);

            if (!SignalMessages.TryRandomKeyFor(req.RequestMsgKeyRoot,
                    rng, out var reqMsg)) {
                reqMsg = req.RequestMsgKey ?? "... <b>{0}</b>!!!";
            }


            double wallAmt = ctx.S.Wallet.Amount;
            var frac = rng.Range(req.MoneyPercentFrom, req.MoneyPercentTo);
            double reqFrac = wallAmt * frac;
            reqMoney = Maths.ToNegotiationValue(reqFrac);
            if (reqMoney < 500) {
                reqMoney = 500;
            }

            var amtStr = Wallet.ToValueString(reqMoney, false);
            var msg = reqMsg.T(amtStr);
            var vars = data.GetOrCreateData();
            vars.SetString("AmtStr", amtStr);
            vars.SetString("ReqMsg", msg);
            return msg;
        }
        private static void ResolveReqRew(SpaceObject so, out float reqMoney) {
            reqMoney = 0f;
            if (so.HasData) {
                reqMoney = so.Data.GetFloat(Key_ReqMoney, 0f);
            }
        }

        protected override void Accept(StoryContext ctx, StoryEventData data) {
            var so = data.TargetSO;
            ResolveReqRew(so, out var reqMoney);
            TryPay(ctx, data, reqMoney);
            OnSignalEnd(ctx, data);
        }

        protected override void Reject(StoryContext ctx, StoryEventData data) {
            data.FailReason = T.Reject;
            OnSignalEnd(ctx, data);
        }

        private void OnSignalEnd(StoryContext ctx, StoryEventData data) {
            // We click a button on the dialog
            string msg;
            var hasFulfilled = false;
            if (data.State == StoryEventState.Ending) {
                hasFulfilled = true;
                if (!SignalMessages.TryRandomMsgFor(req.FulfillMsgKeyRoot, rng, out msg)) {
                    msg = req.FulfillMsgKey?.T() ?? "...";
                }
                msg = $"{T.SigTransactionFulfilled}\n\n{msg}";
                // S.Situation.AddTension(3f, "Paid extortion");
            } else {
                if (!SignalMessages.TryRandomMsgFor(req.RefuseMsgKeyRoot, rng, out msg)) {
                    msg = req.RefuseMsgKey?.T() ?? "...";
                }
                var failReason = data.FailReason;
                if (failReason == T.Reject) {
                    msg = $"{T.SigTransactionRejected}\n\n{msg}";
                } else if (failReason != null) {
                    msg = $"{T.SigTransactionFailed} {failReason}\n\n{msg}";
                } else {
                    msg = $"{T.SigTransactionFailed}\n\n{msg}";
                }
                StartRaid(ctx, data);
            }
            RemoveSignalSO(ctx, data);
            The.Dialog.ShowSimple(IconId.CSignal, msg);
            var logTitle = hasFulfilled ? T.SigTransactionFulfilledTitle :
                T.SigTransactionFailedTitle;
            var reqMsg = data.Data.GetString("ReqMsg", null);
            if (!string.IsNullOrEmpty(reqMsg)) {
                msg = $"{reqMsg}\n\n{msg}";
            } else {
                D.Err("ReqMsg is blank for signal: {0}", this);
            }
            data.CreateLogEntry(IconId.CSignal, logTitle, msg);
        }

        private void StartRaid(StoryContext ctx, StoryEventData data) {
            var teleporter = TradeUI.FindTradePortal(ctx.S);
            var wt = 1f; // TODO
            int initI = 0;
            var so = data.TargetSO;
            if (teleporter != null) {
                var raid = new CombatRaid(ctx.S, so.Seed);
                raid.PosIdx = teleporter.Tile.PosIdx;
                // A portal has one exit; a full late-game budget (up to 60) would pile onto
                // one tile. Still a real raid (a consequence of refusing), so it counts.
                raid.MaxRaiders = MaxPortalRaiders;
                if (!raid.TryStartRaid()) {
                    var fac = raid.Faction ?? ctx.S.Sys.Diplomacy.RandomFactionForRaid();
                    EntityUtils.SpawnExplosive(fac, teleporter.Tile.PosIdx);
                }
                initI = 1;
            }

            var numCapsules = Mathf.CeilToInt(wt / 10f);
            if (numCapsules == 0) {
                numCapsules = 1;
            }
            var pirates = FactionDef.FastGet(FactionIdH.Pirate);
            for (int i = initI; i < numCapsules; i++) {
                CombatSys.StartBreachCapsuleRaid(ctx.S, pirates);
            }
        }
    }
}
