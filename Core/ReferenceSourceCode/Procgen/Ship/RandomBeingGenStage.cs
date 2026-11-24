using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using KL.Utils;
using Game.Commands;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Data.Factions;

namespace Game.Procgen {
    public class RandomBeingGenStage : BaseShipGenStage {
        private readonly int count;
        private Dictionary<Species, float> weightedSpecies;
        private Dictionary<GenItemFlags, float> weightedEquipment;
        private readonly FactionDef faction;
        private readonly ColonyRole role;
        // The guests are survivors rescued from a derelict (PassengerUtils.AddRescueBoostFor).
        // Set by the caller: a stage can't tell a derelict from any other ship.
        public bool IsRescue;
        public RandomBeingGenStage(int count,
                Dictionary<Species, float> weightedSpecies,
                Dictionary<GenItemFlags, float> weightedEquipment,
                FactionDef faction = null,
                ColonyRole role = ColonyRole.Unassigned) {
            this.faction = faction;
            this.role = role;
            this.count = count;
            this.weightedEquipment = weightedEquipment;
            this.weightedSpecies = weightedSpecies;
        }
        public override void Apply() {
            var subRng = rng.Fork();
            for (int i = 0; i < count; i++) {
                int bPos = FindBeingSpawnPos();
                if (rng.TryWeightedFrom(weightedSpecies, out var sp)) {
                    rng.TryWeightedFrom(weightedEquipment, out var eq);
                    var vars = new GenBeingVars(sp, BeingType.Friendly);
                    vars.Faction = faction;
                    vars.Role = role;
                    vars.Body = faction?.GenBodyVars ?? FactionDef.RandomBodyVars(subRng);
                    var data = BeingUtils.GenBeingWithInventory(vars, subRng, eq);
                    var cmd = new CmdSpawnBeing(data, bPos);
                    cmd.Execute(S);
                    var being = cmd.SpawnedBeing;
                    if (being != null) {
                        Ready.WhenGame(being, "Make human damageable again", () => {
                            being.SetImmutable(being.Persona, false);
                        });
                        if (data.Species.IsBiological && !data.Species.IsAnimal) {
                            data.StatsBeing.AIAlignment.UpdateBase(
                                subRng.Range(0.4f, 0.8f));
                        }
                        if (IsRescue && data.IsGuest) {
                            PassengerUtils.AddRescueBoostFor(data);
                        }
                    }
                }
            }
        }
    }
}
