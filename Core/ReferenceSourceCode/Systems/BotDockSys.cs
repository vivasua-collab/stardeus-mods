using System;
using System.Buffers;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Reg;
using KL.Utils;
using MonoMod.Utils;
using UnityEngine;
using UnityEngine.XR;

using Game.Systems.UIUX;
using Game.Utils;
namespace Game.Systems {
    public sealed class BotDockSys : GameSystem, ISaveableSpecial {
        public const string SysId = "BotDockSys";
        public override string Id => SysId;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new BotDockSys());
        }

        private readonly List<BotDockComp> docks = new();

        /// <summary>
        /// Key is dockable BeingData id, value is dock id
        /// </summary>
        private readonly Dictionary<uint, int> dockAssignments = new();
        private readonly Dictionary<uint, BCallback> dockableStateBindables = new();

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RegisterBotDock.AddListener(OnRegisterDock);
            S.Sig.DockableStateChanged.AddListener(OnDockableStateChanged);
            S.Sig.BeingDied.AddListener(OnBeingDied);
        }

        // A destroyed bot frees its dock at once. Left to ValidateAssignments, the dock stayed
        // taken for up to 3 hours and the cleanup reported a routine death as an error.
        private void OnBeingDied(BeingData being) {
            if (being != null && dockAssignments.ContainsKey(being.Id)) {
                AssignDock(being, null);
            }
        }

        private void OnLoadSave(GameState state) {
            state.Query.FindDocks.SetResponder(FindDocks);
            state.Query.FindDockables.SetResponder(FindDockables);
            state.Query.FindBotDock.SetResponder(FindDock);
            state.Query.AssignBotDock.SetResponder(AssignDock);
            state.Query.GetDockableStateB.SetResponder(GetOrCreateDockableStateB);
            state.Clock.OnTick.AddListener(OnTick);
        }

        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPer3Hours == 78) {
                ValidateAssignments();
            }
        }

        private bool AssignDock(BeingData being, BotDockComp dock) {
            // D.Warn("Assigning dock: {0}->{1}", dock, being);
            if (dock == null || !dock.Tile.IsActive) {
                being.Dockable.DockId = 0;
                // get the original dock
                if (dockAssignments.TryGetValue(being.Id, out var oldDockId)) {
                    var oldDock = S.Components.Find<BotDockComp>(oldDockId);
                    if (oldDock != null) {
                        oldDock.SetAssignedBeingData(0);
                        oldDock.UpdateUIBlock(true);
                    }
                    dockAssignments.Remove(being.Id);
                    NotifyDockable(being);
                    return true;
                }
                return false;
            } else {
                if (!dock.IsConstructed) {
                    return false;
                }
                if (dock.AssignedBeingDataId == being.Id) {
                    // nothing to do
                    return true;
                }
                if (dock.AssignedBeingDataId != 0) {
                    var bd = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
                    if (bd != null) {
                        AssignDock(bd, null);
                    }
                    // Unassign the old one first
                    dockAssignments.Remove(dock.AssignedBeingDataId);

                }
                if (being.Dockable.DockId != 0) {
                    var oldDock = S.Components.Find<BotDockComp>(being.Dockable.DockId);
                    // a stale DockId may point at a dock that now belongs to another bot
                    if (oldDock != null && oldDock.AssignedBeingDataId == being.Id) {
                        oldDock.SetAssignedBeingData(0);
                    }
                }
                dock.SetAssignedBeingData(being.Id);
                being.Dockable.DockId = dock.EntityId;
                dockAssignments[being.Id] = dock.EntityId;
                dock.UpdateUIBlock(true);
                NotifyDockable(being);
                return true;
            }
        }

        private void NotifyDockable(BeingData being) {
            NotifyDockableState(being.Id);
        }

        private void NotifyDockableState(uint beingDataId) {
            if (dockableStateBindables.TryGetValue(beingDataId, out var b)) {
                b.Invoke();
            }
        }

        private void OnDockableStateChanged(uint beingDataId) {
            NotifyDockableState(beingDataId);
        }

        private BCallback GetOrCreateDockableStateB(uint beingDataId) {
            if (!dockableStateBindables.TryGetValue(beingDataId, out var b)) {
                b = new BCallback();
                dockableStateBindables[beingDataId] = b;
            }
            return b;
        }

        private BotDockComp FindDock(BeingData being) {
            if (dockAssignments.TryGetValue(being.Id, out var dockId)) {
                var dock = S.Components.Find<BotDockComp>(dockId);
                if (dock == null || !dock.Tile.IsActive) {
                    // tile destroyed? bug?
                    dockAssignments.Remove(being.Id);
                    being.Dockable.DockId = 0;
                    // we'll fall through and will try to auto-assign a new dock
                } else {
                    return dock;
                }
            }

            var freshDock = FindClosestAssignableDockFor(being);
            if (freshDock != null) {
                AssignDock(being, freshDock);
            }
            return freshDock;
        }

        private BotDockComp FindClosestAssignableDockFor(BeingData being) {
            if (being.Being == null) {
                D.Err("Trying to find closest assignable dock for despawned being: {0}", being);
                return null;
            }
            var numDocks = docks.Count;
            if (numDocks == 0) {
                return null;
            }

            // Find closest assignable docks through region path length query
            var dockPool = ArrayPool<BotDockComp>.Shared;
            var assDocks = dockPool.Rent(numDocks);
            var numAssDocks = 0;

            // build the assignable docks array
            for (int i = docks.Count - 1; i >= 0; i--) {
                BotDockComp dock = docks[i];
                if (dock.IsAssignable(true, true)) {
                    assDocks[numAssDocks++] = dock;
                }
            }

            if (numAssDocks == 0) {
                dockPool.Return(assDocks, true);
                return null;
            }

            var wp = being.Being.PosIdx;
            var isG = being.Being.Motion.IsGrounded;

            var reqPool = ArrayPool<RegPathReq>.Shared;
            var regPathReqs = reqPool.Rent(numAssDocks);
            var resPool = ArrayPool<int>.Shared;
            var resPathLengths = resPool.Rent(numAssDocks);

            for (int i = 0; i < numAssDocks; i++) {
                regPathReqs[i] = RegPathReq.To(wp, assDocks[i].Tile.PosIdx, isG);
                regPathReqs[i].Permissions = being.Being.Motion.Permissions;
            }

            BotDockComp closest = null;
            float closestDist = float.MaxValue;

            if (S.Query.RegPathLengths.Ask(regPathReqs, numAssDocks, resPathLengths)) {
                for (int i = 0; i < numAssDocks; i++) {
                    var dist = resPathLengths[i];
                    if (dist < 0) { continue; }

                    if (dist < closestDist) {
                        closest = assDocks[i];
                        closestDist = dist;
                        if (dist < 0.1f) {
                            break;
                        }
                    }
                }
            }
            dockPool.Return(assDocks, true);
            reqPool.Return(regPathReqs);
            resPool.Return(resPathLengths);
            return closest;
        }

        private int FindDockables(BeingData[] results, bool assignable) {
            var found = 0;
            var allBeings = S.Query.AllBeingData.Ask();
            foreach (var b in allBeings.Values) {
                if (b.Species.IsBiological) { continue; }
                if (b.HasStateFlag(BeingState.Dead) || b.Body.HasFlag(BodyFlags.Missing)) {
                    continue;
                }
                var d = b.Dockable;
                if (!assignable || d.IsAssignable(true, false)) {
                    results[found++] = b;
                    if (found == results.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        private int FindDocks(BotDockComp[] results, bool assignable) {
            var found = 0;
            for (int i = docks.Count - 1; i >= 0; i--) {
                var d = docks[i];
                if (!assignable || d.IsAssignable(true, false)) {
                    results[found++] = d;
                    if (found == results.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        private void OnRegisterDock(BotDockComp dock, bool added) {
            // D.Warn("Register dock: {0}->{1}", added, dock);
            if (added) {
                if (!dock.Tile.IsConstructed) {
                    D.Err("Trying to add (register) an unconstructed dock! {0}", dock.Tile);
                    return;
                }
                docks.Add(dock);
                if (dock.AssignedBeingDataId != 0) {
                    // check if we have the assignment
                    if (!dockAssignments.ContainsKey(dock.AssignedBeingDataId)) {
                        // couldn't find
                        dock.SetAssignedBeingData(0);
                    }
                }
            } else {
                // removal of the dock
                if (dock.AssignedBeingDataId != 0) {
                    var bd = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
                    if (bd != null) {
                        AssignDock(bd, null);
                    }
                    dock.SetAssignedBeingData(0);
                }
                docks.Remove(dock);
            }
        }

        private void ValidateAssignments() {
            List<uint> toRemove = null;
            foreach (var ass in dockAssignments) {
                var beingData = S.Query.FindBeingData.Ask(ass.Key);
                if (beingData == null || beingData.HasStateFlag(BeingState.Dead)
                        || beingData.Body.HasFlag(BodyFlags.Missing)) {
                    if (beingData != null) {
                        toRemove ??= new List<uint>();
                        toRemove.Add(beingData.Id);
                    }
                    D.Err("Will remove invalid dock assignment, being missing: {0} -> {1}", ass.Key, beingData);
                    continue;
                }
                var dock = S.Components.Find<BotDockComp>(ass.Value);
                if (dock == null || !dock.Entity.IsActive) {
                    toRemove ??= new List<uint>();
                    toRemove.Add(beingData.Id);
                    D.Err("Will remove invalid dock assignment, dock missing: {0} -> {1}",
                        ass.Value, beingData);
                    continue;
                }
            }

            if (toRemove != null) {
                for (int i = toRemove.Count - 1; i >= 0; i--) {
                    uint ass = toRemove[i];
                    dockAssignments.Remove(ass);
                }
            }

            foreach (var b in S.Query.AllBeingData.Ask()) {
                if (dockAssignments.TryGetValue(b.Key, out var dockId)) {
                    if (b.Value.Dockable.DockId != dockId) {
                        D.ErrSoft("Fixing missing being dock assignment: {0}->{1}", b.Value, dockId);
                        b.Value.Dockable.DockId = dockId;
                    }
                } else if (b.Value.Dockable.DockId != 0) {
                    // A dead body keeps its BeingData, so a DockId dropped from
                    // dockAssignments at death survives a revival. The AI would keep
                    // choosing that dock over charge stations and never recharge.
                    D.ErrSoft("Clearing stale being dock assignment: {0}->{1}",
                        b.Value, b.Value.Dockable.DockId);
                    b.Value.Dockable.DockId = 0;
                    NotifyDockable(b.Value);
                }
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.BotDock ??= new();
            sd.BotDock.DockAssignments = dockAssignments;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.BotDock?.DockAssignments != null) {
                // in case
                if (dockAssignments.Count > 0) {
                    D.Err("Dock assignments aren't empty at load!");
                    dockAssignments.Clear();
                }
                dockAssignments.AddRange(sd.BotDock.DockAssignments);
                Ready.When(WhenH.AIAgentsLoaded, ValidateAssignments);
            }
        }
    }
}
