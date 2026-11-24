using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Mental;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Big picture behavior that executes every turn. It can do
    /// hyperjumps, move from region to region, enter / exit hyperspace, etc.
    /// </summary>
    public abstract class NPCShipBehavior {
        protected static void Register(NPCShipBehavior behavior) {
            behavior.IdH = Hashes.S(behavior.Id);
            Registries.NPCShipBehaviors.Add(behavior.Id, behavior);
        }
        public abstract string Id { get; }
        public int IdH;
        public abstract NPCShipBehaviorScope Scope { get; }
        /// <summary>
        /// Mostly for sector behaviors. Some behaviors should block region
        /// hyperjumping (e.g. Attack, Extort, Gift, Ask for fuel)
        /// </summary>
        public abstract bool CanHyperjump { get; }
        public abstract bool CanInterruptForNotice(NPCShip observer, NPCShip target);
        /// <summary>
        /// Sector behaviors will execute this every tick.
        /// Region behaviors will execute this every time player hyperjumps
        /// or every X hours. <see cref="NPCShipSys"/>
        /// </summary>
        /// <param name="ship">Behaviors are stateless, they operate on the given ship</param>
        /// <returns>Returns the new behavior (or self)</returns>
        public abstract void DoTurn(NPCShip ship);
        public virtual void OnStart(NPCShip ship) {}
        public virtual void OnEnd(NPCShip ship) {}
        public virtual void OnLoad(NPCShip ship) {}
        public virtual bool IsBusy(NPCShip ship, out string message) {
            message = null;
            return false;
        }
        public virtual bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            return true;
        }
        /// <summary>
        /// Rolls whether a ship wants to enter hyperspace this turn. Active quest target
        /// ships get a much lower chance so the player can actually catch up to them.
        /// </summary>
        protected static bool RollWantsHyperspace(NPCShip ship, float chance) {
            if (A.S.Query.IsActiveQuestTargetShip.Ask(ship)) {
                chance *= Tunables.QuestTargetShipHyperjumpChanceMul;
            }
            return Rng.Unseeded.Chance(chance);
        }
        /// <summary>
        /// Rolls whether a ship in hyperspace drops back out this turn. Active quest target
        /// ships use an override chance so they don't linger unreachable in hyperspace.
        /// </summary>
        protected static bool RollExitHyperspace(NPCShip ship, float chance) {
            if (A.S.Query.IsActiveQuestTargetShip.Ask(ship)) {
                chance = Tunables.QuestTargetShipExitHyperspaceChance;
            }
            return Rng.Unseeded.Chance(chance);
        }
        protected readonly HashSet<ISpaceEntity> visited = new();
        protected readonly HashSet<ISpaceEntity> unvisited = new();
        protected readonly HashSet<ISpaceEntity> backtracked = new();
        protected readonly Stack<ISpaceEntity> lastSteps = new();
        protected readonly List<SpaceLink> links = new();
        protected bool TryFollow(NPCShip ship) {
            if (ship.IsDerelict) {
                D.Err("Derelict is trying to follow a ship: {0}->{1}", this, ship);
                ship.SetBehavior(null, NPCShipBehaviorScope.Sector, allowNull: true);
                return false;
            }
            var target = ship.Memory.FollowTarget;
            if (target == null) {
                D.Err("Trying to follow without target: {0}", ship);
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                return false;
            }
            if (target.Parent != ship.SO.Parent) {
                ship.Memory.OnReachFollowTargetFail?.Invoke(ship, target);
                ship.Memory.OnReachFollowTargetFail = null;
                ship.Memory.OnReachFollowTarget = null;
                return false;
            }
            if (ship.SO.Parent != A.Player.Parent) {
                // action is happening somewhere else, simplified simulation
                OnReachFollowTarget(ship, target);
                return true;
            }
            var loc = ship.SO.Location;
            var ftl = target.Location;
            var targetPos = ship.NearbyPosFrom(ftl, out var isNear);
            if (isNear) {
                OnReachFollowTarget(ship, target);
                // we are close enough
                ship.HasLocalTarget = false;
                return true;
            } else {
                if (ship.NumThrustersActual == 0) {
                    D.Err("Ship has no thrusters, can't follow: {0}->{1}", ship, target);
                    ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    return false;
                }
            }

            if (ship.HasLocalTarget) {
                ship.LocalTarget = targetPos;
            } else {
                ship.FlyInLocalSpaceTo(targetPos);
            }
            return true;
        }


        public static bool CheckCanReach(NPCShip ship, SpaceObject target) {
            bool canReach = ship.NumThrustersActual > 0;
            if (!canReach) {
                ship.NearbyPosFrom(target.Location, out var isNear);
                canReach = isNear;
                if (!canReach) {
                    D.Err("Trying to assign distant behavior to ship without thrusters! {0}", ship);
                    NPCShipBehavior_Sector_Idle.AssignTo(ship);
                }
            }
            return canReach;
        }

        private static void OnReachFollowTarget(NPCShip ship, SpaceObject target) {
            if (G.IsDemoMode) {
                ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                return;
            }
            if (!ship.Memory.IsNearFollowTarget) {
                ship.Memory.IsNearFollowTarget = true;
                ship.Memory.OnReachFollowTarget?.Invoke(ship, target);
                ship.Memory.OnReachFollowTarget = null;
                ship.Memory.OnReachFollowTargetFail = null;
            }
        }

        protected void FailReachTarget(NPCShip ship, SpaceObject target) {
            ship.Memory.SetFollowTarget(null);
            ship.Memory.ChaseTargetId = 0;
            ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
        }

        protected void CreatePatrolRouteFor(NPCShip ship) {
            var start = ship.Sector;
            var minDifficulty = ship.Memory.OriginSector.Biome.Difficulty
                - Tunables.NPCShipBehavior_Region_Wander_MinDifficultyDiff;
            ISpaceEntity current = start;
            unvisited.Clear();
            foreach (var sec in start.Region.Sectors) {
                if (sec.Biome.Difficulty >= minDifficulty
                        || sec.Biome.Difficulty > start.Biome.Difficulty) {
                    unvisited.Add(sec);
                }
            }
            unvisited.Remove(start);

            var path = ship.Memory.SectorPath ??= new();
            path.Clear();

            lastSteps.Clear();
            // we will only allow backtracking once
            backtracked.Clear();

            while (unvisited.Count > 0) {
                var next = FindUnvisitedNeighbor(unvisited, current, minDifficulty);
                if (next != null) {
                    if (!TryEnqueueNext(path, (SpaceSector) current, (SpaceSector) next)) {
                        return;
                    }
                    lastSteps.Push(current);
                    current = next;
                } else {
                    // already visited all neighbors, so we have to
                    // backtrack in order to find an unvisited node

                    // solution: crawl the path backwards until we find
                    // a node that has an unvisited neighbor

                    if (lastSteps.TryPop(out var prev)) {
                        if (backtracked.Add(prev)) {
                            if (!TryEnqueueNext(path, (SpaceSector) current, (SpaceSector) prev)) {
                                return;
                            }
                            // if we push here, backtrack will bounce
                            // in a loop
                            // lastSteps.Push(current);
                            current = prev;
                        } else {
                            // already backtracked there
                            return;
                        }
                    } else {
                        // no more last steps, done
                        return;
                    }
                }
            }
        }
        protected ISpaceEntity FindUnvisitedNeighbor(HashSet<ISpaceEntity> unvisited,
                ISpaceEntity current, float minDifficulty) {
            ISpaceEntity next = null;

            links.Clear();
            links.AddRange(current.Links);
            Arrays.Shuffle(Rng.Unseeded, links);

            foreach (var link in links) {
                var neighbor = link.OtherSideOf(current);
                if (unvisited.Remove(neighbor)) {
                    if (neighbor.Biome.Difficulty >= minDifficulty
                            || neighbor.Biome.Difficulty > current.Biome.Difficulty) {
                        next = neighbor;
                        break;
                    }
                }
            }

            return next;
        }

        protected bool TryEnqueueNext<T>(Queue<T> path,
                T current, T next) where T : ISpaceEntity {
            if (SpaceLink.ExistsFor(current, next)) {
                path.Enqueue(next);
                return true;
            } else {
                D.Err("Trying to enqueue a path step where there's no link: {0}=>{1}",
                    current, next);
            }
            return false;
        }
        public override string ToString() {
            return $"NPCShipBehavior[{Id}|{Scope}]";
        }
    }
}
