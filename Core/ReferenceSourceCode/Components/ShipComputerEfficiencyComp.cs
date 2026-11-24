using Game.Constants;
using Game.Data;
using Game.Systems.Suggestions;
using Game.Utils;
using KL.Grid;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ShipComputerEfficiencyComp : BaseComponent<ShipComputerEfficiencyComp>,
            IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipComputerEfficiencyComp());
        }

        public HeatProducerComp HeatProducer;
        private bool suggestedVenting = false;
        private float heatSinkEffectiveness = 0;
        private BFloat heatSinkEffectivenessB;
        public BFloat HeatSinkEffectivenessB => heatSinkEffectivenessB ??= new BFloat(heatSinkEffectiveness);
        private long nextHeatsinkUpdateAt = 0;
        private const long checkHeatsinksEvery = 100;

        public void ApplyHeatSinks() {
            var ticks = G.Ticks;
            if (nextHeatsinkUpdateAt >= ticks) { return; }
            nextHeatsinkUpdateAt = ticks + checkHeatsinksEvery;
            var outl = Tile.Transform.Outline;

            var nextHeatsinkBonus = 0.4f;
            heatSinkEffectiveness = 0f;
            for (int i = 0; i < outl.Length; i++) {
                int p = outl[i];
                var objN = EntityUtils.ParentObjAt(p);
                if (objN != null && objN.IsConstructed
                        && objN.Health.IsOperational
                        && objN.HasComponent<HeatSinkComp>()) {
                    heatSinkEffectiveness += nextHeatsinkBonus;
                    nextHeatsinkBonus *= 0.66f;
                }
                if (heatSinkEffectiveness > 1f) {
                    heatSinkEffectiveness = 1f;
                    break;
                }
            }
            SuggestVentilation();
            heatSinkEffectivenessB?.Set(heatSinkEffectiveness);
            var heat = 1f - heatSinkEffectiveness;
            heat *= S.Difficulty.Reduction;
            HeatProducer.Output = heat * Tunables.ShipComputerHeatRate;
        }

        private void SuggestVentilation() {
            // Look around and suggest venting if we're enclosed
            if (!suggestedVenting && Ready.Areas &&
                    G.Ticks > Consts.TicksPerDay &&
                    S.Sys.Areas.At(Entity.PosIdx) != 0) {
                // TODO change this when we have heat countermeasures
                var heatC = S.GetTemp(Pos.NeighborAt(Tile.PosIdx, 0, -1));
                heatC = Units.KelvinToCelsius(heatC);
                if (heatC > 50) {
                    suggestedVenting = true;
                    S.Sig.AddSuggestion.Send(SuggestionEnsureShipCPUVentilation.SuggId);
                }
            }
        }

        protected override void OnConfig() {
        }

        public override string ToString() {
            return " * CPUEfficiency";
        }

        private bool isSubscribed;

        public override void OnLateReady(bool wasLoaded) {
            if (HeatProducer == null) {
                HeatProducer = entity.GetComponent<HeatProducerComp>();
                ApplyHeatSinks();
            }
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ShipComputerModuleChanged.AddListener(OnModuleChanged);
            }
        }

        private void OnModuleChanged(IShipComputerModule mod) {
            if (mod is HeatSinkComp hs) {
                nextHeatsinkUpdateAt = 0;
            }
        }

        public override void OnRemove() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig.ShipComputerModuleChanged.RemoveListener(OnModuleChanged);
            }
        }

        public string StatsCategoryName => T.CpuEfficiency;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            w.Load(IconId.CTemperature, T.HeatSinkEffectiveness, heatSinkEffectiveness);
            HeatSinkEffectivenessB.Bind(life, (value) => {
                w.SetPercent(value, false);
                w.Item.SetText2(Units.Percentage(value));
            });
        }
    }
}
