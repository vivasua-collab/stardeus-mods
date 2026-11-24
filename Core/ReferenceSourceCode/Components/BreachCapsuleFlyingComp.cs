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
using Game.Data.Factions;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Systems.Ships;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Systems.Voice;

namespace Game.Components {
    public sealed class BreachCapsuleFlyingComp : BaseComponent<BreachCapsuleFlyingComp>,
            ITickProgressListener, IWithTrajectory, IThreat,
            IWithFaction, IShipCombatTurretTarget {
        // public for tests
        public Vector2 Velocity;
        public int ContentsSeed;
        private bool hasMissedLanding;
        private bool isPathCalculated;
        private float lastRotation = 0f;
        private Vector2 hitPosition;
        public bool IsLeaving;
        public bool IsAttached;
        private int podTileId;
        private Tile podTile;
        private Vector2? origin;
        public float TurretEvadePercent => 0.5f;
        public float PointDefenseEvadePercent => 0.15f;
        public float RaidStrengthMul;
        /// <summary>Pre-distributed raid point share from the spawning story event.
        /// When > 0, CombatRaid spends exactly this many points instead of a full plan.</summary>
        public float RaidPoints;
        /// <summary>Gear tier of the parent plan this share was cut from (-1 = none set).
        /// Passed through so this capsule's own, smaller point share doesn't get recomputed
        /// to a lower tier than the parent raid actually earned.</summary>
        public int RaidGearTier = -1;
        /// <summary>Raiders generated up front by the story event and carried hidden until the
        /// capsule breaches (or is shot down) - see RaidPayload. Handed to the landed
        /// BreachCapsuleComp in SetFlyingCapsule. Takes precedence over RaidPoints, which
        /// remains only for capsules already in flight in older saves.</summary>
        public int[] RaiderIds;
        private ShipWeaponTargetTypeDef targetType;
        public ShipWeaponTargetTypeDef TargetType => targetType
                ??= ShipWeaponTargetTypeDef.FastGet(ShipWeaponTargetTypeIdH.BreachCapsule);
        public FactionDef Faction { get; set; }
        private Rng _rng;
        private Rng rng {
            get {
                if (_rng == null) {
                    _rng = new Rng(ContentsSeed);
                }
                return _rng;
            }
        }
        public bool IsMidFlight => !IsAttached;
        public bool CanChangeTrajectory => !IsAttached && !IsLeaving && !hasMissedLanding;
        public Tile PodTile => podTileId == 0 ? null : S.FindEntity<Tile>(podTileId);
        public float TrajectoryChangeEnergyCost => 100;
        public int ThreatType => ThreatIdH.BreachCapsule;
        public bool IsThreatActive => !IsAttached && !IsLeaving
            && !isUnderneathTheShip && isPathCalculated;

        private bool isUnderneathTheShip;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BreachCapsuleFlyingComp());
        }
        public bool WasAligned { get; set; }

        public bool AffectsSoundtrack => true;

        public Vector2 Position => Obj.Position;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("ContentsSeed", ContentsSeed);
            data.SetInt("PodTileId", podTileId);
            data.SetFloat("RaidStrengthMul", RaidStrengthMul);
            data.SetFloat("RaidPoints", RaidPoints);
            data.SetInt("RaidGearTier", RaidGearTier);
            data.SetIntSet("RaiderIds", RaiderIds);
            data.SetBool("WasAligned", WasAligned);
            data.SetBool("TakingOff", IsLeaving);
            data.SetBool("IsAttached", IsAttached);
            data.SetBool("HasMissedLanding", hasMissedLanding);
            data.SetBool("IsPathCalculated", isPathCalculated);
            data.SetString("FactionId", Faction?.Id);
            data.SetVector2("Velocity", Velocity);
            data.SetFloat("LastRotation", lastRotation);
            // TODO
            data.SetVector2("HitPosition", hitPosition);
        }

        protected override void OnLoad(ComponentData data) {
            hasMissedLanding = data.GetBool("HasMissedLanding", false);
            podTileId = data.GetInt("PodTileId", 0);
            RaidStrengthMul = data.GetFloat("RaidStrengthMul", 0f);
            RaidPoints = data.GetFloat("RaidPoints", 0f);
            RaidGearTier = data.GetInt("RaidGearTier", -1);
            RaiderIds = data.GetIntSet("RaiderIds", null);
            lastRotation = data.GetFloat("LastRotation", 0);
            WasAligned = data.GetBool("WasAligned", false);
            IsLeaving = data.GetBool("TakingOff", false);
            IsAttached = data.GetBool("IsAttached", false);
            ContentsSeed = data.GetInt("ContentsSeed", 1);
            isPathCalculated = data.GetBool("IsPathCalculated", false);
            hitPosition = data.GetVector2("HitPosition");
            Velocity = data.GetVector2("Velocity", new Vector2(-1, -1));
            var facId = data.GetString("FactionId", null);
            if (facId != null) {
                Faction = FactionDef.Get(facId);
            }
        }

        public override string ToString() {
            return " * BreachCapsuleFlying";
        }

        private bool isAddedToSec;

        public override void Receive(IComponent sender, int message) {
            if (!IsAttached && message == MsgIdH.HealthWentToZero) {
                var cap = The.Defs.FastGet(DefIdsH.ObjectsDevicesBreachCapsule);
                // ref should do too?
                // answer - no, ref would fuck it up, original will be modified
                var rawMaterials = cap.ConstructedMats;
                //D.Log("Will spawn: {0}", D.Dump(rawMaterials));
                if (!Arrays.IsEmpty(rawMaterials)) {
                    foreach (var mat in rawMaterials) {
                        EntityUtils.SpawnRawMaterial(mat.Type, mat.MaxStackSize,
                            Entity.PosIdx, 1f, true);
                    }
                }
                StartSurvivorRaid();
            }
        }

        private void StartSurvivorRaid() {
            var raid = new CombatRaid(S, ContentsSeed);
            raid.PosIdx = Obj.PosIdx;
            raid.Faction = Faction;
            if (S.Difficulty.Level > 1f) {
                raid.MinRaiders = 2;
            } else {
                raid.MinRaiders = 1;
            }
            // No difficulty bump: WantedTensionMultiplier already covers it (see BreachCapsuleComp).
            raid.MaxRaiders = Mathf.Clamp(S.Situation.Friendlies / 2, raid.MinRaiders, 8);
            if (RaidPoints > 0f) {
                raid.PresetPoints = RaidPoints;
                raid.PresetGearTier = RaidGearTier;
            }
            List<BeingData> units = new();
            if (!Arrays.IsEmpty(RaiderIds)) {
                // Carried raiders spill out into space, then get culled below like any others.
                var ids = RaiderIds;
                RaiderIds = null;
                RaidPayload.Release(S, ids, Obj.PosIdx, Faction, units);
            } else {
                var army = raid.PrepareArmy();
                foreach (var u in army.Units) {
                    units.Add(u.Data);
                }
                S.Query.StartRaid.Ask(army);
            }
            var numUnits = units.Count;
            var forceOneSurvivor = numUnits > 1;
            for (int i = numUnits - 1; i >= 0; i--) {
                MaybeCullRaider(units, ref forceOneSurvivor, i);
            }
        }

        private void MaybeCullRaider(List<BeingData> units, ref bool forceOneSurvivor, int i) {
            var unit = Arrays.SafeGetFast(units, i);
            if (unit == null) { return; }
            var grounded = unit.Being.AIAgent.Motion.IsGrounded;
            var hasInventory = unit.Inventory != null;
            if (grounded && hasInventory && unit.Inventory.Equipment.TryGetValue(
                    InventorySlotIdH.Suit, out var suit)) {
                // jet suit
                if (suit?.Item.Def.IdH == DefIdsH.ResearchProductionSpaceSuitAdvanced) {
                    // need to apply stats before checking the ability
                    D.Err("Survivor has jet suit, but can't fly!: {0}", unit);
                    grounded = false;
                }
            }
            if (grounded) {
                BeingUtils.Kill(S, unit, DamageType.Explosive,
                    Obj, Texts.LinkTo(T.Explosion, Obj.PosIdx), null, 0);
                return;
            }

            var wndSrc = NamedEntity.For(T.Explosion);
            if (forceOneSurvivor || Rng.UChance(0.25f)) {
                // survivor
                BeingUtils.DealHistoricalDamage(S, unit,
                    DamageType.Explosive, 1, Rng.URange(1, 4), wndSrc,
                        Severity.Mild, Severity.Moderate, Severity.Severe);
                if (unit.Body.IsAlive && !unit.Body.IsIncapacitated) {
                    forceOneSurvivor = false;
                }
            } else {
                BeingUtils.DealHistoricalDamage(S, unit,
                    DamageType.Explosive, 1, Rng.URange(2, 5), wndSrc,
                        Severity.Moderate, Severity.Severe,
                        Severity.Critical, Severity.Fatal);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            Obj.Transform.SkipMoveLoose = true;
            // Obj.IsFlying = true;
            if (ContentsSeed == 0) {
                ContentsSeed = Rng.URange(1, int.MaxValue);
            }

            if (IsAttached && entity.TryGetComponent<ParticleTrailComp>(out var trailComp)) {
                trailComp.SetHidden();
            }

            if (isPathCalculated) {
                Obj.Graphics.SetRotation(lastRotation);
            } else {
                // Since we have just created the object, there will be darkness
                Obj.Graphics.Show(false);
            }
        }

        private void AddThreat() {
            if (!isAddedToSec && IsThreatActive) {
                isAddedToSec = true;
                S.Sig.RegisterThreat.Send(this, true);
            }
        }
        private void RemoveThreat() {
            if (isAddedToSec) {
                isAddedToSec = false;
                S.Sig.RegisterThreat.Send(this, false);
            }
        }

        public void Detach(Tile podTile) {
            if (podTile != null) {
                this.podTile = podTile;
            }
            Velocity = new Vector2(rng.Range(-0.03f, 0.03f), -0.2f) * EntityUtils.MapSizeRatio;
            IsAttached = false;
            IsLeaving = true;
            isPathCalculated = false;
        }

        private void CalculatePath() {
            // 1. Transform.Position is now at the point of impact
            // we want to move the obj to the edge of the map
            // and then calculate velocity vector, and speed, so the asteroid
            // would land in target spot

            // figure out on which edge we will spawn the roid
            hitPosition = Obj.Transform.Position;

            Vector2 start;
            if (origin.HasValue) {
                start = EntityUtils.ToSafe(origin.Value);
            } else {
                start = EntityUtils.RandomMapEdgePoint(rng, 1f);
            }

            // Here we have both start and end positions, we can not get
            // a normalized velocity vector
            var velVecNorm = (hitPosition - start).normalized;

            var initVelocity = S.Difficulty.BreachCapsuleSpeed * Tunables.BreachCapsuleSpeedMul;
            if (rng.Chance(0.33f)) {
                // turbo breach capsule
                initVelocity *= rng.Range(1.1f, 1.4f);
                // D.Warn("Turbo breach capsule! V: {0}", initVelocity);
            } else {
                initVelocity *= rng.Range(0.8f, 1.1f);
            }
            initVelocity = Maths.Clamp(initVelocity, 0.9f, 10f);
            // D.Warn("Init velocity: {0} | {1}", initVelocity, Tunables.BreachCapsuleSpeedMul);
            Velocity = velVecNorm * initVelocity * EntityUtils.MapSizeRatio;
            isPathCalculated = true;

            Obj.Transform.TeleportTo(start);
            Obj.Graphics.TeleportTo(start);
            Obj.Graphics.Show(true);

            AddThreat();
        }

        private NotificationHandle incomingEvent;

        private void NotifyIncoming() {
            if (incomingEvent.IsValid) { return; }
            AddThreat();
            incomingEvent = Notification.BuildFor(Obj, null, T.BreachCapsuleIncoming,
                0, NotificationType.Alert)
                .Group("bcap_in")
                .Emergency()
                .WithVoice(VoiceLine.BreachCapsule)
                .Show();
            var title = Texts.LinkToObj(Obj, Texts.Red(T.BreachCapsuleIncoming));
            S.Sig.ShowFanfare.Send(title);
            S.Sys.Log.AddLine(title);
        }

        private int lastPos;
        private float minSqrDist = float.MaxValue;
        public override void OnRemove() {
            if (!Arrays.IsEmpty(RaiderIds)) {
                // Never landed and never shot open: the raiders inside are lost. Intended.
                RaidPayload.Discard(S, RaiderIds);
                RaiderIds = null;
            }
            RemoveThreat();
            RemoveNotification();
        }

        private void RemoveNotification() {
            Notification.Remove(ref incomingEvent);
        }

        private bool hasTickedProgress;

        public void TickProgress(float tickProgress) {
            if (IsAttached) { return; }
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
                if (IsLeaving) {
                    isPathCalculated = true;
                    if (podTile?.Id > 0) {
                        S.CmdQ.Enqueue(new CmdRemoveTile(podTile));
                        podTile = null;
                    }
                } else {
                    CalculatePath();
                }
            }
            var curPos = Obj.Transform.Position;
            var newPos = curPos + Velocity * tpf;
            if (!IsLeaving && !hasMissedLanding &&
                    Vectors.SqrDistance(curPos, newPos) >
                        Vectors.SqrDistance(curPos, hitPosition)) {
                // We are overshooting!
                newPos = hitPosition;
            }

            if (rotationSpeed == 0) {
                rotationSpeed = rng.Range(-1f, 1f);
            }
            lastRotation = (lastRotation + rotationSpeed * tpf) % 360;
            Obj.Graphics.SetRotation(lastRotation);

            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => {
                    if (Obj.IsActive) {
                        S.Objs.Destroy(Obj, true);
                    }
                });
            } else {
                if (!IsLeaving) {
                    NotifyIncoming();
                }
                Obj.Transform.TeleportTo(newPos);
                var newPosIdx = Obj.Transform.PosIdx;
                Obj.Graphics.TeleportTo(newPos);

                if (!IsLeaving && !hasMissedLanding) {
                    lastPos = newPosIdx;
                    // Check if we should hit
                    var sqrDist = Vectors.SqrDistance(hitPosition, newPos);
                    var floor = EntityUtils.FloorAt(newPosIdx);
                    isUnderneathTheShip = floor != null;
                    if (isUnderneathTheShip && isAddedToSec) {
                        RemoveThreat();
                    }

                    // When we overshoot, it will immediately attach
                    if (sqrDist >= 0.5f && sqrDist < minSqrDist) {
                        minSqrDist = sqrDist;
                        return;
                    }
                    // Tractor beam could pull the target in a way where last sq dist would be smaller than new sq dist
                    // So let's only assume we have overshot if distance is < 3
                    if (sqrDist > 9f) {
                        return;
                    }
                    // Attach logic
                    if (floor?.IsConstructed == true) {
                        IsAttached = true;
                        TurnIntoTile();
                    } else {
                        hasMissedLanding = true;
                        D.ErrSoft("Breach capsule missed landing at pos {0}!",
                            Pos.String(newPosIdx));
                        #if !PRODUCTION_BUILD
                        EntityUtils.CameraFocusOn(newPosIdx);
                        S.Clock.SetSpeed(ClockSpeed.Stopped);
                        #endif
                    }
                    RemoveNotification();
                }
            }
        }

        private void TurnIntoTile() {
            RemoveThreat();
            var podTile = EntityUtils.CreatePrototype<Tile>(DefIds.ObjectsDevicesBreachCapsule);
            var pos = EntityUtils.ToPos(hitPosition);
            var cmd = new CmdPlaceTile(pos, podTile, warn: false, true);
            var wasSelected = S.Query.IsSelected.Ask(Obj);
            cmd.Execute(S);
            if (!cmd.IsSuccess) {
                bool hadImmutableObj = false;
                int multiCount = podTile.Transform.GetSubtilePos(pos, Caches.IntArray32);
                for (int mpI = 0; mpI < multiCount; mpI++) {
                    var p = Caches.IntArray32[mpI];
                    var obj = EntityUtils.ParentObjAt(p) ?? EntityUtils.WallAt(p);
                    if (obj?.IsActive == true && obj.IsImmutable) {
                        hadImmutableObj = true;
                        D.Log("Breach Capsule cannot be created, there is immutable obstacle: {0}", obj);
                    }
                }
                if (hadImmutableObj) {
                    // We cannot land, something immutable is in our location
                    IsLeaving = true;
                    IsAttached = false;
                    return;
                }
                // One shared explosion VFX for the whole footprint (spawned after the
                // loop) instead of one per obstacle tile, to avoid a single-frame burst
                // of N particle systems + N camera shakes during landing.
                bool hadObstacle = false;
                int firesStarted = 0;
                //int multiCount = podTile.Transform.GetSubtilePos(pos, Caches.PosIdx);
                for (int mpI = 0; mpI < multiCount; mpI++) {
                    var p = Caches.IntArray32[mpI];
                    D.Warn("Breach capsule is checking obj at {0}", Pos.String(p));
                    if (Pos.IsOOB(p)) {
                        D.Err("Subtile pos is OOB: {0}", Pos.String(p));
                        continue;
                    }
                    var obj = EntityUtils.ParentObjAt(p) ?? EntityUtils.WallAt(p);
                    if (obj?.IsActive == true) {
                        hadObstacle = true;
                        // Reduced fire chance (was 0.25) and capped at 2 per landing
                        // to limit the number of ongoing fire-particle systems spawned.
                        if (firesStarted < 2 && rng.Chance(0.1f)) {
                            S.CmdQ.Enqueue(new CmdStartFire(p));
                            firesStarted++;
                        }
                        // D.Warn("Breach capsule is removing obj at {0}", obj);
                        obj.Constructable.BreakDown(2f, true, immediate: true);
                        var floor = EntityUtils.FloorAt(p);
                        if (floor != null) {
                            floor.ClearGoal("BreachCapsule");
                        }
                    }
                    if (S.Objs.ByPos.TryGetValue(p, out var objs)) {
                        var count = objs.Count;
                        for (int i = count - 1; i >= 0; i--) {
                            Obj o = Arrays.SafeGet(objs, i);
                            if (o == null || o == Obj || !o.IsActive) { continue; }
                            S.Objs.Destroy(o, false);
                        }
                    }
                }
                // Single explosion FX for the whole footprint; shakeCamera:false because
                // the shake below (line 455) already covers the landing impact.
                if (hadObstacle) {
                    Explosions.CreateExplosionFX(S, Obj.Position, 5f, 500f, false);
                }
                cmd = new CmdPlaceTile(pos, podTile, warn: false, true, true);
                cmd.Execute(S);
                S.Sig.ShakeCamera.Send(0.0f, 0.5f);
            }
            if (!cmd.IsSuccess) {
                D.Err("Couldn't create a landed breach capsule! {0}", Entity);
                IsLeaving = true;
                IsAttached = false;
                return;
            } else {
                if (podTile.TryGetComponent<BreachCapsuleComp>(out var podComp)) {
                    podComp.SetFlyingCapsule(this);
                }
                podTileId = podTile.Id;
                IsAttached = true;
                lastRotation = 0;
                var attachPos = podTile.Position + new Vector2(0, -0.9f);
                Obj.Transform.TeleportTo(attachPos);
                Obj.Graphics.TeleportTo(attachPos);
                Obj.Graphics.SetRotation(0);
                The.SysSig.GamepadRumble.Send(0.3f, 0.3f, 0.2f);
                if (entity.TryGetComponent<ParticleTrailComp>(out var trailComp)) {
                    trailComp.SetHidden();
                }
                S.Clock.ExecuteAfterTicks(1, () => {
                    // extra validation if there is any garbage left
                    int cnt = podTile.Transform.GetSubtilePos(podTile.PosIdx, Caches.PosIdx9);
                    for (int i = 0; i < cnt; i++) {
                        var p = Caches.PosIdx9[i];
                        var obj = EntityUtils.ObjAt(p);
                        var parent = EntityUtils.ParentObj(obj);
                        if (parent != podTile && parent != null) {
                            D.Err("Some shit in our position: {0}", parent);
                            parent.Constructable?.BreakDown(2f, true, immediate: true);
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
                    EntityUtils.TriggerChange(podTile);
                    if (wasSelected) {
                        S.Sig.SelectEntityUnfocused.Send(podTile);
                    }
                });
            }
        }

        private float rotationSpeed;

        public void OnShipMove(Vector2 dir) {
        }

        public bool InfluenceTrajectory(Vector2 target, float strength) {
            if (!CanChangeTrajectory) { return false; }
            var prevHitPosition = hitPosition;
            // 1. Set new hit position
            hitPosition = Vector2.MoveTowards(hitPosition, target, strength);
            //D.Log("Moved position: {0} -> {1}", prevHitPosition, hitPosition);
            if (hitPosition != prevHitPosition) {
                // 2. Make velocity vector point to new hit position
                var mag = Velocity.magnitude;
                Velocity = (hitPosition - Obj.Graphics.Position).normalized * mag;
                return true;
            }
            // we are already on this trajectory
            return false;
        }

        private Vector2 stealthOffset;
        private bool hasStealthOffset;

        public bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity) {
            if (IsAttached) {
                velocity = Consts.V2Zero;
                hitPoint = Obj.Graphics.Position;
                return false;
            }
            if (!IsLeaving) {
                if (!hasStealthOffset) {
                    hasStealthOffset = true;
                    stealthOffset = rng.InsideUnitCircle().normalized
                        * rng.Range(0f, 4f);
                }
                velocity = this.Velocity;
                var p = Obj.Graphics.Position;
                var dist = Vectors.Distance(hitPosition, p);
                var t = GameTimer.time;
                var distSqrt = Mathf.Sqrt(dist);
                var stealthPos = (new Vector2(
                    Mathf.Sin(t + p.x),
                    Mathf.Cos((t * 0.7f) + p.y)) + stealthOffset) * distSqrt;
                hitPoint = hitPosition + stealthPos;
                return true;
            } else {
                velocity = this.Velocity;
                hitPoint = Consts.V2Zero;
                return false;
            }
        }

        public Vector2 PredictPosition(float afterSeconds) {
            var newPos = Position + Velocity * afterSeconds;
            return newPos;
        }
        public void SetFaction(FactionDef faction) {
            Faction = faction;
        }

        public void SetOrigin(Vector2 position) {
            origin = position;
        }
    }
}
