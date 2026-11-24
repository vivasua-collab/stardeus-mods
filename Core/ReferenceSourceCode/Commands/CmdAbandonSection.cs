using System;
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Clock;
using KL.Grid;
using KL.Utils;

namespace Game.Commands {
    public sealed class CmdAbandonSection : AsyncCommand {
        private readonly HashSet<int> island;
        private readonly bool isGen;
        private readonly Action onSuccess;
        private readonly Action onFail;
        public override bool TrySameFrame => !Ready.Gen || G.ClockSpeed >= ClockSpeed.Unlimited;
        public CmdAbandonSection(HashSet<int> island, bool isGen, Action onSuccess, Action onFail) : base("abandon_section") {
            this.island = new HashSet<int>(island);
            this.isGen = isGen;
            this.onSuccess = onSuccess;
            this.onFail = onFail;
        }

        public CmdAbandonSection(HashSet<int> island, bool isGen) : base("abandon_section") {
            this.island = new HashSet<int>(island);
            this.isGen = isGen;
            onSuccess = null;
            onFail = null;
        }

        public override IEnumerator ExecuteAsync() {
            if (!ValidateAsimovLaws()) {
                S.Clock.ForceResume(ForceStopReason.BulkOperation);
                onFail?.Invoke();
                yield break;
            }
            foreach (var p in island) {
                var obj = EntityUtils.ObjAt(p);
                if (obj == null) { continue; }
                if (obj.Transform.IsChild) { continue; }
                if (!obj.IsConstructed) { continue; }
                if (!isGen) {
                    if (obj.TryGetComponent<StasisArrayComp>(out var stasis)) {
                        stasis.Purge();
                    }
                }
            }

            var removed = RemovalSet();
            // Once, before the tiles go: the clock is stopped for the whole operation, so
            // nothing walks onto the section in between
            for (int i = S.Objs.All.Count - 1; i >= 0; i--) {
                Obj o = S.Objs.All[i];
                if (o.IsActive && removed.Contains(o.PosIdx)) {
                    S.Objs.Destroy(o);
                }
            }
            for (int i = S.Beings.All.Count - 1; i >= 0; i--) {
                Being b = S.Beings.All[i];
                if (b.IsActive && b.Data.Mind.Faction?.IsPlayer == false
                        && removed.Contains(b.PosIdx)) {
                    // Eliminate, not Beings.Destroy: the data has to end up Hidden and
                    // Missing with its conditions deactivated, or it lingers in
                    // BeingsSys.All (medical window, conditions ticking on nothing)
                    var cause = !isGen && b.IsAlive ? T.DeathAbandonedInSpace : null;
                    BeingUtils.Eliminate(S, b.Data, cause, G.Ticks);
                }
            }
            foreach (var p in removed) {
                if (!isGen && !GameTimer.HasAvailableFrameBudget()) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
                var obj = EntityUtils.ObjAt(p);
                if (obj != null && !obj.Transform.IsChild) {
                    RemoveTile(obj);
                }
                var wall = EntityUtils.WallAt(p);
                if (wall != null) {
                    RemoveTile(wall);
                }
                var floor = EntityUtils.FloorAt(p);
                if (floor != null) {
                    RemoveTile(floor);
                }
            }
            if (!isGen) {
                S.Sig.ForceAreas.Send();
                onSuccess?.Invoke();
                S.Clock.ForceResume(ForceStopReason.BulkOperation);
            }
        }

        // What goes with the section: the floor island, the ring around it, and what the
        // winch would move with it - space devices attached to it and chained ones such as
        // solar panel strings. The ring alone left chained panels and overhanging thrusters
        // of a derelict floating on after the jump (STA-1912). When the move set cannot be
        // trusted (a device that touches another section too), only the ring goes.
        private HashSet<int> RemovalSet() {
            var res = PosRect.Expand(island);
            if (isGen) { return res; }
            var islandId = 0;
            foreach (var p in island) {
                islandId = S.Sys.Areas.IslandAt(p);
                break;
            }
            // no section of its own: loose space devices (AreaSys.CollectOrphanSpaceDevices)
            if (islandId <= 0) { return res; }
            var moveSet = SectionMoveSet.Build(S, islandId, island, false, out var failure);
            if (failure.Failed) {
                D.Warn("Abandoning section #{0} without its attached space devices: they touch another section", islandId);
                return res;
            }
            res.UnionWith(moveSet);
            return res;
        }

        private void RemoveTile(Tile t) {
            var cmd = new CmdRemoveTile(t, isGen);
            if (isGen) {
                cmd.Execute(S);
            } else {
                S.CmdQ.Enqueue(cmd);
            }
        }

        public bool ValidateAsimovLaws() {
            if (isGen) { return true; }
            var hasOverride = A.HasAsimovOverride;
            var cpuCount = 0;
            Entity focusOn = null;

            foreach (var p in island) {
                var obj = EntityUtils.ObjAt(p);
                if (obj == null) { continue; }
                if (obj.Transform.IsChild) { continue; }
                if (!obj.IsConstructed) { continue; }
                if (!hasOverride) {
                    if (obj.TryGetComponent<StasisArrayComp>(out var stasis)) {
                        if (stasis.BeingCount > 0) {
                            EntityUtils.CameraFocusOn(obj, false);
                            The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation1);
                            return false;
                        }
                    }
                }
                if (obj.TryGetComponent<ShipComputerComp>(out var cpu)) {
                    focusOn = obj;
                    cpuCount++;
                }
            }
            var totalCPUs = S.Components.CountOfConstructed<ShipComputerComp>();
            if (!hasOverride) {
                if (cpuCount > 0) {
                    EntityUtils.CameraFocusOn(focusOn, false);
                    The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation3);
                    return false;
                }
            } else {
                if (cpuCount >= totalCPUs) {
                    EntityUtils.CameraFocusOn(focusOn, false);
                    The.Dialog.ShowSimple(IconId.CComputing, T.OperationForbidden);
                    return false;
                }
            }
            return true;
        }
    }
}
