using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using Game.Systems.Atmo;
using Game.Systems.Notify;
using Game.Systems.UIUX;

namespace Game.Systems.Areas {
    public sealed class DoorConnectionCheck {
        private readonly AreaSys area;
        private NotificationHandle unreachableWarning;
        public readonly HashSet<int> UnreachableAreas = new();
        public bool IsUnreachable(int areaId)  {
            return UnreachableAreas.Contains(areaId);
        }

        public bool TeleportersChanged;

        public bool HasUnreachableAreas => UnreachableAreas.Count > 0;

        private readonly List<DoorComp> doors = new();
        public readonly Dictionary<int, DoorComp> DoorsByPos = new();
        // teleporter between two tiles (one directional)
        private readonly Dictionary<int, int> tps = new();
        private readonly List<(int, int)> areaLinks = new();
        private readonly HashSet<int> areasWithExit = new();
        private readonly HashSet<int> unprocessed = new();

        public DoorConnectionCheck(AreaSys area) {
            this.area = area;
        }
        public void RegisterTP(int from, int to) {
            TeleportersChanged = true;
            tps[from] = to;
        }
        public void UnregisterTP(int from) {
            TeleportersChanged = true;
            tps.Remove(from);
        }

        public void RegisterDoor(int pos, DoorComp door) {
            doors.Add(door);
            if (pos < 1) {
                D.Err("Trying to add an uninitialized door with pos: {0}", pos);
            }
            // it is possible to try to register a door to where another door was already placed
            // during winch when adjacent doors exist
            // to prevent that, we will see if we can perhaps unregister the door that is about
            // to be moved into
            if (DoorsByPos.TryGetValue(pos, out var oldDoor)) {
                // we are definitely in mid-winch
                if (pos != oldDoor.Tile.PosIdx) {
                    // force unregister the about-to-be-winched door
                    doors.Remove(oldDoor);
                    DoorsByPos.Remove(pos);
                    oldDoor.MarkUnregistered();
                    // D.Warn("Force unregistered door mid winch: {0}", oldDoor);
                }
            }
            if (!DoorsByPos.TryAdd(pos, door)) {
                D.Err("Trying to add a door at pos {0}, while it already exists: {1}, (new: {2})",
                    Pos.String(pos), DoorsByPos[pos].Tile, door.Tile);
            }
        }

        public void UnregisterDoor(int pos, DoorComp door) {
            doors.Remove(door);
            if (!DoorsByPos.Remove(pos)) {
                D.Err("Trying to remove a door at pos {0} while it was not added: {1}",
                    Pos.String(pos), door);
            }
        }

        private void TryAddLinkBetween(int n1, int n2, bool isTP) {
            int areaA;
            int areaB;
            areaA = area.At(n1);
            areaB = area.At(n2);
            if (areaA == areaB) { return; }
            if (!isTP) {
                // Door to a wall
                if (areaA == -1) {
                    if (areaB < 0) {
                        // walls or doors from both ends, we don't care
                        return;
                    }
                    // Door to door will be a hack that considers that there is an exit
                    if (EntityUtils.WallAt(n1)?.Door != null) {
                        areasWithExit.Add(areaB);
                        return;
                    }
                } else if (areaB == -1) {
                    if (areaA < 0) {
                        return;
                    }
                    if (EntityUtils.WallAt(n2)?.Door != null) {
                        areasWithExit.Add(areaA);
                        return;
                    }
                }
            }
            if (areaA < 1 || areaB < 1) {
                if (areaA != 0) {
                    areasWithExit.Add(areaA);
                }
                if (areaB != 0) {
                    areasWithExit.Add(areaB);
                }
            } else {
                areaLinks.Add(Maths.OrderedTuple(areaA, areaB));
            }
        }

        public void RebuildDoors() {
            DoorsByPos.Clear();
            foreach (var door in doors) {
                DoorsByPos[door.Tile.PosIdx] = door;
                door.UpdateRegisteredPos();
            }
        }

        public void ValidateDoors() {
            foreach (var door in DoorsByPos) {
                var areaTypeAt = area.AreaTypes[door.Key];
                if (door.Key != door.Value.Tile.PosIdx) {
                    D.Err("Invalid DoorByPos. Expected a door at: {0}. It is actually at: {1}",
                    Pos.String(door.Key), Pos.String(door.Value.Tile.PosIdx));
                }
                if (areaTypeAt != AreaType.Door) {
                    D.Err("Invalid DoorByPos. Expected a door area type at: {0}. It is actually: {1}. Act door pos: {2}",
                    Pos.String(door.Key), areaTypeAt, Pos.String(door.Value.Tile.PosIdx));
                }
            }
        }

