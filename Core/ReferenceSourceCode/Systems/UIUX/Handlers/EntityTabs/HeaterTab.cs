using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class HeaterTab : BaseEntityTab {
        public const string TabId = "Heater";

        [SelfInit]
        private static void Register() {
            RegisterTab(new HeaterTab());
        }

        private Tile tile;
        private HeaterComp heater;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => heater.IsCooler ? T.Cooler : T.Heater;

        // protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
        //     widget.SetText(heater.IsCooler ? T.Cooler : T.Heater);
        // }

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<HeaterComp>(out var h)) { return false; }
            if (h.IsDisabled) { return false; }
            this.tile = tile;
            this.heater = h;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var maxTemp = Units.CelsiusToKelvin(
                A.HasAsimovOverride
                    ? Tunables.MaxHeaterTempCelsius_AsimovOverride
                    : Tunables.MaxHeaterTempCelsius);
            var minTemp = Units.CelsiusToKelvin(-30f);

            var sliderRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            sliderRow.Item.Load(heater.IsCooler ? IconId.CSnowflake : IconId.CTemperature,
                T.NounTargetTemperature,
                Texts.Temperature(heater.Limit));
            sliderRow.Slider.Load(heater.Limit, (v) => {
                heater.Limit = Mathf.RoundToInt(v);
            }, minTemp, maxTemp);
            sliderRow.Slider.SetValueTextFunc((v) => {
                return Texts.Temperature(Mathf.RoundToInt(v));
            });

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var resetBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, group.Container, 0);
            resetBtn.Load(IconId.CCog, T.Reset, null, () => {
                heater.ResetLimit();
                ReloadTab();
            });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            heater = null;
            S = null;
        }
    }
}
