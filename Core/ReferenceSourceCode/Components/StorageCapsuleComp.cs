#if !PRODUCTION_BUILD
// #define DEBUG_CAPSULE
#endif
using Game.Systems;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.Systems.Firex;
using Game.Systems.Story;
using Game.Systems.Rendering;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Components {
    public sealed class StorageCapsuleComp : BaseComponent<StorageCapsuleComp>,
            ITickProgressListener, IDisposableComp,
            ITickListener, IOpenable, IEntityPanelGoalProvider,
            IExpeditionLootable, IWithTrajectory {
        private Vector2 velocity;
        public bool CanChangeTrajectory => !hasLanded && !hasMissedLanding;
        public float TrajectoryChangeEnergyCost => 50;
        private bool hasLanded;
        public bool HasLanded => hasLanded;
        public bool IsMidFlight => !hasLanded;
        private bool isOpen;
        public bool IsOpen => isOpen;
        public string RiggedEventId;
        public int AutoOpenInTicks;
        public int ContentsSeed;
        private bool hasMissedLanding;
        private bool isPathCalculated;
        private float lastRotation;
        private Vector2 hitPosition;
        private float rotationSpeed;
        private int riggedForExplosionAt;
        public string FactionId;
        /// <summary>Pre-distributed raid point share from the spawning story event.
        /// When > 0, the CapsuleHostileBeing raid uses preset points instead of a full plan.</summary>
        public float RaidPoints;
        /// <summary>Gear tier of the parent plan this share was cut from (-1 = none set).
        /// Passed through so this capsule's own, smaller point share doesn't get recomputed
        /// to a lower tier than the parent raid actually earned.</summary>
        public int RaidGearTier = -1;
        /// <summary>Raiders generated up front by the multi-capsule story event and carried
        /// hidden until the capsule opens (see RaidPayload). Takes precedence over RaidPoints,
        /// which remains only for capsules already in flight in older saves.</summary>
        public int[] RaiderIds;

        public void RigForExplosionAt(int at) {
            riggedForExplosionAt = at;
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StorageCapsuleComp());
        }
        public bool WasAligned { get; set; }

        public bool CanDisposeNow(out string reason) {
            if (isOpen) {
                reason = null;
                return true;
            }
            reason = T.MustOpenFirst;
            return false;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("HasLanded", hasLanded);
            data.SetBool("WasAligned", WasAligned);
            data.SetBool("IsOpened", isOpen);
            data.SetInt("ContentsSeed", ContentsSeed);
            data.SetFloat("LastRotation", lastRotation);
            data.SetBool("HasMissedLanding", hasMissedLanding);
            data.SetBool("IsPathCalculated", isPathCalculated);
            data.SetVector2("Velocity", velocity);
            data.SetVector2("HitPosition", hitPosition);
            data.SetString("RiggedEvent", RiggedEventId);
            data.SetInt("AutoOpenInTicks", AutoOpenInTicks);
            data.SetString("FactionId", FactionId);
            data.SetFloat("RaidPoints", RaidPoints);
            data.SetInt("RaidGearTier", RaidGearTier);
            data.SetIntSet("RaiderIds", RaiderIds);
        }

        protected override void OnLoad(ComponentData data) {
            hasMissedLanding = data.GetBool("HasMissedLanding", false);
            WasAligned = data.GetBool("WasAligned", false);
            hasLanded = data.GetBool("HasLanded", false);
            isOpen = data.GetBool("IsOpened", false);
            if (!hasLanded) {
                lastRotation = data.GetFloat("LastRotation", 0);
            }
            ContentsSeed = data.GetInt("ContentsSeed", 1);
            isPathCalculated = data.GetBool("IsPathCalculated", false);
            hitPosition = data.GetVector2("HitPosition");
            velocity = data.GetVector2("Velocity", new Vector2(-1, -1));
            RiggedEventId = data.GetString("RiggedEvent", null);
            AutoOpenInTicks = data.GetInt("AutoOpenInTicks", 0);
            FactionId = data.GetString("FactionId", null);
            RaidPoints = data.GetFloat("RaidPoints", 0f);
            RaidGearTier = data.GetInt("RaidGearTier", -1);
            RaiderIds = data.GetIntSet("RaiderIds", null);
        }

        public override void OnRemove() {
            if (!isOpen && !Arrays.IsEmpty(RaiderIds)) {
                // Destroyed before opening: the raiders inside are lost. Intended.
                RaidPayload.Discard(S, RaiderIds);
                RaiderIds = null;
            }
        }

        public override string ToString() {
            return " * StorageCapsule";
        }

        private void InteractWithCapsule() {
            if (!hasLanded) {
                UISounds.PlayActionDenied();
                return;
            }
            if (!isOpen) {
                if (!TryOrderOpen()) {
                    UISounds.PlayActionDenied();
                }
            }
        }
        public override void OnLateReady(bool wasLoaded) {
            if (ContentsSeed == 0) {
                ContentsSeed = S.Rng.Range(1, int.MaxValue);
            }

            if (!Ready.Gen) {
                isPathCalculated = true;
                hasLanded = true;
            }

            Obj.Movable.IsForbidden = !hasLanded;
            // Obj.IsFlying = !hasLanded;
            // Obj.Processable.IsProcessableNow = isOpen;
            //Obj.Transform.SkipMoveLoose = !hasLanded;

            if (!isOpen && wasLoaded) {
                Ready.WhenGameLate(this, "Capsule", NotifyIncoming);
            }

            if (isPathCalculated || hasLanded) {
                Ready.When(WhenH.ObjsLoaded, () => {
                    if (hasLanded) {
                        RemoveTrail();
                        SwitchRenderer(SpriteOrder.ObjBelowFlat);
                        UpdateGraphic();
                    } else if (hasMissedLanding) {
                        SwitchToBelow();
                        //SwitchRenderer(SpriteOrder.ObjBelow);
                    }
                    // D.Log("Restoring obj rotation: {0}", lastRotation);
                    Obj.Graphics.SetRotation(lastRotation);
                    Obj.Light.SwitchLight(hasLanded && !isOpen);
                });
            } else {
                Obj.Graphics.Show(false);
                // Since we have just created the object, there will be darkness
            }
        }
        private void MakeLanded() {
            hasLanded = true;
            RemoveTrail();
            Obj.Transform.SkipMoveLoose = false;
            Obj.Movable.IsForbidden = false;
            if (!S.Sys.Areas.IsOutside(Obj.PosIdx)) {
                // blow through the roof
                Explosion.Create(S, explosionLevel,
                    position: Obj.Position,
                    flags: ExplosionFlags.SkipEvent);
                // EntityUtils.CreateExplosion(EntityUtils.CenterOf(Obj.PosIdx), 1f, 100f, false);
            }
            SwitchRenderer(SpriteOrder.ObjBelowFlat);
            if (S.Query.IsSelected.Ask(Obj)) {
                UIUtils.RefreshSelectedEntityUI();
            }
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

        private void SwitchRenderer(int newOrder) {
            // D.Warn("Switching renderer for {0} -> {1}", Entity, newOrder);
            Obj.Graphics.RemoveShadow();
            Obj.Light.SwitchLight(hasLanded && !isOpen);
            Obj.Graphics.ChangeRenderer(Renderers.Sprites, newOrder);
        }

        public bool TryOrderOpen() {
            if (TryGetGoal(out var g)) {
                return false;
            }
            g = CreateGoal(JobTypeIdH.Hauling, IconId.COpenPackage, T.OpenCapsule, this)
                .WithWorldToDesired(AIVarsH.IsContainerOpen, false, true)
                .ByPlayer(true)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WriteInitialState();

            return entity.TrySetGoal(g);
        }

        private NotificationHandle incomingEvent;

        private void NotifyIncoming() {
            if (incomingEvent.IsValid) { return; }
            if (!hasLanded) {
                S.Sig.AddObjWithTrajectory.Send(this);
            }
            incomingEvent = Notification.BuildFor(Obj, null, T.StorageContainerIncoming,
                0, NotificationType.Info)
                .Group("cap_in")
                .Show();
            if (!Ready.Gen) { return; }
            S.Sys.Log.AddLine(Obj, T.StorageContainerIncoming);
            S.Sig.ShowFanfare.Send(Texts.LinkToObj(Obj, T.StorageContainerIncoming));
        }

        private int lastPos;
        private bool hasTickedProgress;

        public void TickProgress(float tickProgress) {
            if (hasLanded) { return; }
            if (!hasTickedProgress) {
                hasTickedProgress = true;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                S.Objs.Destroy(Obj, false);
                return;
            }
            if (!Obj.IsActive) { return; }
            // S.Clock.PreventMultiTick();
            var tpf = S.Clock.TicksPerUpdate;
            if (!isPathCalculated) {
                CalculatePath();
            }
            var curPos = Obj.Transform.Position;
            var newPos = curPos + velocity * tpf - shipShift;
            if (!hasMissedLanding && Vectors.SqrDistance(curPos, newPos) > Vectors.SqrDistance(curPos, hitPosition)) {
                // We are overshooting!
                newPos = hitPosition;
            }
            shipShift = Consts.V2Zero;
            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => S.Objs.Destroy(Obj, true));
            } else {
                NotifyIncoming();
                Obj.Transform.TeleportTo(newPos);
                var newPosIdx = Obj.Transform.PosIdx;
                Obj.Graphics.TeleportTo(newPos);
                if (!hasMissedLanding) {
                    lastPos = newPosIdx;
                    // Check if we should hit
                    var vecTillImpact = newPos - hitPosition;
                    var heightFromShip = vecTillImpact.y;
                    if (heightFromShip < 0.4f) {
                        hitPosition = newPos;
                        var floor = EntityUtils.FloorAt(newPosIdx);
                        if (floor?.IsConstructed == true) {
                            newPos = Vector2.Lerp(hitPosition, newPos, Rng.URange(0.1f, 0.33f));
                            Obj.Transform.TeleportTo(newPos);
                            Obj.Graphics.TeleportTo(newPos);
                            MakeLanded();
                            lastRotation = 0;
                            Obj.Graphics.SetRotation(0);
                            CheckDropDamage();
                            The.SysSig.GamepadRumble.Send(0.2f, 0.2f, 0.1f);
                            // Lands wherever the ship is, rarely where the player looks
                            if (Obj.IsActive && !isOpen) {
                                UnseenObjectsSys.For(S)?.Track(Obj);
                            }
                        } else {
                            hasMissedLanding = true;
                            SwitchToBelow();
                        }
                    }
                }
            }
            if (!hasLanded) {
                var shadowHeight = (Obj.Graphics.Position - hitPosition).y;
                Obj.Graphics.SetShadowHeight(shadowHeight);
                if (rotationSpeed == 0) {
                    rotationSpeed = S.Rng.Range(-1f, 1);
                }
                lastRotation = (lastRotation + rotationSpeed * tpf) % 360;
                Obj.Graphics.SetRotation(lastRotation);
            }
        }

        private void SwitchToBelow() {
            SwitchRenderer(SpriteOrder.ObjBelow);
            if (Entity.TryGetComponent<ParticleTrailComp>(out var pt)) {
                pt.TrySwitchTo(null, "_Below");
            }
        }

        private int explosionLevel;

        protected override void OnConfig() {
            explosionLevel = Config.GetInt(PropertyIdH.ImpactExplosionLevel, 3);
        }

        private void CheckDropDamage() {
            if (riggedForExplosionAt == lastPos) {
                Explosion.Create(S, explosionLevel, position: Obj.Position);
            }
            var p = lastPos;
            var obj = EntityUtils.ParentObjAt(p) ?? EntityUtils.WallAt(p);
            if (obj?.IsActive == true) {
                if (S.Rng.Chance(0.25f)) {
                    S.CmdQ.Enqueue(new CmdStartFire(p));
                }
                obj.MaterialStorage?.DumpContents();
                obj.Constructable.BreakDown(2f, true, immediate: true);
                Explosions.CreateExplosionFX(S, Obj.Position, 3f, 300f, true);
            }
            if (S.Objs.ByPos.TryGetValue(p, out var objs)) {
                var count = objs.Count;
                for (int i = count - 1; i >= 0; i--) {
                    Obj o = objs[i];
                    if (o == Obj) { continue; }
                    var op = o.Position;
                    var dir = (op - Obj.Position).normalized;
                    dir *= 0.5f;
                    o.Transform.TeleportTo(op + dir);
                    o.Graphics.TeleportTo(op + dir);
                    // S.CmdQ.Enqueue(new CmdRemoveObj(o));
                }
            }
        }


        private void UpdateGraphic() {
            if (isOpen) {
                // To make things appear in the right place
                Obj.Graphics.OverrideGraphic($"{Obj.Graphics.GraphicId}_Open");
                Obj.Graphics.SetRotation(lastRotation);
            }
        }

        private void RemoveTrail() {
            Entity.GetComponent<ParticleTrailComp>().DisableParticles();
        }

        public void Open(Being opener) {
            if (isOpen) { return; }
            isOpen = true;
            UpdateGraphic();
            Obj.Light.SwitchLight(false);

            Notification.Remove(ref incomingEvent);

            // Opened on its own (auto-open timer): nobody completes the player's Open task,
            // so it would stay behind and keep sending workers to an open capsule
            if (opener == null) {
                TryClearGoal("Capsule opened on its own");
            }

            // task isn't over just yet, so we can't refresh right away
            if (S.Query.IsExclusivelySelected.Ask(Obj)) {
                S.Clock.ExecuteAfterFramesForUI(1, UIUtils.RefreshSelectedEntityUI);
            }

            var order = S.Query.GetOnlineOrderForCapsule.Ask(EntityId);
            if (order != null) {
                var sb = Caches.StringBuilder;
                sb.Clear();
                foreach (var item in order.Items) {
                    item.QuantitySelected = item.Quantity;
                    var obj = item.SpawnAt(S, Obj.PosIdx);
                    sb.AppendFormat(" {0} {1}\n", GlyphMap.ArrowRight,
                        Texts.LinkToObj(obj, $"{item.Name} x{item.Quantity}"));
                }
                WPopup.Create(false, IconId.COpenPackage, T.StorageCapsule, null,
                        sb.ToString().TrimEnd())
                    .Spawn(true);

                return;
            }


            // The rigged event takes over the carried raiders from here (see RaidPayload).
            var ids = RaiderIds;
            RaiderIds = null;
            var started = CapsuleOpenHelper.Start(S, this, Obj, opener, ContentsSeed,
                RiggedEventId, FactionId, RaidPoints, raidGearTier: RaidGearTier,
                raiderIds: ids);
            if (!started && !Arrays.IsEmpty(ids)) {
                RaidPayload.Discard(S, ids);
            }
        }

        public void OnSpawnLoot(Expedition exp) {
            if (Obj == null) { return; }
            MakeLanded();
            Obj.Graphics.Show(true);
        }

        private Vector2 shipShift;
        public void OnShipMove(Vector2 dir) {
            shipShift = dir;
            if (!hasLanded) {
                hitPosition -= dir;
            }
        }

        public bool InfluenceTrajectory(Vector2 target, float strength) {
            if (hasLanded) { return false; }
            var prevHitPosition = hitPosition;
            // 1. Set new hit position
            hitPosition = Vector2.MoveTowards(hitPosition, target, strength);
            //D.Log("Moved position: {0} -> {1}", prevHitPosition, hitPosition);
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
            if (hasMissedLanding || hasLanded) {
                velocity = Consts.V2Zero;
                hitPoint = Consts.V2Zero;
                return false;
            }
            velocity = this.velocity;
            hitPoint = hitPosition;
            return true;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public void Tick(long currentTick) {
            if (isOpen || AutoOpenInTicks < 1 || !hasLanded ) {
                return;
            }
            AutoOpenInTicks--;
            if (AutoOpenInTicks < 1) {
                D.Warn("Auto opening capsule: {0}", Entity);
                Open(null);
            }
        }
    }
}
