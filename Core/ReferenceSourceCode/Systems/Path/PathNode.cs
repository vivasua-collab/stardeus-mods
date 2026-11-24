using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using Unity.Collections;
using KL.Grid;
using System.Collections.Generic;

namespace Game.Systems.Path {
    public struct PathNode {
        public int PosIdx;
        public float Difficulty;
        public int TeleportsTo;
        public PathNodeType Type;
        public NavPermission Deny;
        // TODO unwrap this in job code
        public bool IsDoor => (Type & PathNodeType.Door) == PathNodeType.Door;
        public bool IsWall => (Type & PathNodeType.Wall) == PathNodeType.Wall;
        public bool IsTileWithHeight => (Type & PathNodeType.TileWithHeight) == PathNodeType.TileWithHeight;
        public bool IsTeleporter => (Type & PathNodeType.Teleporter) == PathNodeType.Teleporter;
        public bool IsWorkSpot => (Type & PathNodeType.Port) == PathNodeType.Port;
        public bool IsWalkable => (Type & PathNodeType.Walkable) == PathNodeType.Walkable;
        public bool IsPassable => (Type & PathNodeType.Passable) == PathNodeType.Passable;
        // walked across, never stopped on (PassabilityComp.IsPassThroughOnly)
        public bool IsPassThroughOnly =>
            (Type & PathNodeType.PassThroughOnly) == PathNodeType.PassThroughOnly;

        private static void GetBasePassability(PassabilityComp pass, out bool isP,
            out bool isW, out float diff, out NavPermission deny) {
            if (pass == null) {
                isP = true;
                isW = false;
                diff = 0;
                deny = NavPermission.None;
            } else {
                isP = pass.IsPassable;
                isW = pass.IsWalkable;
                diff = pass.Difficulty;
                deny = pass.Deny;
            }
        }

        private static void GetWallPassability(PassabilityComp pass, out bool isP,
                out bool isW, out bool isDoor, out float diff, out NavPermission deny) {
            GetBasePassability(pass, out isP, out isW, out diff, out deny);
            isDoor = pass?.IsDoor == true;
        }

        public static void Update(ref NativeArray<PathNode> nodes, int posIdx,
                Dictionary<int, int> tpMap, HashSet<int> workSpots) {
            if (!EntityUtils.IsWithinBounds(posIdx)) { return; }
            var node = nodes[posIdx];

            bool isObjWalkable = false;
            bool isObjPassable = true;
            float objDiff = 0f;
            var objDeny = NavPermission.None;

            bool isWallWalkable;
            bool isWallPassable;
            bool isDoor;
            float wallDiff;
            var wallDeny = NavPermission.None;

            bool isFloorWalkable;
            bool isFloorPassable;
            float floorDiff;
            var floorDeny = NavPermission.None;

            bool isTeleporter = false;

            // Object passability
            var cur = EntityUtils.ObjAt(posIdx);
            var isObjPresent = cur != null;
            var isTileWithHeight = false;
            var isObjPassThroughOnly = false;
            PassabilityComp pass = null;

            if (isObjPresent) {
                var parent = cur.Transform.IsChild ? cur.Transform.Parent : cur;
                pass = parent.Passability;
                isTileWithHeight = parent.Graphics.HasHeight
                    && pass?.IsUnbuiltOverrideActive != true;
                isObjPassThroughOnly = pass?.IsPassThroughOnly == true;

                var tp = cur.Teleporter;
                if (tp != null) {
                    if (tpMap.TryGetValue(posIdx, out var tpTo) && tp.IsTeleporterActive) {
                        isTeleporter = true;
                        node.TeleportsTo = tpTo;
                    } else {
                        node.TeleportsTo = -1;
                    }
                } else {
                    node.TeleportsTo = -1;
                }

                if (parent.Transform.IsMulti && cur == parent) {
                    int posCount = parent.Transform.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < posCount; i++) {
                        var mPos = Caches.PosIdx[i];
                        #if !PRODUCTION_BUILD
                        if (!EntityUtils.IsWithinBounds(mPos)) {
                            D.Err("Subtile is OOB for: {0}", parent);
                        }
                        #endif
                        if (mPos == posIdx) {
                            GetBasePassability(pass, out isObjPassable,
                                out isObjWalkable, out objDiff, out objDeny);
                        } else {
                            // child pos
                            Update(ref nodes, mPos, tpMap, workSpots);
                        }
                    }
                } else {
                    GetBasePassability(pass, out isObjPassable, out isObjWalkable,
                        out objDiff, out objDeny);
                }
            } else {
                if (node.IsTeleporter) {
                    isTeleporter = false;
                    node.TeleportsTo = -1;
                }
                // This would set the defaults as they are already
                //GetBasePassability(null, out isObjPassable, out isObjWalkable, out objDiff);
            }

            // Wall passability
            cur = EntityUtils.WallAt(posIdx);
            GetWallPassability(cur?.Passability ?? null,
                out isWallPassable, out isWallWalkable, out isDoor, out wallDiff, out wallDeny);
            var isSolidWall = !isDoor && cur?.IsConstructed == true
                && cur.Passability?.IsUnbuiltOverrideActive != true;
            var isWallPassThroughOnly = cur?.Passability?.IsPassThroughOnly == true;

