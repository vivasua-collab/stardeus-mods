using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Systems.AI;
namespace Game.Procgen {
    // Owns the starting fuel of every reactor on a generated ship: whatever an earlier
    // stage set on a reactor gets overwritten here, so the fuel is chosen per scenario.
    // Intact ships (Leaving Earth, No Robots, Cargo Ship) burn steel plate; wrecks
    // (Wrecked, New Beginning) burn the scrap they are knee-deep in, which is also the
    // fuel the charge station tutorial recommends.
    public class RefuelDevicesGenStage : BaseShipGenStage {
        private readonly string fuelMatId;

        public RefuelDevicesGenStage(string fuelMatId = MatId.SteelPlate) {
            this.fuelMatId = fuelMatId;
        }

        public override void Apply() {
            var fuelMat = MatType.Get(fuelMatId);
            foreach (var reactor in S.Components
                    .GenFind<MatterReactorComp>()) {
                if (IsWithinGenArea(reactor.Tile.PosIdx)) {
                    var fuel = reactor.Entity.GetComponent<RefillableStorageComp>();
                    fuel.SetTargetMaterial(fuelMat);
                    fuel.Fill(100);
                    if (fuel.TryGetGoal(out var g)) {
                        AIGoal.Cancel(g, G.Ticks);
                    }
                }
            }
            foreach (var store in S.Components.GenFind<RefillableStorageComp>()) {
                if (store.IsConfigured && store.MatType.IdH == MatIdH.Water) {
                    if (IsWithinGenArea(store.Tile.PosIdx)) {
                        store.Fill(10);
                        if (store.TryGetGoal(out var g)) {
                            AIGoal.Cancel(g, G.Ticks);
                        }
                    }
                }
            }
        }
    }
}
