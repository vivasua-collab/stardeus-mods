using System;
using Game.Systems.AI.Combat;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Procgen;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Ships;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public abstract class BaseDerelictShipStoryEvent : StoryEvent, IProducesDerelictShip {
        protected virtual int maxDerelictWidth => isSmall ? 55 : 100;
        protected virtual int maxDerelictHeight => isSmall ? 35 : 65;
        protected virtual int cellWidth => isSmall ? 4 : 5;
        protected virtual int cellHeight => isSmall ? 3 : 4;
        protected virtual int minLootCount => isSmall ? 2 : 5;
        protected virtual int maxLootCount => isSmall ? 5 : 10;
        protected virtual float chanceOfSmallShip => 0.8f;
        protected bool isSmall;
        protected virtual int minDerelictWidth =>
            Math.Min(Mathf.RoundToInt(Consts.CornerMargin * 0.9f),
                Mathf.RoundToInt(maxDerelictWidth * 0.7f));
        protected virtual int minDerelictHeight =>
            Math.Min(Mathf.RoundToInt(Consts.CornerMargin * 0.7f),
                Mathf.RoundToInt(maxDerelictHeight * 0.7f));
        protected virtual int maxEngines => 4;
        protected virtual string[] engineOptions => EngineOptionsForTier();
        protected string[] EngineOptionsForTier() {
            var options = new HashSet<string>();
            foreach (var defKV in The.Defs.Defs) {
                var def = defKV.Value;
                if (!def.IsPlacementInSpace) { continue; }
                if (def.IsAbstract || def.IsTemplate) { continue; }
                if (!A.S.Research.IsDefUnlocked(def)) { continue; }
                if (!def.HasComponent(ComponentIdH.ShipEngine)) { continue; }
                var allEngines = A.S.Components.AllByType<ShipEngineComp>();
                if (allEngines != null) {
                    foreach (var engine in allEngines) {
                        if (engine.IsConstructed) {
                            options.Add(engine.Entity.Definition.Id);
                        }
                    }
                }
            }
            return options.ToArray();
        }

        private Curve derelictSizeCurve;

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            // derelict events are handled by CmdSpawnDerelictShip,
            // they will not start / tick at all
            data.Flags = StoryEventDataFlags.SkipEventLog;
            data.SetState(ctx.S, StoryEventState.Ending);
            var icon = GraphicsId.SpaceObjectsDerelictsShipUnclassified01_Derelict_D;
            string title = T.EvtDerelictApproachedTitle;
            string desc = T.EvtDerelictApproachedDesc;
            if (data.TargetSO != null) {
                var looks = data.TargetSO.Looks(true);
                if (looks != null) {
                    icon = SpaceObjectLooks.SpriteFor(data.TargetSO, looks);
                }
            }
            data.CreateLogEntry(icon, title, desc);
        }

        // How much shorter than its no-build corner a derelict's rect is. The hull comes out up
        // to about 3 tiles past its rect on a side, and a bit differently from run to run: 5
        // left a Titan touching the corner's edge or past it, and its overhang counted as the
        // player's ship (STA-1924)
        protected const int CornerSlack = 10;

        public virtual PosRect GetShipRect(NPCShip ship, Rng rng, out PosRect maxArea) {
            int center = ChooseCenter(rng, out var maxWidth, out var maxHeight);
            int w;
            int h;
            if (ship == null) {
                D.ErrSoft("Derelict has no Ship!");
                isSmall = rng.Chance(chanceOfSmallShip);
                maxHeight = Maths.Min(maxHeight - CornerSlack, maxDerelictHeight);
                maxWidth = Maths.Min(maxWidth - CornerSlack, maxDerelictWidth);
                w = (int)rng.Range(Mathf.Min(minDerelictWidth, maxWidth), maxWidth);
                h = (int)rng.Range(Mathf.Min(minDerelictHeight, maxHeight), maxHeight);
                D.Warn($"Derelict: {w}/{h}. Small: {isSmall}");
            } else {
                derelictSizeCurve ??= Tunables.DerelictSizeCurve;
                var derelictSize = ship.Class.DerelictSizeFor(ship.Size);
                maxWidth = derelictSizeCurve.Evaluate(derelictSize);
                maxHeight = maxWidth * ship.Class.DerelictWidth(rng);
                if (maxHeight > Consts.CornerMargin - CornerSlack) {
                    maxHeight = Consts.CornerMargin - CornerSlack;
                }
                if (maxWidth > Consts.CornerMargin - CornerSlack) {
                    maxWidth = Consts.CornerMargin - CornerSlack;
                }
                w = (int)maxWidth;
                h = (int)maxHeight;
                D.Warn($"Derelict: {w}/{h}. Class: {ship.Class}");
            }
            var halfW = w / 2;
            var halfH = h / 2;
            maxArea = new PosRect(
                Pos.NeighborAt(center, -w / 2, -h / 2),
                Pos.NeighborAt(center, w / 2 - 1, h / 2 - 1));
            return new PosRect(
                Pos.NeighborAt(center, -halfW, -halfH),
                Pos.NeighborAt(center, halfW - 1, halfH - 1));
        }
        protected virtual void AddStructureStages(ShipFactory factory, Rng rng) {
            GenShip.AddStructureStages(factory, rng, maxEngines, 0.01f,
                rng.OneOf(0, 6), engineOptions);
        }

        protected virtual void AddDestructionStages(ShipFactory factory, Rng rng) {
            GenShip.AddDestructionStages(factory, rng, 0);
        }

        protected virtual void AddPostStructureStages(ShipFactory factory, Rng rng) {
        }

        protected virtual void AddLootStages(ShipFactory factory, Rng rng) {
            var lootCount = rng.Range(minLootCount, maxLootCount);
            if (lootCount > 0) {
                GenShip.AddRandomLootStages(A.S, factory, rng, lootCount,
                    A.S.Research.MaxTier, A.S.Research.IsDefActuallyUnlocked);
            }
        }

        protected virtual void AddPostLootStages(ShipFactory factory, Rng rng) {
            GenShip.AddPostLootStages(factory, rng);
        }

        private Curve crewRemnantsCurve;

        /// <summary>
        /// Guaranteed crew remnants for a derelict resulting from a ship just being defeated
        /// in combat (see NPCShip.TurnIntoDerelict / ShipStateFlags.SpawnDevices). Hostile
        /// when the player did the killing, or when the faction itself is hostile towards
        /// the player; otherwise the survivors keep their own faction as non-hostile guests
        /// (Passenger role). No-ops for derelicts that weren't just killed in combat. Events
        /// that already generate their own combat-tied crew from ForShip (Traitor, Virus1/2,
        /// Pirates) override this as a no-op to avoid spawning hostiles twice.
        /// </summary>
        protected virtual void AddCombatCrewRemnantsStage(ShipFactory factory, Rng rng) {
            var ship = factory.ForShip;
            if (ship == null) { return; }
            if ((ship.State & ShipStateFlags.SpawnDevices) != ShipStateFlags.SpawnDevices) {
                return;
            }
            if ((ship.State & ShipStateFlags.CrewAbandoned) != 0) {
                return;
            }
            var fac = ship.Faction;
            if (fac == null) {
                D.Err("Combat-derelict has no faction, skipping crew remnants! {0}", ship);
                return;
            }

            bool isHostile = (ship.State & ShipStateFlags.DefeatedByPlayer) != 0
                || A.S.Sys.Diplomacy.IsHostileTowardsPlayer(fac);

            crewRemnantsCurve ??= CurveDef.Get(CurveId.Derelict_CrewRemnants);
            int count = Mathf.Max(1, Mathf.RoundToInt(crewRemnantsCurve.Evaluate(ship.Size)));

            Dictionary<Species, float> weightedSpecies;
            if (isHostile) {
                // Hostile remnants keep the faction's raid mix: bots by the current raid tier,
                // humans by the faction's AI share (see RaiderSpecies).
                weightedSpecies = new Dictionary<Species, float>();
                RaiderSpecies.WeightedFor(fac, RaiderSpecies.CurrentGearTier(A.S, fac), false,
                    weightedSpecies);
            } else {
                // Non-hostile remnants are guests - biased towards people rather than robots
                // (see SurvivorSpecies).
                weightedSpecies = SurvivorSpecies.WeightedFor(A.S, fac);
            }
            var weightedEquipment = new Dictionary<GenItemFlags, float>() {
                { GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized, 1f },
            };
            // Guests never fight, so the "combat off -> leave nobody alive" fallback only
            // applies to hostile remnants; guests keep the normal dead/wounded/unscathed
            // split either way (the ship was destroyed regardless).
            bool includeSurvivors = !isHostile || A.IsCombatOn;
            var role = isHostile ? ColonyRole.Unassigned : ColonyRole.Passenger;
            factory.AddStage(new CombatDerelictCrewGenStage(count, weightedSpecies,
                weightedEquipment, fac, includeSurvivors, role));
        }

        protected virtual void AddCosmeticStages(ShipFactory factory, Rng rng) {
            GenShip.AddCosmeticStages(factory, rng);
        }

        protected virtual void AddPostDestructionStages(ShipFactory factory, Rng rng) {
            GenShip.AddPostDestructionStages(factory, rng);
        }

        protected virtual void AddRotationStages(ShipFactory factory, Rng rng) {
            var dir = rng.Range(-1, 3);
            if (dir == -1 || dir == 1) {
                factory.AddStage(new ShipRotateGenStage(false, (sbyte) dir));
            } else if (dir == 2) {
                factory.AddStage(new ShipRotateGenStage(false, 1));
                factory.AddStage(new ShipRotateGenStage(false, 1));
            }
        }

        protected virtual void AddFinalStages(ShipFactory factory, Rng rng) {

        }

        public virtual ShipFactory CreateShipFactory(GameState s, Rng rng, NPCShip forShip) {
            var buildSeed = rng.Int;
            var rekt = GetShipRect(forShip, rng, out var maxArea);
            //D.Warn("Ship area: {0}", maxArea);
            var factory = new ShipFactory(rekt, buildSeed, false);
            factory.ShipShape = ShipShapeConfig.ForScenario(Scenario.Random(rng).Id);
            factory.ForShip = forShip;
            factory.Confine(maxArea);
            var cw = cellWidth;
            var ch = cellHeight;
            if (forShip?.Class.IsSpaceStation == true) {
                factory.ShipShape = ShipShapeConfig.ForSpaceStation();
                ch = cw;
            }
            factory.SetCellSize(cw, ch);
            AddStructureStages(factory, rng);
            AddPostStructureStages(factory, rng);
            AddShipDevices(factory, rng);
            AddRotationStages(factory, rng);
            AddDestructionStages(factory, rng);
            AddPostDestructionStages(factory, rng);
            AddLootStages(factory, rng);
            AddPostLootStages(factory, rng);
            AddCombatCrewRemnantsStage(factory, rng);
            AddCosmeticStages(factory, rng);
            AddFinalStages(factory, rng);
            return factory;
        }

        private void AddShipDevices(ShipFactory factory, Rng rng) {
            if (factory.ForShip == null) { return; }
            if ((factory.ForShip.State & ShipStateFlags.SpawnDevices) == ShipStateFlags.SpawnDevices) {
                var stage = new ShipDevicesGenStage(factory.ForShip);
                factory.AddStage(stage);
            }
        }

        protected virtual int ChooseCenter(Rng rng, out float maxWidth, out float maxHeight) {
            int x = 0, y = 0;
            var corner = rng.Range(0, 4);
            int center = GetCornerCenter(ref x, ref y, corner);
            int attempts = 0;
            while (EntityUtils.GetTopmostParentTile(center) != null && attempts++ < 4) {
                D.Warn("Corner is not empty! {0}. Trying text one.", Pos.String(center));
                rng = rng.Fork();
                center = GetCornerCenter(ref x, ref y, ++corner % 4);
            }
            maxWidth = Consts.CornerMargin;
            maxHeight = Consts.CornerMargin;
            return center;
        }

        private static int GetCornerCenter(ref int x, ref int y, int corner) {
            switch (corner) {
                case 0: {
                        // top right
                        x = Pos.GridW - Consts.CornerMargin / 2;
                        y = Pos.GridH - Consts.CornerMargin / 2;
                        break;
                    }
                case 1: {
                        // bottom right
                        x = Pos.GridW - Consts.CornerMargin / 2;
                        y = Consts.CornerMargin / 2;
                        break;
                    }
                case 2: {
                        // bottom left
                        x = Consts.CornerMargin / 2;
                        y = Consts.CornerMargin / 2;
                        break;
                    }
                case 3: {
                        // top left
                        x = Consts.CornerMargin / 2;
                        y = Pos.GridH - Consts.CornerMargin / 2;
                        break;
                    }
            }
            var center = Pos.FromXY(x, y);
            return center;
        }

        public virtual void AfterGenerate(GameState s, ShipFactory factory, Rng rng) { }
    }
}
