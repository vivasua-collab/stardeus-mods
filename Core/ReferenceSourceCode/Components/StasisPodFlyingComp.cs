using Game.Systems;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Firex;
using Game.Systems.Notify;

namespace Game.Components {
    public sealed class StasisPodFlyingComp : BaseComponent<StasisPodFlyingComp>,
            ITickProgressListener, IWithTrajectory {
        private Vector2 velocity;
        public int ContentsSeed;
        private bool hasMissedLanding;
        private bool isPathCalculated;
        private float lastRotation;
        private Vector2 hitPosition;
        private bool isTakingOff;
        public bool IsMidFlight => true;
        public bool CanChangeTrajectory => !isTakingOff && !hasMissedLanding;
        public float TrajectoryChangeEnergyCost => 50;
        private int riggedForExplosionAt;
        public string RiggedEventId;

        public void RigForExplosionAt(int at) {
            riggedForExplosionAt = at;
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StasisPodFlyingComp());
        }
        public bool WasAligned { get; set; }
        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("ContentsSeed", ContentsSeed);
            data.SetBool("TakingOff", isTakingOff);
            data.SetBool("WasAligned", WasAligned);
            data.SetBool("HasMissedLanding", hasMissedLanding);
            data.SetBool("IsPathCalculated", isPathCalculated);
            data.SetVector2("Velocity", velocity);
            data.SetFloat("LastRotation", lastRotation);
            data.SetString("RiggedEvent", RiggedEventId);
            data.SetVector2("HitPosition", hitPosition);
        }

        protected override void OnLoad(ComponentData data) {
            hasMissedLanding = data.GetBool("HasMissedLanding", false);
            lastRotation = data.GetFloat("LastRotation", 0);
            isTakingOff = data.GetBool("TakingOff", false);
            ContentsSeed = data.GetInt("ContentsSeed", 1);
            WasAligned = data.GetBool("WasAligned", false);
            isPathCalculated = data.GetBool("IsPathCalculated", false);
            hitPosition = data.GetVector2("HitPosition");
            velocity = data.GetVector2("Velocity", new(-1, -1));
            RiggedEventId = data.GetString("RiggedEvent", null);
        }

        public override string ToString() {
            return " * StasisPodFlying";
        }

        public override void OnLateReady(bool wasLoaded) {
            Obj.Transform.SkipMoveLoose = true;
            // Obj.IsFlying = true;
            S.Sig.OnShipRotate.AddListener(OnShipRotate);
            if (ContentsSeed == 0) {
                ContentsSeed = S.Rng.Range(1, int.MaxValue);
                //D.Warn("Replacing 0 seed with: {0}", ContentsSeed);
            }

            if (isPathCalculated) {
                //Ready.WhenRendering(this, "Restore storage capsule looks after load", () => {
                    if (hasMissedLanding) {
                        SwitchSortOrder(SpriteOrder.ObjBelow);
                    }
                    // D.Log("Restoring obj rotation: {0}", lastRotation);
                    Obj.Graphics.SetRotation(lastRotation);
                //});
            } else {
                // Since we have just created the object, there will be darkness
                Obj.Graphics.Show(false);
            }
        }
        private float lastShipRotation;
        private void OnShipRotate(float rotation) {
            S.Sys.Ship.RotateSpaceObj(rotation, velocity, Obj.Position, ref lastShipRotation);
        }

        private Tile podTile;

        public void LaunchUpwards(Tile podTile) {
            this.podTile = podTile;
            velocity = new Vector2(S.Rng.Range(-0.003f, 0.003f), 2.5f) * EntityUtils.MapSizeRatio;
            // For the shadow
            hitPosition = Entity.Position;
            isTakingOff = true;
        }

        private void CalculatePath() {
            // 1. Transform.Position is now at the point of impact
            // we want to move the obj to the edge of the map
            // and then calculate velocity vector, and speed, so the asteroid
            // would land in target spot

            // figure out on which edge we will spawn the roid
            var gw = S.GridWidth;
            var gh = S.GridHeight;
            hitPosition = Obj.Transform.Position;
            var start = new Vector2(S.Rng.Range(1f, gw - 2f), gh - 1f);

            // Here we have both start and end positions, we can not get
            // a normalized velocity vector
            var velVecNorm = (hitPosition - start).normalized;

            velocity = velVecNorm * Rng.URange(1.5f, 2.5f) * EntityUtils.MapSizeRatio;
            isPathCalculated = true;

            Obj.Transform.TeleportTo(start);
            Obj.Graphics.TeleportTo(start);
            Obj.Graphics.Show(true);
        }

        private void SwitchSortOrder(int order) {
            Obj.Graphics.RemoveShadow();
            Obj.Graphics.OverrideSortOrder(order);
            if (Entity.TryGetComponent<ParticleTrailComp>(out var pt)) {
                pt.TrySwitchTo(null, "_Below");
            }
        }

        private NotificationHandle incomingEvent;

        private void NotifyIncoming() {
            if (incomingEvent.IsValid) { return; }
            S.Sig.AddObjWithTrajectory.Send(this);
            incomingEvent = Notification.BuildFor(Obj, null,
                    T.StasisPodIncoming, 0, NotificationType.Warning)
                .Group("spod_in")
                .Show();
            var podText = Texts.LinkToObj(Obj, T.StasisPodIncoming);
            S.Sig.ShowFanfare.Send(podText);
            S.Sys.Log.AddLine(podText);
        }

        private bool isWaitingForLanding;
        private bool skippedFirstTickProgress;

        public override void OnRemove() {
            S.Sig.OnShipRotate.RemoveListener(OnShipRotate);
            RemoveIncomingEvent();
        }

        public void TickProgress(float tickProgress) {
            if (isWaitingForLanding) { return; }
            // FIXME perhaps not needed?
            if (!skippedFirstTickProgress) {
                skippedFirstTickProgress = true;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                S.Objs.Destroy(Obj, false);
                return;
            }
            if (!Obj.IsActive) { return; }
            // S.Clock.PreventMultiTick();
            if (!isPathCalculated) {
                if (isTakingOff) {
                    isPathCalculated = true;
                    if (podTile != null) {
                        var pt = podTile;
                        podTile = null;
                        S.Clock.OnNextUpdate(() => {
                            new CmdRemoveTile(pt).Execute(S);
                            pt.Outdoors.UpdateOutdoorsState(G.Ticks);
                            if (!pt.Outdoors.IsOutdoors) {
                                Explosion.Create(S, explosionLevel,
                                    position: pt.Position,
                                    flags: ExplosionFlags.SkipEvent);
                                // EntityUtils.CreateExplosion(pt.Position, 5f, 400f, false);
                            }
                        });
                    }
                } else {
                    CalculatePath();
                }
            }
            var tpf = S.Clock.TicksPerUpdate;
            var curPos = Obj.Transform.Position;
            var newPos = curPos + velocity * tpf - shipShift;
            if (!isTakingOff && !hasMissedLanding &&
                    Vectors.SqrDistance(curPos, newPos) >
                        Vectors.SqrDistance(curPos, hitPosition)) {
                // We are overshooting!
                newPos = hitPosition;
            }
            shipShift = Consts.V2Zero;

            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => S.Objs.Destroy(Obj, true));
            } else {
                if (!isTakingOff) {
                    NotifyIncoming();
                }
                Obj.Transform.TeleportTo(newPos);
                var newPosIdx = Obj.Transform.PosIdx;
                Obj.Graphics.TeleportTo(newPos);

                if (!isTakingOff && !hasMissedLanding) {
                    // Check if we should hit
                    var vecTillImpact = newPos - hitPosition;
                    var heightFromShip = vecTillImpact.y;
                    if (heightFromShip < 0.4f) {
                        hitPosition = newPos;
                        var floor = EntityUtils.FloorAt(newPosIdx);
                        if (floor?.IsConstructed == true) {
                            isWaitingForLanding = true;
                            TurnIntoTile();
                        } else {
                            // Make sure we destroy any objects floating in space
                            // at hit position
                            Caches.TileSet.Clear();
                            Pos.ForEachAround(EntityUtils.ToPos(hitPosition), 1,
                                true, true, (p) => {
                                    if (EntityUtils.IsWithinBounds(p)) {
                                        var spaceObj = EntityUtils.ParentObjAt(p);
                                        if (spaceObj?.IsActive == true) {
                                            spaceObj.Constructable.BreakDown(2f, true, immediate: true);
                                            if (Caches.TileSet.Add(spaceObj)) {
                                                // only create once
                                                Explosion.Create(S, explosionLevel - 1,
                                                    position: hitPosition,
                                                    flags: ExplosionFlags.SkipEvent | ExplosionFlags.Immediate);
                                                // EntityUtils.CreateExplosion(
                                                //     hitPosition, 3f, 300f, false);
                                            }
                                        }
                                    }
                                    return true;
                                });
                            hasMissedLanding = true;
                            SwitchSortOrder(SpriteOrder.ObjBelow);
                        }
                        RemoveIncomingEvent();
                    }
                }
            }
            if (isTakingOff && Obj.Graphics.IsHidden) {
                Obj.Graphics.Show(true);
            }
            var shadowHeight = (Obj.Graphics.Position - hitPosition).y;
            Obj.Graphics.SetShadowHeight(shadowHeight);
            if (isTakingOff) { return; }
            if (rotationSpeed == 0) {
                rotationSpeed = S.Rng.Range(-1f, 1);
            }
            lastRotation = (lastRotation + rotationSpeed * tpf) % 360;
            Obj.Graphics.SetRotation(lastRotation);
        }

        private int explosionLevel;

        protected override void OnConfig() {
            explosionLevel = Config.GetInt(PropertyIdH.ImpactExplosionLevel, 4);
        }

        private void RemoveIncomingEvent() {
            if (incomingEvent.IsValid) {
                Notification.Remove(ref incomingEvent);
            }
        }

        // FIXME refactor with breach capsule
        private void TurnIntoTile() {
            if (!Obj.IsActive) {
                // D.Warn("Stasis pod was destroyed before turning into tile {0}", Obj);
                return;
            }
            var health = Obj.Health.Health;
            bool wasSelected = S.Query.IsSelected.Ask(Obj);
            S.Clock.OnNextUpdate(() => S.Objs.Destroy(Obj));
            //S.Objs.Destroy(Obj, false);
            if (riggedForExplosionAt == Obj.PosIdx) {
                //D.Warn("Rigged for explosion at {0}", Pos.String(riggedForExplosionAt));
                Explosion.Create(S, explosionLevel,
                    position: Obj.Position,
                    delay: 1,
                    flags: ExplosionFlags.SkipEvent);
                // // delay damage 1 ticks intentional!
                // var expl = new CmdCreateExplosion(Obj.Position, radius, str, delayDamageTicks: 1);
                // expl.Execute(S);
            }
            var podTile = EntityUtils.CreatePrototype<Tile>(DefIds.ObjectsDevicesStasisPod);
            var pos = EntityUtils.ToPos(hitPosition);

            if (!S.Sys.Areas.IsOutside(pos)) {
                // blow through the roof
                Explosion.Create(S, explosionLevel,
                    posIdx: pos,
                    flags: ExplosionFlags.SkipEvent);
                // EntityUtils.CreateExplosion(EntityUtils.CenterOf(pos), 3f, 300f, false);
            }
            if (TryAdjustHitPosition(pos, true, out var newHP)) {
                pos = newHP;
            }
            if (!SetTileRotation(podTile, pos)) {
                var wspot = podTile.Transform.RotatedOutPort(pos);
                var topmost = EntityUtils.GetTopmostParentTile(wspot);
                if (topmost != null && topmost.Definition.LayerId > WorldLayer.Floor) {
                    D.Warn("Destroying obj at podTile workspot: {0} -> {1}",
                        Pos.String(wspot), topmost);
                    Explosions.CreateExplosionFX(S, EntityUtils.CenterOf(wspot), 3f, 200f, true);
                    if (topmost.IsConstructed) {
                        topmost.Constructable.BreakDown(0.5f, false, true, true, true, false);
                    } else {
                        AIGoal.Cancel(topmost.Goal, G.Ticks);
                    }
                }
            }
            var cmd = new CmdPlaceTile(pos, podTile, warn: false, true);
            cmd.Execute(S);
            // this will make sure that pod tile and the floor will have been made immutable
            if (!cmd.IsSuccess) {
                bool areaCleanupFailed = false;

                bool hadImmutableObj = false;
                var cnt = podTile.Transform.GetSubtilePos(pos, Caches.PosIdx9);
                for (int i = 0; i < cnt; i++) {
                    var p = Caches.PosIdx9[i];
                    var obj = EntityUtils.ParentObjAt(p) ?? EntityUtils.WallAt(p);
                    if (obj?.IsActive == true && obj.IsImmutable) {
                        hadImmutableObj = true;
                        D.Log("Stasis Pod cannot land, there is immutable obstacle: {0}", obj);
                    }
                }

                if (hadImmutableObj) {
                    Explosions.CreateExplosionFX(S, Obj.Position, 3f, 300f, true);
                    return;
                }

                cnt = podTile.Transform.GetSubtilePos(pos, Caches.PosIdx9);
                for (int i = 0; i < cnt; i++) {
                    var p = Caches.PosIdx9[i];
                    CleanupPosition(p, ref areaCleanupFailed);
                    if (areaCleanupFailed) { break; }
                }
                if (areaCleanupFailed) {
                    // We failed to turn flying pod into tile
                    D.Warn("Failed turning pod into tile, area cleanup failed");
                    isWaitingForLanding = false;
                    return;
                }
                cmd = new CmdPlaceTile(pos, podTile, warn: false, true, true);
                cmd.Execute(S);
            }
            if (!cmd.IsSuccess) {
                D.Err("Couldn't create a landed stasis pod! {0}", Entity);
                isWaitingForLanding = false;
                Explosion.Create(S, explosionLevel,
                    position: hitPosition,
                    flags: ExplosionFlags.SkipEvent);
                // EntityUtils.CreateExplosion(hitPosition, 3f, 400f, false);
                return;
            }

            // Make sure workspot is clear
            // IMPORTANT: some objects may have workspot inside the object
            // i.e. breach capsule. This would then remove it
            var outPort = podTile.Transform.OutPort;
            var wsObj = EntityUtils.GetTopmostParentTile(outPort);
            if (wsObj?.Definition.LayerId > WorldLayer.Floor) {
                // Destroy anything in the work spot on landing
                wsObj.Constructable.BreakDown(1f, false, immediate: true);
            }
            The.SysSig.GamepadRumble.Send(0.3f, 0.3f, 0.2f);

            // Inherit content seed from the flying capsule
            var pod = podTile.GetComponent<StasisPodComp>();
            pod.RiggedEventId = RiggedEventId;
            podTile.Health.SetHealth(health);
            pod.ContentsSeed = ContentsSeed;
            // A player who followed the pod in (selected it) has already seen it land
            if (!wasSelected) {
                UnseenObjectsSys.For(S)?.Track(podTile);
            }

            S.Clock.ExecuteAfterTicks(1, () => {
                // extra validation if there is any garbage left
                int cnt = podTile.Transform.GetSubtilePos(pod.Tile.PosIdx, Caches.PosIdx);
                for (int i = 0; i < cnt; i++) {
                    var p = Caches.PosIdx[i];
                    var obj = EntityUtils.ObjAt(p);
                    var parent = EntityUtils.ParentObj(obj);
                    if (parent != podTile) {
                        D.Err("Some shit in our position: {0}", parent);
                        parent.Constructable.BreakDown(2f, true, immediate: true);
                    }
                    var wall = EntityUtils.WallAt(p);
                    if (wall != null) {
                        wall.Constructable.BreakDown(2f, true, true);
                    }
                    var floor = EntityUtils.WallAt(p);
                    if (floor != null && !floor.Constructable.IsConstructed) {
                        floor.Constructable.FinishConstruction(false);
                    }
                }
                EntityUtils.TriggerChange(pod.Tile);
                if (wasSelected) {
                    S.Sig.SelectEntityUnfocused.Send(pod.Tile);
                }
            });
        }

        private bool SetTileRotation(Tile tile, int pos) {
            tile.Transform.Rotation = Rng.UFrom(Facing.Types);
            var dir = Rng.UOneOf(Facing.Type.Left, Facing.Type.Right);
            for (int i = 0; i < 4; i++) {
                var ws = tile.Transform.RotatedOutPort(pos);
                if (EntityUtils.IsEmptyFloorAt(ws)) { return true; }
                tile.Transform.Rotation = Facing.Rotate(
                    tile.Transform.Rotation, dir);
            }
            return false;
        }

        // This desperately tries to prevent the pod crashing into anything
        private bool TryAdjustHitPosition(int hp, bool propagate, out int adjustedHP) {
            adjustedHP = Pos.Invalid;
            var isHPOK = EntityUtils.IsEmptyFloorAt(hp);
            if (!isHPOK && !propagate) {
                return false;
            }
            var hpN = Pos.NeighborAt(hp, 0, 1);
            var hpE = Pos.NeighborAt(hp, 1, 0);
            var hpNE = Pos.NeighborAt(hp, 1, 1);

            var isNOK = EntityUtils.IsEmptyFloorAt(hpN);
            var isEOK = EntityUtils.IsEmptyFloorAt(hpE);
            var isNEOK = EntityUtils.IsEmptyFloorAt(hpNE);
            if (isHPOK && isNOK && isNEOK && isEOK) {
                adjustedHP = hp;
                // All 4 ok!
                return true;
            }
            // fuck
            if (!propagate) {
                return false;
            }
            var hpS = Pos.NeighborAt(hp, 0, -1);
            var hpW = Pos.NeighborAt(hp, -1, 0);
            var hpSW = Pos.NeighborAt(hp, -1, -1);
            var hpNW = Pos.NeighborAt(hp, -1, 1);
            var hpSE = Pos.NeighborAt(hp, 1, -1);
            var isSOK = EntityUtils.IsEmptyFloorAt(hpS);
            var isWOK =  EntityUtils.IsEmptyFloorAt(hpW);
            var isSWOK = EntityUtils.IsEmptyFloorAt(hpSW);
            var isSEOK = EntityUtils.IsEmptyFloorAt(hpSE);
            var isNWOK = EntityUtils.IsEmptyFloorAt(hpNW);
            if (isSOK && TryAdjustHitPosition(hpS, false, out adjustedHP)) {
                return true;
            }
            if (isNOK && TryAdjustHitPosition(hpN, false, out adjustedHP)) {
                return true;
            }
            if (isEOK && TryAdjustHitPosition(hpE, false, out adjustedHP)) {
                return true;
            }
            if (isWOK && TryAdjustHitPosition(hpW, false, out adjustedHP)) {
                return true;
            }
            if (isNEOK && TryAdjustHitPosition(hpNE, false, out adjustedHP)) {
                return true;
            }
            if (isNWOK && TryAdjustHitPosition(hpNW, false, out adjustedHP)) {
                return true;
            }
            if (isSEOK && TryAdjustHitPosition(hpSE, false, out adjustedHP)) {
                return true;
            }
            if (isSWOK && TryAdjustHitPosition(hpSW, false, out adjustedHP)) {
                return true;
            }
            return false;
        }

        private void CleanupPosition(int p, ref bool areaCleanupFailed) {
            var obj = EntityUtils.ParentObjAt(p) ?? EntityUtils.WallAt(p);
            if (obj?.IsActive == true) {
                if (obj.IsImmutable) {
                    // Edge case when a pod tries to land on say another pod that is
                    // already immutable
                    D.Warn("Landing on an immutable object! Removing pod tile");
                    areaCleanupFailed = true;
                    return;
                }
                if (S.Rng.Chance(0.25f)) {
                    S.CmdQ.Enqueue(new CmdStartFire(p));
                }
                Explosions.CreateExplosionFX(S, obj.Position, 3f, 300f, true);
                obj.Constructable.BreakDown(2f, true, immediate: true);
            }
            if (S.Objs.ByPos.TryGetValue(p, out var objs)) {
                var count = objs.Count;
                for (int i = count - 1; i >= 0; i--) {
                    Obj o = objs[i];
                    if (o == Obj) { continue; }
                    S.Objs.Destroy(o, false);
                }
            }
        }

        private float rotationSpeed;

        private Vector2 shipShift;
        public void OnShipMove(Vector2 dir) {
            if (!isTakingOff && !hasMissedLanding) {
                shipShift = dir;
                hitPosition -= dir;
            }
        }


        public bool InfluenceTrajectory(Vector2 target, float strength) {
            if (isTakingOff || hasMissedLanding) { return false; }
            var prevHitPosition = hitPosition;
            // 1. Set new hit position
            hitPosition = Vector2.MoveTowards(hitPosition, target, strength);
            if (hitPosition != prevHitPosition) {
                // 2. Make velocity vector point to new hit position
                var mag = velocity.magnitude;
                velocity = (hitPosition - Obj.Graphics.Position).normalized * mag;
                return true;
            }
            // we are already on this trajectory
            return false;
        }

        public bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity) {
            if (isTakingOff || hasMissedLanding) {
                velocity = Consts.V2Zero;
                hitPoint = Consts.V2Zero;
                return false;
            }
            velocity = this.velocity;
            hitPoint = hitPosition;
            return true;
        }
    }
}
