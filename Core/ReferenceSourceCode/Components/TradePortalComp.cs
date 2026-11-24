using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX.Codex.Pages;
using Game.Data;

namespace Game.Components {
    public sealed class TradePortalComp : BaseComponent<TradePortalComp>, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            notAvailable = null;
            AddComponentPrototype(new TradePortalComp());
        }

        private const float DefaultEnergyCost = 3f;
        private float energyCost;
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        private static string notAvailable;
        public static string NotAvailable {
            get {
                return notAvailable ??= Texts.AutoLink(Texts.Red(T.TradePortalNotAvailable),
                    The.Defs.FastGet(DefIdsH.ObjectsDevicesTradePortal));
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(TradePage.PageId, 3f);
                }
            }
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * TraderPortal";
        }
    }
}
