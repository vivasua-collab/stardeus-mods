using System;
using Game.Achievements;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI.Combat;
using Game.Systems.TileAnim;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components {
    public sealed class BreachCapsuleComp : BaseComponent<BreachCapsuleComp>,
            ITickListener, IWithFaction {
        private bool isOpened;
        public bool IsOpened => isOpened;
        // Raiders spawn RaidDelayTicks after the capsule opens, so IsOpened alone
        // does not mean the raid exists yet
        public bool IsRaidStarted => raidStarted;
        public bool IsEscapeOptionAvailable => isOpened;
        public int GatewayPos => Tile.Transform.OutPort;
        public FactionDef Faction { get; set; }
        public int FlyingCapsuleId;
        public int ContentsSeed;
        public float RaidStrengthMul;
        /// <summary>Pre-distributed raid point share from the spawning story event.
        /// When > 0, CombatRaid spends exactly this many points instead of a full plan.</summary>
        public float RaidPoints;
        /// <summary>Gear tier of the parent plan RaidPoints was cut from (-1 = none set), so a
        /// small share isn't recomputed to a lower tier than the parent raid earned.</summary>
        public int RaidGearTier = -1;
        /// <summary>Raiders generated up front by the story event, carried hidden by the flying
        /// capsule and handed over on landing (see RaidPayload). Takes precedence over
        /// RaidPoints, which remains only for capsules already landed in older saves.</summary>
        public int[] RaiderIds;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BreachCapsuleComp());
        }
        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsOpened", isOpened);
            data.SetInt("FlyingCapsuleId", FlyingCapsuleId);
            data.SetFloat("RaidStrengthMul", RaidStrengthMul);
            data.SetFloat("RaidPoints", RaidPoints);
            data.SetInt("RaidGearTier", RaidGearTier);
            data.SetIntSet("RaiderIds", RaiderIds);
            data.SetInt("ContentsSeed", ContentsSeed);
            data.SetString("FactionId", Faction?.Id);
            data.SetBool("RaidStarted", raidStarted);
            data.SetInt("RaidDelayLeft", raidDelayLeft);
        }

        protected override void OnLoad(ComponentData data) {
            isOpened = data.GetBool("IsOpened", false);
            FlyingCapsuleId = data.GetInt("FlyingCapsuleId", 0);
            RaidStrengthMul = data.GetFloat("RaidStrengthMul", 0f);
            RaidPoints = data.GetFloat("RaidPoints", 0f);
            RaidGearTier = data.GetInt("RaidGearTier", -1);
            RaiderIds = data.GetIntSet("RaiderIds", null);
            ContentsSeed = data.GetInt("ContentsSeed", 1);
            var facId = data.GetString("FactionId", null);
            if (facId != null) {
                Faction = FactionDef.Get(facId);
            }
            raidStarted = data.GetBool("RaidStarted", false);
            raidDelayLeft = data.GetInt("RaidDelayLeft", -1);
            // Backward compat: old saves didn't persist raidStarted / raidDelayLeft.
            // If the capsule is already open the raid has already been handled — set
            // raidStarted so Tick() does not try to spawn a second raid.
            if (isOpened && !raidStarted && raidDelayLeft == -1) {
                raidStarted = true;
            }
        }

        public override string ToString() {
            return " * BreachCapsule";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (ContentsSeed == 0) {
                ContentsSeed = S.Rng.Range(1, int.MaxValue);
            }
            if (!isOpened) {
                Tile.SetImmutableWithoutFloor(this, true);
                // drill animation will go brrr
                // Tile.Graphics.SetGraphicId(GraphicsId.NoAtlasTransparent);
                if (Tile.TryGetComponent<TileAnimationComp>(out var anim)) {
                    anim.RegisterOnNextUpdate();
                }
            }
            if (wasLoaded && isOpened) {
                S.Clock.OnNextUpdate(() => {
                    CheckRemovability();
                });
            }
        }

        private void CheckRemovability() {
            Tile.SetImmutableWithoutFloor(this, !isOpened);
        }

        public override void OnRemove() {
            if (!raidStarted && !Arrays.IsEmpty(RaiderIds)) {
                // Removed before it breached: the raiders inside are lost. Intended.
                RaidPayload.Discard(S, RaiderIds);
                RaiderIds = null;
            }
            if (FlyingCapsuleId != 0) {
                var podFlying = S.Objs.Find(FlyingCapsuleId);
                if (podFlying != null && podFlying
                        .TryGetComponent<BreachCapsuleFlyingComp>(out var flying)) {
                    if (flying.IsAttached && !flying.IsLeaving) {
                        S.Objs.Destroy(podFlying);
                    }
                }
            }
            if (Tile?.Transform != null) {
                var positions = Tile.Transform.GetSubtilePos(Caches.IntArray128);
                for (int i = 0; i < positions; i++) {
                    var p = Caches.IntArray128[i];
                    var floor = EntityUtils.FloorAt(p);
                    if (floor != null && floor.Health is DamageableComp dmg && dmg.IsDamaged) {
                        dmg.CreateRepairGoal();
                    }
                }
            }
        }

        public void SetFlyingCapsule(BreachCapsuleFlyingComp cap) {
            FlyingCapsuleId = cap.EntityId;
            ContentsSeed = cap.ContentsSeed;
            Faction = cap.Faction;
            RaidStrengthMul = cap.RaidStrengthMul;
            RaidPoints = cap.RaidPoints;
            RaidGearTier = cap.RaidGearTier;
            // The landed pod now owns the payload; clear it on the flying capsule so its own
            // OnRemove (when the pod is torn down later) doesn't discard raiders already released.
            RaiderIds = cap.RaiderIds;
            cap.RaiderIds = null;
        }

        public bool Drill() {
            if (isOpened) { return false; }
            var maxHealth = 0f;
            var health = 0f;
            bool hasAnyFloor = false;
            var cnt = Tile.Transform.GetSubtilePos(Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                var p = Caches.PosIdx[i];
                var floor = EntityUtils.FloorAt(p);
                if (floor != null) {
                    floor.ClearGoal(T.ObjectsDevicesBreachcapsule);
                    hasAnyFloor = true;
                    var d = floor.Health;
                    maxHealth += d.MaxHealth;
                    if (floor.IsConstructed && floor.Health.HealthPercent > 0.5f) {
                        var dmg = S.Rng.Range(0f, 1f) * S.Difficulty.Reduction * DrillIntervalTicks;
                        floor.Health.TakeDamage(dmg, DamageType.Piercing, tryAutoRepair: false);
                    }
                    health += d.Health;
                }
            }
            if (!hasAnyFloor) { return true; }
            return health < maxHealth * 0.55f;
        }

        public void Open() {
            isOpened = true;
            Tile.Graphics.SetGraphicId(GraphicsId.ObjectsDevicesBreachCapsule);
            if (Tile.TryGetComponent<TileAnimationComp>(out var anim)) {
                anim.RegisterAnim(false);
            }
            The.AchievementTracker.Unlock(AchievementBreached.AchievementId);
            CheckRemovability();
        }

        private Obj FindMissingFlyingCapsule() {
            var podFlyingComp = S.Components.FindFirstMatching<BreachCapsuleFlyingComp>(
                (p) => Vectors.SqrDistance(Entity.Position, p.Obj.Position) < 4);
            var podFlying = podFlyingComp?.Obj;
            FlyingCapsuleId = podFlying?.Id ?? 0;
            return podFlying;
        }

        public void Detach() {
            //D.Log("Detaching");
            var podFlying = S.Objs.Find(FlyingCapsuleId)
                    ?? FindMissingFlyingCapsule();
            if (podFlying != null && podFlying
                    .TryGetComponent<BreachCapsuleFlyingComp>(out var flying)) {
                flying.Detach(Tile);
            } else {
                D.Err("Trying to detach breach capsule flying that does not exist!");
            }
            Tile.MaterialStorage.DestroyContents();
        }

        // Drill every N ticks with N× damage to keep overall drilling speed the same
        // while reducing the per-frame graphics-rebuild frequency at high clock speeds.
        private const int DrillIntervalTicks = 15;
        // Short pause between the floor bursting open and the raiders pouring through,
        // giving the game time to drain queued destruction commands first.
        private const int RaidDelayTicks = 10;
        private bool raidStarted;
        // -1 = not yet breached; >= 0 = ticks remaining before raid spawns
        private int raidDelayLeft = -1;

        private void StartRaid() {
            if (raidStarted) {
                D.Err("Starting raid more than once for breach capsule: {0}", this);
                return;
            }
            // Open() was already called from Tick() when drilling completed.
            raidStarted = true;
            D.Log("Breach content seed: {0}", ContentsSeed);
            if (!Arrays.IsEmpty(RaiderIds)) {
                var ids = RaiderIds;
                RaiderIds = null;
                if (RaidPayload.Release(S, ids, GatewayPos, Faction) == null) {
                    var payloadRng = new Rng(ContentsSeed);
                    int payloadCount = payloadRng.Range(2, Mathf.CeilToInt(3 * S.Difficulty.Level));
                    EntityUtils.SpawnExplosive(Faction, GatewayPos, payloadRng, payloadCount);
                }
                return;
            }
            var raid = new CombatRaid(S, ContentsSeed);
            raid.PosIdx = GatewayPos;
            raid.Faction = Faction;
            if (S.Difficulty.Level > 1f) {
                raid.MinRaiders = 2;
            } else {
                raid.MinRaiders = 1;
            }
            // No difficulty bump here: Difficulty.WantedTensionMultiplier is already in the
            // plan's multiplier chain, and the old 0.75/1.25 on top counted difficulty twice.
            if (RaidPoints > 0f) {
                raid.PresetPoints = RaidPoints;
                raid.PresetGearTier = RaidGearTier;
            } else {
                raid.AdjustStrength(RaidStrengthMul);
            }
            if (!raid.TryStartRaid()) {
                var rng = new Rng(ContentsSeed);
                int count = rng.Range(2, Mathf.CeilToInt(3 * S.Difficulty.Level));
                EntityUtils.SpawnExplosive(Faction, GatewayPos, rng, count);
            }
        }

        public void Tick(long currentTick) {
            if (raidStarted) { return; }
            if (isOpened) {
                // Capsule is open: count down before spawning raiders so the game has a
                // tick or two to drain floor-destruction commands from the queue first.
                if (raidDelayLeft > 0) {
                    raidDelayLeft--;
                } else if (raidDelayLeft == 0) {
                    StartRaid();
                }
                return;
            }
            // Throttle drilling: damage tiles once every DrillIntervalTicks ticks with
            // proportionally larger damage.  The entity-id phase offset spreads multiple
            // simultaneous capsules across different ticks, matching the FluidCannonComp
            // pattern used elsewhere for the same reason.
            if (currentTick % DrillIntervalTicks != Tile.Id % DrillIntervalTicks) {
                return;
            }
            if (Drill()) {
                S.Sig.ShakeCamera.Send(0.0f, 0.5f);
                Open();
                raidDelayLeft = RaidDelayTicks;
            }
        }

    }
}
