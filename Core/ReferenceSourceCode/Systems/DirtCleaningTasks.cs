using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Areas;
using KL.Grid;

namespace Game.Systems {
    // Keeps one hidden colony task per dirty room or outside area, so dirt itself creates
    // cleaning work at the lowest priority instead of waiting for someone's cleaning need or
    // idle time. Why it works this way: Docs/Architecture/Systems/AI/CleaningTasks.md
    public sealed class DirtCleaningTasks {
        // picks up tasks that finished while their area got dirty again
        private const int SyncTicks = Consts.TicksPer10Minutes;
        // safety net for counts that drifted through an area change nobody announced
        private const int RecountTicks = Consts.TicksPerHour;

        private struct AreaDirt {
            public int Tiles;
            // where a new task for the area is reached at: the tile that got dirty last
            public int Anchor;
        }

        private readonly DirtSys dirtSys;
        private readonly List<DirtData> dirt;
        // dirty tiles (not dirt items) per cleaning area, see AreaOf
        private readonly Dictionary<int, AreaDirt> dirtByArea = new();
        private readonly List<AIGoal> goals = new();
        private readonly HashSet<int> claimedAreas = new();
        private readonly HashSet<int> countedTiles = new();
        // tile events before the first count would be counted again by it
        private bool isCounted;
        private bool needsRecount;
        private bool needsSync;

        // NonSerialized: Unity compares serializable layouts between the Editor and
        // the player, and these fields do not exist in production builds
        #if !PRODUCTION_BUILD
        [System.NonSerialized] public int Recounts;
        [System.NonSerialized] public int Syncs;
        #endif

        public DirtCleaningTasks(DirtSys dirtSys, List<DirtData> dirt) {
            this.dirtSys = dirtSys;
            this.dirt = dirt;
        }

        // The room or outside area a tile is cleaned with. A door belongs to the first room
        // next to it. Derelict corners and the map margin belong to none (0), cleaners are
        // never sent there, and neither do tiles nobody can stand on: dirt stays under
        // furniture built on top of it until the next load
        public int AreaOf(int pos) {
            if (!EntityUtils.IsWithinMargin(pos, false, false)) { return 0; }
            var areas = dirtSys.S.Sys.Areas;
            var type = areas.AreaTypeAt(pos);
            if (type != AreaType.Floor && type != AreaType.Door) { return 0; }
            var area = areas.At(pos);
            if (area != -1) { return area; }
            area = RoomOrOutsideAt(areas, Pos.N(pos));
            if (area != 0) { return area; }
            area = RoomOrOutsideAt(areas, Pos.S(pos));
            if (area != 0) { return area; }
            area = RoomOrOutsideAt(areas, Pos.E(pos));
            if (area != 0) { return area; }
            return RoomOrOutsideAt(areas, Pos.W(pos));
        }

        // -1 is a wall or a door, 0 is space
        private static int RoomOrOutsideAt(AreaSys areas, int pos) {
            var area = areas.At(pos);
            return area == -1 ? 0 : area;
        }

        // Area ids change on every recalculation, the tile a task is anchored on does not
        public int AreaOfTask(AIGoal goal) {
            var anchor = goal.ReachEntity;
            if (anchor == null || !anchor.IsActive) { return 0; }
            return AreaOf(anchor.PosIdx);
        }

        // A task handed back is anchored where its worker stands, so the auction, which picks
        // the nearest task, gives the worker its room back instead of the room whose anchor
        // happens to be nearer
        public void Reanchor(AIGoal goal, int pos) {
            var area = AreaOfTask(goal);
            if (area == 0 || AreaOf(pos) != area) { return; }
            var floor = EntityUtils.FloorAt(pos);
            if (floor != null) {
                goal.ReachEntity = floor;
            }
        }

        public bool HasDirt(int area) {
            return dirtByArea.ContainsKey(area);
        }

        // The counts can lag behind an area change until the next recount, so a worker that
        // finds nothing asks the dirt itself before giving up on the area
        public bool HasActualDirt(int area) {
            for (int i = dirt.Count - 1; i >= 0; i--) {
                if (AreaOf(dirt[i].PosIdx) == area) { return true; }
            }
            if (HasDirt(area)) {
                needsRecount = true;
            }
            return false;
        }

        public void OnTileDirtied(int pos) {
            if (!isCounted) { return; }
            var area = AreaOf(pos);
            if (area == 0) { return; }
            dirtByArea.TryGetValue(area, out var entry);
            entry.Tiles++;
            entry.Anchor = pos;
            dirtByArea[area] = entry;
            if (entry.Tiles == 1) {
                needsSync = true;
            }
        }

