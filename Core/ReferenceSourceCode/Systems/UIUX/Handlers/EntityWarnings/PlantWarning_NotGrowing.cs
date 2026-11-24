using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class PlantWarning_NotGrowing : BaseEntityWarning {
        public const string WarningId = "Plant_NotGrowing";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new PlantWarning_NotGrowing());
        }

        public override string Id => WarningId;
        public override int Order => 20;

        private PlantComp plant;

        public override bool ShowFor(GameState s, ObjData obj, out bool blockActions) {
            blockActions = false;
            if (obj.Obj == null) {
                return false;
            }
            if (!obj.Obj.TryGetComponent<PlantComp>(out var p)) {
                return false;
            }
            if (p.IsGrowing || p.IsGrown) {
                return false;
            }
            plant = p;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, group.Container, idx++);
            warning.Load(IconId.CWarning,
                T.InfoPlantNotGrowing, Texts.Red(plant.GrowthState));
        }

        public override void Unload() {
            plant = null;
        }
    }
}
