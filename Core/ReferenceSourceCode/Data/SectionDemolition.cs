using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public enum SectionDemolitionFailure {
        None,
        NoSection,
        AsimovFirstLaw,
        AsimovThirdLaw,
    }

    public static class SectionDemolition {
        // The set the charge actually consumes: the floor island plus every
        // island-0 cell that travels with it - angled hull corners, space-built
        // devices, chained attachments. Reusing the winch/rotate helper is the
        // point: a section that would move as one piece must vanish as one piece,
        // otherwise the angled corners are left floating where the section was.
        // A partial set from a failed walk is harmless here - it degrades to the
        // bare island, which is what demolition did before.
        private static HashSet<int> BuildDemolitionSet(GameState s, int islandId,
                HashSet<int> island) {
            return SectionMoveSet.Build(s, islandId, island, false, out _);
        }

        public static SectionDemolitionFailure CanDemolish(GameState s, int pos,
                out int offendingPos) {
            offendingPos = Pos.Invalid;
            var islandId = s.Sys.Areas.IslandAt(pos);
            if (islandId <= 0
                    || !s.Sys.Areas.Islands.TryGetValue(islandId, out var island)) {
                return SectionDemolitionFailure.NoSection;
            }
            if (A.HasAsimovOverride) {
                return SectionDemolitionFailure.None;
            }
            var hasCpu = false;
            var hasOccupiedStasis = false;
            foreach (var p in BuildDemolitionSet(s, islandId, island)) {
                var obj = EntityUtils.ParentObjAt(p);
                if (obj?.IsActive != true || !obj.IsConstructed) { continue; }
                if (!hasCpu && obj.TryGetComponent<ShipComputerComp>(out _)) {
                    hasCpu = true;
                    offendingPos = p;
                }
                if (obj.TryGetComponent<StasisArrayComp>(out var stasis)
                        && stasis.BeingCount > 0) {
                    hasOccupiedStasis = true;
                }
            }
            // The First Law outranks the Third, so humans in stasis are the reason
            // given even when the last Ship Computer is in the section too
            if (hasOccupiedStasis) {
                return SectionDemolitionFailure.AsimovFirstLaw;
            }
            if (hasCpu && s.Components.CountOfConstructed<ShipComputerComp>(2) < 2) {
                return SectionDemolitionFailure.AsimovThirdLaw;
            }
            return SectionDemolitionFailure.None;
        }

        // How many frames a single ring will wait for the command queue before
        // giving up on it. The queue is shared, so a busy colony can keep it fed
        // indefinitely - the wave is cosmetic and must never stall the demolition.
        private const int MaxFramesPerRing = 120;

        // How far apart merged stacks end up, in tiles. This is also the distance a
        // pile is allowed to visibly jump when it is absorbed, so it trades hauling
        // trips against materials appearing to slide across the wreck.
        private const int MergeBlockSize = 8;

        // A little past the BreakDown radius the drops are scattered with below.
        private const float MatScatterMargin = 3f;

        public static void Execute(GameState s, int islandId, int fromPos) {
            if (!s.Sys.Areas.Islands.TryGetValue(islandId, out var island)) {
                D.Err("SectionDemolition: island {0} not found", islandId);
                return;
            }
            var orderHaul = s.Prefs.GetBool(Pref.DeconstructOrderHaul,
                PrefH.DeconstructOrderHaul, false);
            var rings = BuildRings(s, islandId, island, fromPos);
            Coroutines.Start(DemolishRings(s, rings, orderHaul),
                nameof(SectionDemolition), "SectionDemolition",
                trySameFrame: false, isSystem: false);
        }

        // Groups the footprint into concentric bands around the charge. Rounded
        // Euclidean radius, so a band is a circle rather than a diamond or a square,
        // and there are as many bands as the section has radius - a per-distance
        // grouping would produce hundreds of near-identical frames instead.
        private static List<List<int>> BuildRings(GameState s, int islandId,
                HashSet<int> island, int fromPos) {
            var rings = new List<List<int>>();
            Pos.ToXY(fromPos, out var fx, out var fy);
            foreach (var p in BuildDemolitionSet(s, islandId, island)) {
                Pos.ToXY(p, out var x, out var y);
                var dx = x - fx;
                var dy = y - fy;
                var r = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
                while (rings.Count <= r) {
                    rings.Add(new List<int>());
                }
                rings[r].Add(p);
            }
            return rings;
        }

        // BreakDown only marks the tile and enqueues a CmdRemoveTile, so dumping the
        // whole section in at once leaves the pacing to however the command runner
        // happens to slice its frame budget. Feeding it one ring at a time and letting
        // the queue drain in between is what actually makes the collapse read as a
        // shockwave travelling outward from the charge.
        private static IEnumerator DemolishRings(GameState s, List<List<int>> rings,
                bool orderHaul) {
            foreach (var ring in rings) {
                if (ring.Count == 0) { continue; }
                if (!GameState.IsCurrent(s)) { yield break; }
                foreach (var pos in ring) {
                    // Wall before the floor holding it up, so it is never dangling
                    // when its own floor goes
                    BreakDownIfDemolishable(EntityUtils.WallAt(pos), orderHaul);
                    BreakDownIfDemolishable(EntityUtils.FloorAt(pos), orderHaul);
                }
                yield return null;
                var waited = 0;
                while (s.CmdQ.Size > 0 && waited++ < MaxFramesPerRing) {
                    yield return null;
                }
            }
            if (GameState.IsCurrent(s)) {
                MergeLooseMaterials(s, rings);
            }
        }

        // Every demolished tile drops its own pile, and a hauler walks to each one
        // individually - a large derelict buries the colony in months of hauling for
        // materials that all came from the same place. Merging them into per-block
        // stacks is the difference between the charge being useful and being a
        // punishment. Runs after the collapse so it sees every drop at once, in a
        // single scan.
        private static void MergeLooseMaterials(GameState s, List<List<int>> rings) {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var ring in rings) {
                foreach (var pos in ring) {
                    var p = EntityUtils.CenterOf(pos);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
            if (min.x > max.x) { return; }
            // CmdSpawnMats scatters drops around the tile they came from, so the
            // search has to reach past the footprint or the outermost ones are missed
            var margin = new Vector2(MatScatterMargin, MatScatterMargin);
            var piles = new List<Obj>();
            s.Objs.FindVisualInRect(min - margin, max + margin, piles, null);

            var stacks = new Dictionary<(int bx, int by, int type), UnstoredMatComp>();
            foreach (var obj in piles) {
                var pile = obj.Pile;
                if (!CanMerge(pile)) { continue; }
                Pos.ToXY(obj.PosIdx, out var x, out var y);
                var key = (x / MergeBlockSize, y / MergeBlockSize, pile.Type.IdH);
                if (!stacks.TryGetValue(key, out var into)
                        || into.StackSize + pile.StackSize
                            > UnstoredMatComp.AbsoluteMaxStackSize) {
                    // Nothing to merge into yet, or the running stack is full and
                    // this pile becomes the one the rest of the block merges into
                    stacks[key] = pile;
                    continue;
                }
                into.Absorb(pile);
            }
        }

        private static bool CanMerge(UnstoredMatComp pile) {
            if (pile?.Entity.IsActive != true || pile.Type == null) { return false; }
            // A single item posing as an object, not a material stack
            if (pile.TreatAsObj) { return false; }
            // A hauler is already on its way to this exact pile. An *unassigned* goal
            // is fine to absorb - the engine cancels it when the pile is exhausted -
            // and it has to be, because with the DeconstructOrderHaul preference on
            // CmdSpawnMats gives every fresh drop one the moment it lands.
            if (pile.Reserved > 0 || pile.Entity.Goal?.Agent != null) { return false; }
            // Folding a forbidden pile into an allowed stack would quietly un-forbid it
            if (pile.IsForbidden) { return false; }
            return true;
        }

        private static void BreakDownIfDemolishable(Tile tile, bool orderHaul) {
            if (tile?.IsActive != true || !tile.IsConstructed) { return; }
            if (tile.IsImmutable || tile.IsPendingMove) { return; }
            tile.Constructable?.BreakDown(2f, orderHaul: orderHaul);
        }
    }
}
