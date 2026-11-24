using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Data.Factions;
using KL.Randomness;
using Game.Systems.Trade;
using Game.Systems.Notify;

namespace Game.Components {
    public sealed class StasisPodMerchantComp : BaseComponent<StasisPodMerchantComp>,
            IWithFaction, ITickListener {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StasisPodMerchantComp());
        }

        public bool IsTraderActive;

        public int Seed;
        public bool CanTrade;

        public TradeUI Trade;
        public bool IsTradeEnded => !entity.IsActive ||
                (Trade != null && Trade.IsComplete);

        public FactionDef Faction { get; set; }
        private uint traderId;
        public Trader Trader;
        private NotificationHandle tradeEvent;
        private StasisPodComp stasisPod;

        public void SetWillingToTrade(bool isWilling) {
            if (isWilling) {
                willLaunchAfterTrade = true;
                if (!tradeEvent.IsValid) {
                    tradeEvent = Notification.BuildFor(Tile, IconId.CTrade,
                        T.MerchantOfferingTrade, 0, NotificationType.Immediate)
                        .WithOnClick(OpenTradeView)
                        .Group("merch_trade")
                        .Sticky()
                        .Show();
                }
                ShowInfoIcon(IconId.CTrade, T.WillingToTrade);
                Faction ??= S.Sys.Diplomacy.RandomFactionForTrade();
            } else {
                if (tradeEvent.IsValid) {
                    Notification.Remove(ref tradeEvent);
                }
                HideInfoIcon();
            }
            CanTrade = isWilling;
        }

        protected override void OnConfig() {
            IsTraderActive = false;
            CanTrade = false;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsActive", IsTraderActive);
            data.SetBool("CanTrade", CanTrade);
            data.SetBool("WillLaunchAfterTrade", willLaunchAfterTrade);
            if (IsTraderActive) {
                data.SetInt("Seed", Seed);
                data.SetString("FactionId", Faction?.Id);
                if (Trader != null) {
                    data.SetUInt("TraderId", Trader.Id);
                }
            }
        }

        protected override void OnLoad(ComponentData data) {
            IsTraderActive = data.GetBool("IsActive", IsTraderActive);
            CanTrade = data.GetBool("CanTrade", CanTrade);
            willLaunchAfterTrade = data.GetBool("WillLaunchAfterTrade", willLaunchAfterTrade);
            if (IsTraderActive) {
                Seed = data.GetInt("Seed", 0);
                var facId = data.GetString("FactionId", null);
                if (facId != null) {
                    Faction = FactionDef.Get(facId);
                }
                data.TryGetUint("TraderId", out traderId);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            stasisPod = Entity.GetRequiredComponent<StasisPodComp>(this);
            if (IsTraderActive) {
                Ready.When(WhenH.AllSystemsLoaded, () => {
                    SetWillingToTrade(CanTrade);
                });
            }
        }

        private bool hasAffectedStanding;
        private bool willLaunchAfterTrade;


        public override void Receive(IComponent sender, int message) {
            if (!IsTraderActive || message != MsgIdH.DeconstructionOngoing) {
                return;
            }
            if (!hasAffectedStanding) {
                hasAffectedStanding = true;
                if (!Faction.IsBlank && !Faction.IsInternal) {
                    S.Query.ChangeFactionAttitude.Ask(Faction,
                        FactionDef.Player, -0.03f);
                }
            }

            SpawnExplosive();

            IsTraderActive = false;
            willLaunchAfterTrade = true;

            if (Trade != null) {
                Trade.EndTrade();
            } else {
                EndTrading(null);
            }
        }

        private void SpawnExplosive() {
            var rng = new Rng(Seed);
            int count = rng.Range(1, Mathf.CeilToInt(4 * S.Difficulty.Level));
            EntityUtils.SpawnExplosive(Faction, Tile.Transform.WorkSpotOrPos, rng, count);
        }

        public override string ToString() {
            return $" * Merchant [Seed {Seed}]";
        }

        public override void OnRemove() {
            if (tradeEvent.IsValid) {
                Notification.Remove(ref tradeEvent);
            }
            Trade?.EndTrade();
        }

        public void OpenTradeView() {
            if (Seed == 0) {
                Seed = Rng.UInt;
            }
            IsTraderActive = true;
            if (Trader == null) {
                if (traderId != 0) {
                    Trader = S.Query.FindTrader.Ask(traderId);
                }
                if (Trader == null) {
                    var rng = new Rng(Seed);
                    Faction ??= S.Sys.Diplomacy.RandomFactionForTrade();
                    var req = new TraderRequest();
                    req.SO = S.Universe.Player.Parent;
                    req.WealthFactor = rng.Curve(TraderRequest.WealthCurvePoor);
                    req.Rng = rng;
                    req.Faction = Faction;
                    Trader = S.Query.CreateTrader.Ask(req);
                }
            }
            if (Trade != null) {
                Trade.Show();
                return;
            }
            var pos = Entity.PosIdx;
            if (Tile?.Transform.HasOutPort == true) {
                pos = Tile.Transform.OutPort;
            }
            Trade = S.Query.StartTradeLocal.Ask(Trader, pos);
            if (tradeEvent.IsValid) {
                Notification.Remove(ref tradeEvent);
            }
            Trade.OnEnd = EndTrading;
        }

        public void EndTrading(TradeUI trade) {
            SetWillingToTrade(false);
            S.Sig.RemoveTrader.Send(Trader);
            TryLaunchAfterTrade();
        }

        private void TryLaunchAfterTrade() {
            if (willLaunchAfterTrade) {
                if (CanTrade || A.IsInHyperspace) {
                    return;
                }
                willLaunchAfterTrade = false;
                stasisPod.Launch();
            }
        }

        public void Tick(long currentTick) {
            TryLaunchAfterTrade();
        }
    }
}
