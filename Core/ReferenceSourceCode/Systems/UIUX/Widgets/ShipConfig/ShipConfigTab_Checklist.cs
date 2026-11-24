using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipConfigTab_Checklist {
        private readonly ShipConfigWidget parent;
        private readonly GameState S;
        private Lifetime lifetime;

        public ShipConfigTab_Checklist(ShipConfigWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public void Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("ShipConfig.Checklist");

            int blockIdx = 0;
            AddConditionBlock(blockIdx++);
            AddFlightCapabilityBlock(blockIdx++);
        }

        private void AddConditionBlock(int blockIdx) {
            var block = UISpawn.Block(lifetime, parent.WItems, blockIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.UiGroupCondition);

            var flightCap = S.Query.GetShipFlightCondition.Ask();

            AddBoolItem(block.Container, idx++, T.ShipInOnePiece, flightCap.IsInOnePiece);
            AddBoolItem(block.Container, idx++, T.ShipHasBridgeControls, flightCap.HasBridge);
            AddBoolItem(block.Container, idx++, T.ShipHasBridgeControlsOperator, flightCap.HasBridgeOperator);
            AddEngineItem(block.Container, idx++, T.ShipEnginesReady, flightCap.HasEnginesSector);
        }

        private void AddBoolItem(RectTransform container, int idx, string name, BBool binding) {
            var w = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, container, idx);
            w.SetText(name);
            binding.Bind(lifetime, v => {
                w.SetImage(v ? IconId.CCheck : IconId.CCross);
                w.SetText2(v ? T.Yes : T.No);
            });
        }

        private void AddEngineItem(RectTransform container, int idx, string name, BBool binding) {
            var w = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, container, idx);
            w.SetText(name);
            binding.Bind(lifetime, v => {
                w.SetImage(v ? IconId.CCheck : IconId.CCross);
                w.SetText2(v ? T.Yes : T.No);
            });
        }

        private void AddFlightCapabilityBlock(int blockIdx) {
            var block = UISpawn.Block(lifetime, parent.WItems, blockIdx);
            int idx = 0;

            var headerBtn = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            headerBtn.Load(T.ShipNavFlightCapability);

            S.Sys.ShipNav.CheckFlightReady(out _, out _, out _, out _);

            // Planet capable
            var planetW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            var flightCap = S.Query.GetShipFlightCondition.Ask();
            planetW.SetText(T.ShipCapablePlanet);
            flightCap.HasEnginesSector.Bind(lifetime, v => {
                planetW.SetImage(v ? IconId.CCheck : IconId.CCross);
                planetW.SetText2(v ? T.Yes : T.No);
            });

            // Short hyperjump
            var shortW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            shortW.SetText(T.NounHyperjumpShort);
            flightCap.HasEnginesRegion.Bind(lifetime, v => {
                shortW.SetImage(v ? IconId.CCheck : IconId.CCross);
                shortW.SetText2(v ? T.Yes : T.No);
            });

            // Long hyperjump
            var longW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            longW.SetText(T.NounHyperjumpLong);
            flightCap.HasEnginesUniverse.Bind(lifetime, v => {
                longW.SetImage(v ? IconId.CCheck : IconId.CCross);
                longW.SetText2(v ? T.Yes : T.No);
            });
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