        public void OnTileCleaned(int pos) {
            if (!isCounted) { return; }
            var area = AreaOf(pos);
            if (area == 0 || !dirtByArea.TryGetValue(area, out var entry)) { return; }
            entry.Tiles--;
            if (entry.Tiles > 0) {
                dirtByArea[area] = entry;
                return;
            }
            // the task of a room cleaned by someone else goes away at once
            dirtByArea.Remove(area);
            needsSync = true;
        }

        public void MarkRecount() {
            needsRecount = true;
        }

        public void MarkSync() {
            needsSync = true;
        }

        public void Tick(long ticks) {
            // the map is still being generated, or area ids are being rebuilt
            if (!Ready.Gen || !Ready.Areas) { return; }
            if (needsRecount || !isCounted || ticks % RecountTicks == 0) {
                Recount();
            }
            if (needsSync || ticks % SyncTicks == 0) {
                Sync(ticks);
            }
        }

        private void Recount() {
            dirtByArea.Clear();
            countedTiles.Clear();
            for (int i = dirt.Count - 1; i >= 0; i--) {
                var pos = dirt[i].PosIdx;
                if (!countedTiles.Add(pos)) { continue; }
                var area = AreaOf(pos);
                if (area == 0) { continue; }
                dirtByArea.TryGetValue(area, out var entry);
                entry.Tiles++;
                entry.Anchor = pos;
                dirtByArea[area] = entry;
            }
            countedTiles.Clear();
            isCounted = true;
            needsRecount = false;
            needsSync = true;
            #if !PRODUCTION_BUILD
            Recounts++;
            #endif
        }

        private void Sync(long ticks) {
            needsSync = false;
            #if !PRODUCTION_BUILD
            Syncs++;
            #endif
            var priority = Tunables.CleaningTaskPriority;
            claimedAreas.Clear();
            // a task someone is working on keeps its area, the others make way for it
            for (int i = goals.Count - 1; i >= 0; i--) {
                var goal = goals[i];
                if (AIGoal.IsArchived(goal.State)) {
                    goals.RemoveAt(i);
                } else if (goal.State == AIGoalState.InProgress) {
                    claimedAreas.Add(AreaOfTask(goal));
                }
            }
            for (int i = goals.Count - 1; i >= 0; i--) {
                var goal = goals[i];
                // its worker finishes by itself once the area is clean
                if (goal.State == AIGoalState.InProgress) { continue; }
                var area = AreaOfTask(goal);
                if (area == 0 || !dirtByArea.ContainsKey(area) || !claimedAreas.Add(area)) {
                    // the area is clean or merged into one that has a task, or the anchor
                    // is gone or built over; a fresh task comes below if it is still dirty
                    AIGoal.Cancel(goal, ticks);
                    goals.RemoveAt(i);
                    continue;
                }
                // a changed tunable applies without a reload
                if (goal.GoalPriority != priority) {
                    goal.SetPriority(ticks, priority);
                }
            }
            foreach (var kv in dirtByArea) {
                if (claimedAreas.Contains(kv.Key)) { continue; }
                var anchor = kv.Value.Anchor;
                // the anchor left its area or got built over since it was counted; the
                // recount after that area change brings a new one
                if (AreaOf(anchor) != kv.Key) { continue; }
                var floor = EntityUtils.FloorAt(anchor);
                if (floor == null) { continue; }
                goals.Add(CreateGoal(floor, priority, ticks));
            }
        }

        private AIGoal CreateGoal(Tile floor, int priority, long ticks) {
            var goal = AIGoal.Create(JobTypeIdH.Cleaning, T.CleanFloors, null, ticks)
                .WithFlag(AIGoalFlags.Hidden)
                .WithReachEntity(floor)
                .WithPriority(priority)
                .WithIcon(IconId.CMop)
                .WithRequiredAbilities(AbilityIdH.CleanFloors, AbilityIdH.DumbLabor)
                .WithWorldToDesired(AIVarsH.IsAreaClean, false, true)
                .WriteInitialState();
            dirtSys.S.Sig.AIAddGoal.Send(goal);
            return goal;
        }

        // The AI system drops its goals on unload, so these are only forgotten
        public void Clear() {
            dirtByArea.Clear();
            goals.Clear();
            claimedAreas.Clear();
            countedTiles.Clear();
            isCounted = false;
            needsRecount = false;
            needsSync = false;
        }
    }
}
