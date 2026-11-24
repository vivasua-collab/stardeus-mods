using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Firex;
using Game.Components.Upgrades;

namespace Game.Components {
    public sealed class WithFuseComp : BaseComponent<WithFuseComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new WithFuseComp());
        }

        public float MalfunctionWeight;
        public bool CanHaveGenericMalfunction;
        public float CurrentBreakChance {
            get {
                if (!Tile.ENode.IsConsuming && !Tile.ENode.IsProducing) {
                    return 0f;
                }
                var o2 = S.GetO2(Tile.PosIdx);
                return (50f + MalfunctionWeight) * (1.1f - Tile.Health.HealthPercent) * (1f + o2);
            }
        }

        protected override void OnConfig() {
            MalfunctionWeight = Config.GetFloat(PropertyIdH.MalfunctionWeight);
            CanHaveGenericMalfunction = Config.GetBool(PropertyIdH.CanHaveGenericMalfunction, true);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.ENode == null) {
                D.Err("WithFuse requires ElectricNode component! {0}",
                    Tile.Definition.Id);
                MalfunctionWeight = 0f;
            }
            if (Tile.Upgrades == null) {
                D.Err("WithFuse requires Upgrades component! {0}",
                    Tile.Definition.Id);
                MalfunctionWeight = 0f;
            }
        }

        public override string ToString() {
            return " * WithFuse";
        }

        public bool HasFuse {
            get {
                if (!(Tile.Upgrades?.UsedSlots > 0)) { return false; }
                foreach (var up in Tile.Upgrades.Upgrades) {
                    if (up == FuseUpgrade.Id) { return true; }
                }
                return false;
            }
        }

        public bool TryBlowFuse() {
            A.Grid.DisconnectAll(Tile.ENode);
            if (!(Tile.Upgrades?.UsedSlots > 0)) { return false; }
            // try to find and blow a fuse
            for (int i = 0; i < Tile.Upgrades.Upgrades.Length; i++) {
                string up = Tile.Upgrades.Upgrades[i];
                if (up == FuseUpgrade.Id) {
                    Tile.Upgrades.DestroyUpgrade(i, FuseUpgrade.Id);
                    return true;
                }
            }
            return false;
        }

        public void DoMalfunction(out bool blownFuse) {
            blownFuse = TryBlowFuse();
            if (blownFuse) { return; }

            Tile.Health.TakeDamage(Tile.Health.Health * S.Rng.Range(0.6f, 0.7f),
                DamageType.Energy);
            var explodeChance = S.Difficulty.FireChanceScale * 0.25f;
            if (Tile.Flammable.CanExplode) {
                if (Explosions.CanExplode(explodeChance, S.Rng)) {
                    var explosionChance = Tile.Flammable.ExplosivenessCurve.Evaluate(
                        Tile.Health.HealthPercent);
                    if (S.Rng.Chance(explosionChance)) {
                        Tile.Flammable.CreateExplosion(false);
                        return;
                    }
                }
            }
            if (!Tile.Flammable.IsOnFire) {
                S.Sys.Firex.TrySetOnFire(Tile, Fire.CreateBalanced(S));
            }
        }
    }
}