            // Floor passability
            cur = EntityUtils.FloorAt(posIdx);
            GetBasePassability(cur?.Passability ?? null,
                out isFloorPassable, out isFloorWalkable, out floorDiff, out floorDeny);

            var isPassable = isFloorPassable && isWallPassable && isObjPassable;
            var isWalkable = isPassable && (isFloorWalkable || isWallWalkable || isObjWalkable);
            node.Difficulty = floorDiff + wallDiff + objDiff;
            node.Deny = objDeny | wallDeny | floorDeny;

            node.Type = PathNodeType.None;
            if (isPassable) {
                node.Type |= PathNodeType.Passable;
            }
            if (isWalkable) {
                node.Type |= PathNodeType.Walkable;
            }
            if (isObjPassThroughOnly || isWallPassThroughOnly) {
                node.Type |= PathNodeType.PassThroughOnly;
            }
            if (isDoor) {
                node.Type |= PathNodeType.Door;
            } else if (isSolidWall) {
                node.Type |= PathNodeType.Wall;
            } else if (isTileWithHeight) {
                node.Type |= PathNodeType.TileWithHeight;
            }
            if (isTeleporter) {
                D.Warn("Making node a teleporter: {0}", Pos.StringFromTo(node.PosIdx, node.TeleportsTo));
                node.Type |= PathNodeType.Teleporter;
            }
            if (workSpots.Contains(posIdx)) {
                node.Type |= PathNodeType.Port;
            }

            nodes[posIdx] = node;
        }

        public static PathNode ForSpace(int posIdx) {
            return new PathNode {
                Difficulty = 0,
                Type = PathNodeType.Passable,
                //O2 = 0f,
                //Temp = 0f,
                PosIdx = posIdx,
                TeleportsTo = -1,
            };
        }
        private static NavPermission noWalkFly = NavPermission.Walking | NavPermission.Flying;
        private static NavPermission noRobotBio = NavPermission.Robot | NavPermission.Biological;
        private static NavPermission noFriendEnemy = NavPermission.Friendly | NavPermission.Enemy;

        public static string DenyStr(NavPermission perms) {
            var nobody = false;
            if ((perms & noWalkFly) == noWalkFly) { nobody = true; }
            if ((perms & noRobotBio) == noRobotBio) { nobody = true; }
            if ((perms & noFriendEnemy) == noFriendEnemy) { nobody = true; }
            if (nobody) {
                return T.PermissionsDenied_V1.F(Texts.Red(T.All));
            }
            var countEnabled = Enums.CountEnabledFlags(perms);
            if (countEnabled / (float) NavPermissions.DisplayOrder.Length > 0.5f) {
                // we are denying a lot, let's only say what we allow
                var ps = PermissionStr(perms, true, Texts.TMPColorGreen);
                if (string.IsNullOrWhiteSpace(ps)) {
                    return T.PermissionsDenied_V1.F(Texts.Red(T.All));
                }
                return T.PermissionsAllowed_V1.F(ps);
            } else {
                // we are allowing most, lets only say what we deny
                var ps = PermissionStr(perms, false, Texts.TMPColorRed);
                return T.PermissionsDenied_V1.F(ps);
            }
        }

        public static string PermissionStr(NavPermission permissions,
                bool inverse = false, string color = null) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var order = NavPermissions.DisplayOrder;
            for (int i = 0; i < order.Length; i++) {
                var perm = order[i];
                bool contains = (permissions & perm) == perm;
                if (contains != inverse) {
                    if (sb.Length > 0) {
                        sb.Append(", ");
                    }
                    if (color != null) {
                        sb.AppendFormat("<color={0}>{1}</color>",
                            color, NameForPerm(perm));
                    } else {
                        sb.Append(NameForPerm(perm));
                    }
                }
            }
            return sb.ToString();
        }

        public static string NameForPerm(NavPermission perm) {
            switch (perm) {
                case NavPermission.Animal: return T.NavPermAnimal;
                case NavPermission.Biological: return T.NavPermBiological;
                case NavPermission.Enemy: return T.NavPermEnemy;
                case NavPermission.Friendly: return T.NavPermFriendly;
                case NavPermission.Guest: return T.NounGuest;
                case NavPermission.Robot: return T.NavPermRobot;
                case NavPermission.Humanoid: return T.NavPermHumanoid;
                case NavPermission.Walking: return T.NavPermWalking;
                case NavPermission.Flying: return T.NavPermFlying;
            }
            D.Err("Unhandled nav permission name: {0}", perm);
            return T.Unknown;
        }

        public bool IsRoamable(bool grounded) {
            if ((Type & (PathNodeType.Door | PathNodeType.Teleporter
                    | PathNodeType.Port)) != PathNodeType.None) {
                return false;
            }
            if ((Type & PathNodeType.Passable) != PathNodeType.Passable) {
                return false;
            }
            if (grounded && ((Type & PathNodeType.Walkable) != PathNodeType.Walkable)) {
                return false;
            }
            return Difficulty < 0.1f;
        }
    }
}
