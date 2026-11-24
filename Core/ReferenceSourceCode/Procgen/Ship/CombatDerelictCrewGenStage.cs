using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using KL.Randomness;

namespace Game.Procgen {
    /// <summary>
    /// Spawns the crew remnants of a ship that was just defeated in combat and turned into
    /// this derelict (see NPCShip.TurnIntoDerelict / ShipStateFlags.SpawnDevices). Always on
    /// the ship's own faction, split into dead, wounded and unscathed - like a scaled-down
    /// raid rather than an empty hull or a free colonist. Whether the living ones are
    /// hostiles or non-hostile guests (Passenger role) is decided by the caller through
    /// <paramref name="role"/> - see BaseDerelictShipStoryEvent.
    /// </summary>
    public class CombatDerelictCrewGenStage : BaseShipGenStage {
        private static readonly DamageType[] woundDamageTypes = {
            DamageType.Energy, DamageType.Sharp, DamageType.Piercing, DamageType.Explosive,
        };

        private readonly int count;
        private readonly Dictionary<Species, float> weightedSpecies;
        private readonly Dictionary<GenItemFlags, float> weightedEquipment;
        private readonly FactionDef faction;
        /// <summary>
        /// When false every crew member is spawned already dead. Used when live hostiles
        /// could not act anyway (combat disabled); guests are always allowed to live.
        /// </summary>
        private readonly bool includeSurvivors;
        private readonly ColonyRole role;

        public CombatDerelictCrewGenStage(int count,
                Dictionary<Species, float> weightedSpecies,
                Dictionary<GenItemFlags, float> weightedEquipment,
                FactionDef faction,
                bool includeSurvivors,
                ColonyRole role = ColonyRole.Unassigned) {
            this.count = count;
            this.weightedSpecies = weightedSpecies;
            this.weightedEquipment = weightedEquipment;
            this.faction = faction;
            this.includeSurvivors = includeSurvivors;
            this.role = role;
        }

        public override void Apply() {
            var subRng = rng.Fork();
            for (int i = 0; i < count; i++) {
                int bPos = FindBeingSpawnPos();
                if (!rng.TryWeightedFrom(weightedSpecies, out var sp)) { continue; }
                rng.TryWeightedFrom(weightedEquipment, out var eq);

                var vars = new GenBeingVars(sp, BeingType.Friendly);
                vars.Faction = faction;
                vars.Role = role;
                vars.Body = faction.GenBodyVars;
                var data = BeingUtils.GenBeingWithInventory(vars, subRng, eq);
                var cmd = new CmdSpawnBeing(data, bPos);
                cmd.Execute(S);
                if (cmd.SpawnedBeing == null) { continue; }

                var woundTimeAgo = subRng.Range(Consts.TicksPerHour, Consts.TicksPer3Days);
                bool isDead = !includeSurvivors || subRng.Chance(Tunables.DerelictCrewDeadChance);
                if (isDead) {
                    BeingUtils.KillHistorical(S, data, woundTimeAgo, subRng, woundDamageTypes);
                } else if (subRng.Chance(Tunables.DerelictCrewWoundedChance)) {
                    BeingUtils.DealHistoricalDamage(S, data, subRng.From(woundDamageTypes),
                        woundTimeAgo, subRng.Range(1, 3), null,
                        Severity.Moderate, Severity.Severe);
                }
                if (!isDead && role == ColonyRole.Passenger) {
                    PassengerUtils.AddRescueBoostFor(data);
                }
            }
        }
    }
}
