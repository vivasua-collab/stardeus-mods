using System.Collections.Generic;
using Game.Systems.AI.Combat;
using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Procgen;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class DerelictShipVirusType1StoryStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipVirusType1Story";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipVirusType1StoryStoryEvent());
        }
        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjGenStage(new List<Def> {
                The.Defs.FastGet(DefIdsH.ObjContainersQuestPackage)
            }));
            int wantedSurvivors = 3;
            if (rng.Chance(0.1f)) {
                wantedSurvivors++;
            }
            if (rng.Chance(0.1f)) {
                wantedSurvivors++;
            }

            var fac = factory.ForShip?.Faction ?? FactionDef.FastGet(FactionIdH.Virus);

            // The faction's raid mix: bots by the current raid tier, humans by the faction's
            // AI share (see RaiderSpecies).
            var weightedSpecies = new Dictionary<Species, float>();
            RaiderSpecies.WeightedFor(fac, RaiderSpecies.CurrentGearTier(A.S, fac), false,
                weightedSpecies);
            var weightedEquipment = new Dictionary<GenItemFlags, float>();
            weightedEquipment.Add(GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized, 1.0f);

            factory.AddStage(new RandomBeingGenStage(wantedSurvivors,
                weightedSpecies,
                weightedEquipment,
                faction: fac));
        }

        // Already spawns its own combat-tied crew above - the base guaranteed crew
        // remnants stage would double up on hostiles for this event.
        protected override void AddCombatCrewRemnantsStage(ShipFactory factory, Rng rng) { }
    }
}
