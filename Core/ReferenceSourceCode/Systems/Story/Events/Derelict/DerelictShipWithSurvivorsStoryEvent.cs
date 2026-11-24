using System.Collections.Generic;
using Game.Systems.AI.Combat;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Factions;
using Game.Procgen;
using Game.Systems.Creatures;
using Game.Systems.Diplomacy;
using Game.Systems.Items;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithSurvivorsStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithSurvivors";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithSurvivorsStoryEvent());
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            int wantedSurvivors = 1;
            if (rng.Chance(0.1f)) {
                wantedSurvivors++;
            }
            if (rng.Chance(0.1f)) {
                wantedSurvivors++;
            }

            var fac = factory.ForShip?.Faction ?? The.R.Sys.Diplomacy.RandomFactionForTrade();
            if (fac == null) {
                D.Err("Couldn't get faction for trade in derelict ship with survivors!");
                fac = FactionDef.FastGet(FactionIdH.Adventurer);
            }

            // Peacefully-found derelicts only yield hostile survivors when the derelict's
            // faction is one we're actually hostile to - otherwise they keep their own
            // faction as non-hostile guests the player can later send home. Combat-killed
            // derelicts never reach this event at all (see
            // StoryEventFlags.DerelictSpawnsSurvivors), so this only decides the "found it
            // drifting" case.
            bool isHostile = A.S.Sys.Diplomacy.IsHostileTowardsPlayer(fac);
            if (isHostile && !A.IsCombatOn) {
                // Combat is disabled - leave the ship empty rather than spawning unfightable hostiles.
                return;
            }

            // Hostile survivors keep the faction's raid mix (see RaiderSpecies); non-hostile
            // ones are guests, biased towards people rather than robots (see SurvivorSpecies).
            Dictionary<Species, float> weightedSpecies;
            if (isHostile) {
                weightedSpecies = new Dictionary<Species, float>();
                RaiderSpecies.WeightedFor(fac, RaiderSpecies.CurrentGearTier(A.S, fac), false,
                    weightedSpecies);
            } else {
                weightedSpecies = SurvivorSpecies.WeightedFor(A.S, fac);
            }
            var weightedEquipment = new Dictionary<GenItemFlags, float>();
            weightedEquipment.Add(GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized, 0.5f);
            weightedEquipment.Add(GenItemFlags.Casual | GenItemFlags.SpaceReady, 0.5f);

            factory.AddStage(new RandomBeingGenStage(wantedSurvivors,
                weightedSpecies,
                weightedEquipment,
                faction: fac,
                role: isHostile ? ColonyRole.Unassigned : ColonyRole.Passenger) {
                IsRescue = !isHostile,
            });
        }
    }
}