        public bool FindUnreachableAreas() {
            TeleportersChanged = false;
            UnreachableAreas.Clear();
            areaLinks.Clear();
            areasWithExit.Clear();
            foreach (var tp in tps) {
                TryAddLinkBetween(tp.Key, tp.Value, true);
            }
            // collect area links
            for (int i = doors.Count - 1; i >= 0; i--) {
                DoorComp d = doors[i];
                var dPos = d.Tile.PosIdx;
                int n1;
                int n2;
                if (d.Tile.Transform.Rotation == Facing.Type.Down) {
                    n1 = Pos.N(dPos);
                    n2 = Pos.S(dPos);
                } else {
                    n1 = Pos.E(dPos);
                    n2 = Pos.W(dPos);
                }
                TryAddLinkBetween(n1, n2, false);
            }

            var numAreas = area.Count;

            if (areasWithExit.Count == 0) {
                // nothing is connected to space!
                foreach (var a in area.Areas) {
                    UnreachableAreas.Add(a.Key);
                }
            } else {
                unprocessed.Clear();
                foreach (var a in area.Areas) { var areaId = a.Key;
                    if (a.Value.Count == 0) {
                        #if !PRODUCTION_BUILD
                        D.Err("Area with 0 size! {0}", areaId);
                        #endif
                        continue;
                    }
                    if (!areasWithExit.Contains(areaId)) {
                        unprocessed.Add(areaId);
                    }
                }
                CrawlAreas();
            }

            UpdateUnreachableWarning();
            return UnreachableAreas.Count > 0;
        }

        private void CrawlAreas() {
            bool canContinue = unprocessed.Count > 0;
            while (canContinue) {
                canContinue = false;
                for (int i = areaLinks.Count - 1; i >= 0; i--) {
                    var link = areaLinks[i];
                    var a1 = link.Item1;
                    var a2 = link.Item2;
                    bool foundSomething = false;
                    if (areasWithExit.Contains(a1)) {
                        if (areasWithExit.Add(a2)) {
                            foundSomething = true;
                        }
                        unprocessed.Remove(a2);
                    } else if (areasWithExit.Contains(a2)){
                        if (areasWithExit.Add(a1)) {
                            foundSomething = true;
                        }
                        unprocessed.Remove(a1);
                    }
                    if (foundSomething) {
                        areaLinks.RemoveAt(i);
                        canContinue = true;
                    }
                }
            }
            foreach (var area in unprocessed) {
                UnreachableAreas.Add(area);
            }
        }

        private readonly B<string> unreachableTitle = new(null);

        private void UpdateUnreachableWarning() {
            if (!Ready.Gen) { return; }
            if (HasUnreachableAreas) {
                unreachableTitle.Set(T.UnreachableAreasDetected_V1.F(UnreachableAreas.Count));
                if (!unreachableWarning.IsValid) {
                    unreachableWarning = Notification.Build(IconId.CWarning, null, 0, NotificationType.Warning)
                        .WithOnClick(() => {
                            if (UnreachableAreas.Count == area.Areas.Count) {
                                WPopup.Create(false, IconId.CRoom, T.MissingAirlock,
                                    T.MissingAirlockDesc).Spawn();
                            } else {
                                ShowUnreachableAreasPopup(area.S);
                            }
                        })
                        .WithTitleB(unreachableTitle)
                        .Sticky()
                        .Show();
                }
            } else {
                Notification.Remove(ref unreachableWarning);
            }
        }

        public static void ShowUnreachableAreasPopup(GameState s) {
            WPopup.Create(false, IconId.CRoom, T.UnreachableAreas,
                T.UnreachableAreasDesc)
                    .WithAction2(T.ShowAreas, () => s.Sig.ShowOverlay.Send(OverlayId.Areas))
                    .WithAction(T.Ok, null)
                    .Spawn();
        }

        public void UpdateAtmo(AtmoLayer layer) {
            for (int i = doors.Count - 1; i >= 0; i--) {
                DoorComp door = doors[i];
                door.OnAtmoRecalculated(layer);
            }
        }
    }
}
