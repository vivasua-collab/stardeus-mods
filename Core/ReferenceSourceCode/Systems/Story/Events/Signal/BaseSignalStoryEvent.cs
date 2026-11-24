using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using KL.Clock;
using Game.Systems.Trade;
using Game.Utils;

namespace Game.Systems.Story {
    public abstract class BaseSignalStoryEvent : StoryEvent {
        protected const string Key_ReqDefIdH = "sig.req.def";
        protected const string Key_RewDefIdH = "sig.rew.def";
        protected const string Key_ReqQuantity = "sig.req.qtt";
        protected const string Key_RewQuantity = "sig.rew.qtt";
        protected const string Key_RewardMoney = "sid.rew.money";
        protected const string Key_ReqMoney = "sid.req.money";
        protected SignalReqConfig req;
        protected Rng rng;
        protected abstract string ProcessReq(StoryContext ctx, StoryEventData data);
        protected void RemoveSignalSO(StoryContext ctx, StoryEventData data) {
            var so = data.TargetSO;
            if (so == null) { return; }
            if (data.State != StoryEventState.Ending) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
            D.Warn("Removing signal SO: {0}", so);
            data.TargetSO = null;
            SOUtils.RemoveSO(so);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var so = data.TargetSO;
            rng = new Rng(Hashes.Quick(so.Seed, Primes.Prime066));
            req = SignalReqConfig.RandomFor(Id, rng);
            if (req == null) {
                data.FailEvent(ctx.S, $"Failed getting random signal event for id: {Id}");
                return;
            }
            var vars = data.GetOrCreateData();
            var reqMsg = ProcessReq(ctx, data);
            vars.SetString("ReqMsg", reqMsg);

            var dd = The.Dialog.ShowChoice(IconId.CSignal, reqMsg,
                () => OnAccept(ctx, data),
                () => OnReject(ctx, data),
                T.VerbAccept, T.VerbRefuse, canCloseWithEsc: false,
                    () => OnShow(ctx, data),
                    sideText: null,
                    textTplHash: Hashes.S(req.Id), isSkippable: false);
            dd.OverrideSortOrder = 30;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (data.TargetSO?.Parent == null) {
                data.FailEvent(ctx.S, T.AdRejTargetUnavailable);
                return;
            }
            // var vars = data.GetOrCreateData();
            // if (!vars.TryGetString("ReqMsg", out _)) {
            //     // for load
            //     OnStart(ctx, data);
            // }
        }

        private void OnAccept(StoryContext ctx, StoryEventData data) {
            if (req.IsImmediateChoice) {
                Ready.SetUserInput(true, "Signal");
                The.ScreenBlock.HideImmediate();
                ctx.S.Clock.ForceResume(ForceStopReason.Choice);
            }
            Accept(ctx, data);
        }

        private void OnReject(StoryContext ctx, StoryEventData data) {
            if (req.IsImmediateChoice) {
                Ready.SetUserInput(true, "Signal");
                The.ScreenBlock.HideImmediate();
                ctx.S.Clock.ForceResume(ForceStopReason.Choice);
            }
            Reject(ctx, data);
        }

        protected abstract void Accept(StoryContext ctx, StoryEventData data);

        protected abstract void Reject(StoryContext ctx, StoryEventData data);

        private void OnShow(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            s.Clock.SetSpeed(ClockSpeed.Stopped);
            if (req.IsImmediateChoice) {
                UIUtils.CloseFullScreenUI();
                Ready.SetUserInput(false, "Signal");
                s.Clock.ForceStop(ForceStopReason.Choice);
                The.ScreenBlock.SetCenterText(T.Waiting, 0f);
                The.ScreenBlock.SetSideText(string.Empty);
                The.ScreenBlock.Show();
            }
        }

        protected void TryPay(StoryContext ctx, StoryEventData data, float reqMoney) {
            if (!ctx.S.Wallet.TryPay(reqMoney, null)) {
                data.FailReason = T.TradeNotEnoughCash;
            } else {
                data.FailReason = null;
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }


        protected bool CheckTeleporter(StoryContext ctx, StoryEventData data,
                out TradePortalComp tradePortal) {
            tradePortal = TradeUI.FindTradePortal(ctx.S);
            if (tradePortal != null) {
                return true;
            }
            var anyTradePortal = ctx.S.Components.FindFirstByType<TradePortalComp>();
            if (anyTradePortal != null) {
                data.FailReason = $"<br>{Texts.Red(T.NotReady)}: {Texts.LinkToTile(anyTradePortal.Tile)}.";
            } else {
                data.FailReason = $"<br>{Texts.Red(T.NotAvailable)}: {Texts.LinkToCodex(DefIdsH.ObjectsDevicesTradePortal)}.";
            }
            return false;
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            if (data.TargetSO?.Parent == null) {
                data.FailEvent(ctx.S,
                    $"Failed loading signal story event, no SO: {Id}");
            }
        }
    }
}
