using Game.Data;
using Game.Systems.Creatures;
using KL.Utils;
using Game.CodeGen;
using KL.Randomness;
using Game.Systems.Naming;

using Game.Utils;
namespace Game.Components {
    public sealed class CrawlerFacehugComp : BaseComponent<CrawlerFacehugComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CrawlerFacehugComp());
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Obj.TryGetComponent<ObjGraphicsComp>(out var g)) {
                g.Show(false);
            }
            S.Clock.OnNextTick(TurnIntoCrawler);
        }

        private void TurnIntoCrawler() {
            var crawlerId = Obj.Data.BeingDataId;
            if (crawlerId == 0) { return; }
            var data = S.Query.FindBeingData.Ask(crawlerId);
            if (data != null && data.Species.IdH != SpeciesIdH.Crawler) {
                D.Err("WTF, wrong species! Should be crawler: {0}", data);
                data = null;
            }
            if (data == null) {
                D.Err("Couldn't find the crawler data! Generating fresh one");
                data = S.Query.GenBeingFull.Ask(
                    new GenBeingVars(Species.Get(SpeciesIdH.Crawler), BeingType.Neutral),
                    Rng.Unseeded);
            } else {
                data.StateFlags &= ~BeingState.Hidden;
            }
            data.StatsBeing.Hunger.UpdateBase(Rng.URange(0.9f, 0.95f));
            S.Beings.Create(Obj.PosIdx, data);
            S.Objs.Destroy(Obj);
        }
    }
}
