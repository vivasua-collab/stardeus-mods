using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ObjQualityComp : BaseComponent<ObjQualityComp>, IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ObjQualityComp());
        }

        public string StatsCategoryName {
            get {
                if (Obj.Data.BodyPart != null) {
                    return T.NounBodyPart;
                } else if (Obj.Data.Item != null) {
                    return T.EquipableItem;
                } else {
                    return T.NounObject;
                }
            }
        }

        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var q = Obj.Data.Quality;
            var widget = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            widget.Load(IconId.CBookmark, T.NounQuality, q, Units.PercentageColored(q, 0.85f, 0.5f, false));
            var bpState = Obj.Data.BodyPartState;
            if (bpState != null) {
                var bpWidget = UISpawn.Widget<WIconText2Progress>(life,
                    UIPrefabId.WContentProgress, block.Container, idx++);
                bpWidget.Load(Obj.Definition.Preview, T.Condition, bpState.Condition,
                    Units.PercentageColored(bpState.Condition, 0.85f, 0.5f, false));
                if (bpState.IsInfected) {
                    var infWidget = UISpawn.Widget<WIconText>(life,
                        UIPrefabId.WContentText, block.Container, idx++);
                    infWidget.Load(IconId.CPoison, T.AdjInfected);
                }
            }
        }
    }
}
