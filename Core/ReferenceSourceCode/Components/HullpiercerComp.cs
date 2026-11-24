using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
using Game.Systems.Turrets;
using Game.Systems.Ships;
using Game.Systems.AI.Combat;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.UI;

namespace Game.Components {
    public sealed class HullpiercerComp : BaseComponent<HullpiercerComp>,
            ITickProgressListener, IThreat, IWithFaction,
            IWithTrajectory, ITurretTarget, IShipCombatTurretTarget {
        public Vector2 Velocity;
        public bool CanChangeTrajectory => !hadImpact;
        // The cheapest projectile a tractor beam can pull, which the codex shows as its minimum
        public const float TrajectoryChangeCost = 25f;
        // A piercer is a small boarding pod: one that misses or is shot down must not take a
        // quarter of the raid with it, and a big raid must not land as one wave (STA-1674)
        public const int MaxRaidersPerPiercer = 2;
        public float TrajectoryChangeEnergyCost => TrajectoryChangeCost;
        private int lastPos;
        public Vector2 Position => Obj.Position;
        public FactionDef Faction { get; set; }
        public bool IsOpen;
        private int impactPos;
        private long openTick;
        private int wantedDepth;
        public bool IsMidFlight => !hadImpact;
        public bool IsThreatActive => !hadImpact;
        public bool AffectsSoundtrack => true;
        public float TurretEvadePercent => 0.6f;
        public float PointDefenseEvadePercent => 0.25f;
        public int Seed;
        /// <summary>Pre-distributed raid point share from the spawning story event.
        /// When > 0, CombatRaid spends exactly this many points instead of a full plan.</summary>
        public float RaidPoints;
        /// <summary>Gear tier of the parent plan this share was cut from (-1 = none set).
        /// Passed through so this piercer's own, smaller point share doesn't get recomputed
        /// to a lower tier than the parent raid actually earned.</summary>
        public int RaidGearTier = -1;
        /// <summary>Raiders generated up front by the story event and carried hidden until
        /// Open() (see RaidPayload). Takes precedence over RaidPoints, which remains only for
        /// piercers already in flight in older saves.</summary>
        public int[] RaiderIds;
        private ShipWeaponTargetTypeDef targetType;
        public ShipWeaponTargetTypeDef TargetType => targetType
                ??= ShipWeaponTargetTypeDef.FastGet(ShipWeaponTargetTypeIdH.BreachCapsule);

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HullpiercerComp());
        }
        private void UpdateGraphic() {
            if (IsOpen) {
                // To make things appear in the right place
                Obj.Graphics.OverrideGraphic($"{Obj.Graphics.GraphicId}_Open");
                Obj.Graphics.SetRotation(lastRotation);
            }
        }
        private void RemoveTrail() {
            Entity.GetComponent<ParticleTrailComp>().DisableParticles();
        }

        public void Open() {
            if (IsOpen) { return; }
            IsOpen = true;
            // Obj.Processable.IsProcessableNow = true;
            UpdateGraphic();

            Notification.Remove(ref incomingEvent);

            if (Seed == 0) {
                Seed = S.Rng.Int;
            }

            if (!Arrays.IsEmpty(RaiderIds)) {
                var ids = RaiderIds;
                RaiderIds = null;
                if (RaidPayload.Release(S, ids, Obj.PosIdx, Faction) == null) {
                    BeingUtils.GenerateDeadBeings(S, 1, S.Rng, Obj.PosIdx, 0.5f, 1.0f);
                }
                return;
            }

            var raid = new CombatRaid(S, Seed);
            raid.PosIdx = Obj.PosIdx;
            raid.Faction = Faction;
            raid.MinRaiders = 1;
            raid.MaxRaiders = MaxRaidersPerPiercer;
            if (RaidPoints > 0f) {
                raid.PresetPoints = RaidPoints;
                raid.PresetGearTier = RaidGearTier;
            }
            if (!raid.TryStartRaid()) {
                BeingUtils.GenerateDeadBeings(S, 1, S.Rng, Obj.PosIdx,
                    0.5f, 1.0f);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            Ready.When(WhenH.ObjsLoaded, () => {
                if (openTick != 0) {
                    RemoveTrail();
                    UpdateGraphic();
                }
                Obj.Graphics.SetRotation(lastRotation);
            });
        }

        protected override void OnConfig() {
            // for spawning them in dev mode
            Velocity = new Vector2(-1, 0);
        }

        public bool WasAligned { get; set; }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Faction", Faction?.IdH ?? 0);
            data.SetInt("ImpactPos", impactPos);
            data.SetLong("OpenTick", openTick);
            data.SetBool("WasAligned", WasAligned);
            data.SetBool("IsOpen", IsOpen);
            data.SetVector2("Velocity", Velocity);
            data.SetBool("HadImpact", hadImpact);
            data.SetFloat("RaidPoints", RaidPoints);
            data.SetInt("RaidGearTier", RaidGearTier);
            data.SetIntSet("RaiderIds", RaiderIds);
        }

        protected override void OnLoad(ComponentData data) {
            var facId = data.GetInt("Faction", FactionIdH.Pirate);
            Faction = FactionDef.FastGet(facId);
            impactPos = data.GetInt("ImpactPos", 0);
            openTick = data.GetLong("OpenTick", 0L);
            WasAligned = data.GetBool("WasAligned", false);
            IsOpen = data.GetBool("IsOpen", IsOpen);
            Velocity = data.GetVector2("Velocity", new Vector2(-1, 0));
            hadImpact = data.GetBool("HadImpact", hadImpact);
            RaidPoints = data.GetFloat("RaidPoints", 0f);
            RaidGearTier = data.GetInt("RaidGearTier", -1);
            RaiderIds = data.GetIntSet("RaiderIds", null);
        }

        public override string ToString() {
            return " * Hullpiercer";
        }

        public override void OnRemove() {
            if (!IsOpen && !Arrays.IsEmpty(RaiderIds)) {
                // Shot down or lost before opening: the raiders inside are gone. Intended.
                RaidPayload.Discard(S, RaiderIds);
                RaiderIds = null;
            }
            RemoveThreat();
        }

        private void RemoveThreat() {
            if (isAddedToSec) {
                isAddedToSec = false;
                S.Sig.OnShipRotate.RemoveListener(OnShipRotate);
                S.Sig.RegisterThreat.Send(this, false);
            }
        }

        private bool isAddedToSec;

        private void AddThreat() {
            if (!isAddedToSec && !IsOpen) {
                isAddedToSec = true;
                S.Sig.OnShipRotate.AddListener(OnShipRotate);
                S.Sig.RegisterThreat.Send(this, true);
            }
        }

        private float lastRotation;
        private void OnShipRotate(float rotation) {
            S.Sys.Ship.RotateSpaceObj(rotation, Velocity, Obj.Position, ref lastRotation);
        }

        public void Destroy() {
            if (!Obj.IsActive) {
                // Meteor can or cannot explode and destroy itself, so it's ok
                //D.Err("Trying to destroy inactive meteor! {0}", Obj);
                return;
            }
            S.CmdQ.Enqueue(new CmdRemoveObj(Obj));
        }

        private NotificationHandle incomingEvent;
        public int ThreatType => ThreatIdH.BreachCapsule;

        private void NotifyIncoming() {
            if (incomingEvent.IsValid) { return; }
            AddThreat();
            incomingEvent = Notification.BuildFor(Obj, null,
                    T.IncomingObject_V1.F(Obj.Definition.NameT),
                0, NotificationType.Alert)
                .Group("hpierc")
                .Emergency(true)
                .WithVoice(VoiceLine.Hullpiercer)
                .Show();
        }

        private bool hadImpact;

        public void ApplyImpact() {
            hadImpact = true;
            Explosions.CreateExplosionFX(S, Obj.Position, 2f, 100f, true);
            if (impactPos == 0) {
                impactPos = Obj.PosIdx;
                wantedDepth = S.Rng.Range(3, 6);
            }
        }

        private float rotationAngle;
        private bool hasTickedProgress;

        public void TickProgress(float tickProgress) {
            if (IsOpen) { return; }
            if (openTick != 0) {
                if (G.Ticks >= openTick) {
                    Open();
                }
                return;
            }
            if (!hasTickedProgress) {
                hasTickedProgress = true;
                return;
            }
            if (S.Universe.Player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                Destroy();
                return;
            }
            if (!Obj.IsActive) { return; }
            // S.Clock.PreventMultiTick();
            var tpf = S.Clock.TicksPerUpdate;
            var curPos = Obj.Transform.Position;
            var newPos = curPos + Velocity * tpf - shipShift;
            shipShift = Consts.V2Zero;
            if (!EntityUtils.IsWithinBounds(newPos)) {
                S.Clock.OnNextUpdate(() => {
                    if (Obj.IsActive) {
                        S.Objs.Destroy(Obj, true);
                    }
                });
            } else {
                NotifyIncoming();
                Obj.Transform.TeleportTo(newPos);
                var newPosIdx = Obj.Transform.PosIdx;
                Obj.Graphics.TeleportTo(newPos);
                rotationAngle += 20f * tpf;
                Obj.Graphics.SetRotation(rotationAngle % 360);
                if (lastPos != newPosIdx) {
                    lastPos = newPosIdx;
                    // Check if we should hit
                    if (EntityUtils.IsWallAt(newPosIdx)) {
                        var wall = EntityUtils.WallAt(newPosIdx);
                        // Have a chance to fly through structure if it's not near enclosed area
                        if (!wall.IsConstructed) { return; }
                        wall.Constructable.BreakDown(
                            0.5f, false, immediate: true, silent: true, skipDanglingCheck: false, autoRebuild: true);

                        RemoveThreat();
                        ApplyImpact();
                    }
                    if (impactPos != 0 && Pos.SqrDistance(impactPos, newPosIdx) >= wantedDepth * wantedDepth) {
                        if (EntityUtils.IsEmptyFloorAt(newPosIdx)) {
                            // stop
                            openTick = G.Ticks + Consts.TicksPer15Minutes;
                            Velocity = Consts.V2Zero;
                            RemoveTrail();
                            if (S.Query.IsSelected.Ask(Obj)) {
                                UIUtils.RefreshSelectedEntityUI();
                            }
                            return;
                        }
                    }
                }
            }
        }
        private Vector2 shipShift;
        public void OnShipMove(Vector2 dir) {
            shipShift = dir * Tunables.IncomingSpaceBodiesShipMoveMul;
        }

        public bool InfluenceTrajectory(Vector2 target, float strength) {
            var hitPosition = Obj.Transform.Position + Velocity;
            // 1. Set new hit position
            hitPosition = Vector2.MoveTowards(hitPosition, target, strength);
            var mag = Velocity.magnitude;
            Velocity = (hitPosition - Obj.Graphics.Position).normalized * mag;
            return true;
        }

        public bool TryGetHitPoint(out Vector2 hitPoint, out Vector2 velocity) {
            velocity = this.Velocity;
            hitPoint = Obj.Graphics.Position + (velocity * 25f);
            return true;
        }

        public static Obj Spawn(GameState S, Rng rng, Vector2 targetPos, Vector2 startPos) {
            startPos = EntityUtils.ToSafe(startPos);
            var cmd = new CmdSpawnObj(startPos,
                The.Defs.FastGet(DefIdsH.ObjContainersHullpiercer), true);
            cmd.Execute(S);
            if (cmd.Obj.TryGetComponent<HullpiercerComp>(out var hp)) {
                hp.Seed = rng.Int;
                hp.wantedDepth = rng.Range(3, 6);
                hp.Velocity = (targetPos - startPos).normalized
                    * rng.Range(5f, 7f) * EntityUtils.MapSizeRatio;
            }
            // D.Warn("Spawned hullpiercer at {0} with v: {1}", startPos, hp.Velocity);
            return cmd.Obj;
        }

        public Vector2 PredictPosition(float afterSeconds) {
            var newPos = Position + Velocity * afterSeconds * 2 - shipShift;
            // D.Warn($"Position: {Position}, Velocity: {Velocity}, AfterSeconds: {afterSeconds}, ShipShift: {shipShift}, NewPos: {newPos}");
            return newPos;
        }
        public void SetFaction(FactionDef faction) {
            Faction = faction;
        }
    }
}
