using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
using Game.Systems.AI.Combat;
using Game.Systems.Ships;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithParticleCollectorStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithParticleCollector";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithParticleCollectorStoryEvent());
        }
        // A guard detail on a loot derelict, not a raid - see DerelictShipPiratesStoryEvent.
        private const int MinGuards = 3;
        private const int MaxGuards = 8;
        private const float GuardStrengthMul = 0.35f;
        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            var items = new List<Def> {
                The.Defs.FastGet(DefIdsH.ObjectsDevicesParticleCollector),
            };
            if (rng.Chance(0.66f)) {
                items.Add(The.Defs.FastGet(DefIdsH.ObjectsDevicesSolarPanelArray));
            }  else {
                items.Add(The.Defs.FastGet(DefIdsH.ObjectsDevicesSolarPanelArray2));
            }
            factory.AddStage(new ObjectsInSpaceGenStage(items));
        }

        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {
            var ship = factory.ForShip;
            // A ship just shot down already has its own crew aboard (crew remnants), a
            // second, random faction guarding it read as a raid out of nowhere (STA-1881)
            if (ship != null && (ship.State & ShipStateFlags.SpawnDevices) != 0
                    && (ship.State & ShipStateFlags.CrewAbandoned) == 0) {
                return;
            }
            var positions = new List<int>();
            factory.Spec.Bounds.ForEachExpanded((p) => {
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                    positions.Add(p);
                }
            }, 1);
            var pos = rng.From(positions);
            var raid = new CombatRaid(s, rng.Int);
            raid.PosIdx = pos;
            raid.MinRaiders = MinGuards;
            raid.MaxRaiders = MaxGuards;
            raid.AdjustStrength(GuardStrengthMul);
            raid.CountsAsRaid = false;
            // The wreck's own faction guards it when it can; otherwise a random raiding crew
            // found it first
            var owner = ship?.Faction;
            if (owner != null && owner.CanRaid && s.Sys.Diplomacy.IsHostileTowardsPlayer(owner)) {
                raid.Faction = owner;
            }
            raid.TryStartRaid(out var army);
        }

    }
}
