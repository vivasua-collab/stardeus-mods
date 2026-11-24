using Game.Constants;
using Game.Data;
using Game.Systems.Projectiles;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class FluidCannonComp : BaseComponent<FluidCannonComp>,
            IRangedShowOthers, IRangedAreaOutline, ITurret, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new FluidCannonComp());
        }
        private const float DefaultEnergyCost = 5f;
        private float shotCost;
        private float range;
        private float rangeSqr;
        public float Range => range;
        public float RangeOthersMaxSqrDist => rangeSqr;
        public Color RadiusColor => Consts.ColorTurretRange;
        public int RangeGroupId => JobTypeIdH.Firefighting;
        public bool RadiusSameArea => true;
        public bool RadiusInSpace => false;
        private long lastShotTick;
        private int ticksBetweenShots;
        private int shootTick;
        private int windupTicks;
        // Windup is per fire incident: FirexSys.FireCycle advances exactly once when
        // every fire has died out, so a new incident re-arms the cannon without any
        // extra tracking of which fire is which
        private int windupCycle = -1;
        private long windupUntilTick;
        private float accuracySlack;
        private RefillableStorageComp ammo;
        public RefillableStorageComp Ammo => ammo;
        public int ShotsFired;
        private bool hasDeficit;
        public bool IsTurretUsable {
            get {
                var tt = Tile;
                return EntityUtils.IsDeviceUsable(tt) &&
                        tt.ENode.IsPowerable &&
                        tt.Health.IsOperational;
            }
        }

        private float energyCost;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        protected override void OnConfig() {
            shootTick = Rng.URange(0, 5);
            shotCost = Config.GetFloat(PropertyIdH.ShotCost, 0.5f);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            // Degrees
            accuracySlack = Config.GetFloat(PropertyIdH.AccuracySlack, 10f) * 0.5f;
            ticksBetweenShots = Config.GetInt(PropertyIdH.MinutesBetweenShots, 5)
                * Consts.TicksPerMinute;
            windupTicks = Config.GetInt(PropertyIdH.WindupMinutes, 10)
                * Consts.TicksPerMinute;
            range = Config.GetInt(PropertyIdH.Range, 15);
            rangeSqr = range * range;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("ShotsTaken", ShotsFired);
            //data.SetFloat("WaterLevel", currentStock);
            data.SetInt("WindupCycle", windupCycle);
            data.SetLong("WindupUntilTick", windupUntilTick);
        }

        protected override void OnLoad(ComponentData data) {
            //currentStock = data.GetFloat("WaterLevel", 0);
            ShotsFired = data.GetInt("ShotsTaken", 0);
            windupCycle = data.GetInt("WindupCycle", -1);
            windupUntilTick = data.GetLong("WindupUntilTick", 0);
        }

        public override string ToString() {
            return " * WaterCannon";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (ammo != null) { return; }
            if (!Entity.TryGetComponent<RefillableStorageComp>(out ammo)) {
                var msg = "FluidCannon has no RefillableStorageComp on the entity!";
                D.Err(msg);
                WPopup.SystemWarning("GAME BREAKING BUG", msg);
            }
            S.Sys.Sec.AddTurret(this);
        }

        public override void OnRemove() {
            S.Sys.Sec.RemoveTurret(this);
        }

        // FIXME refactor along with TurretComp
        public bool TryShootAt(IThreat target, long tick) {
            //if (!target.Flammable.IsOnFire) { return false; }
            if (!CanShoot(tick)) { return false; }
            var pos = Tile.Transform.PosIdx;
            if (!CanHit(pos, target.Entity.PosIdx)) { return false; }
            // Line of sight. Hack - we will simply check if we are in the same room
            var a = S.Sys.Areas;
            var turretRoom = a.At(pos);
            var targetRoom = a.At(target.Entity.PosIdx);
            bool isTurretOutside = turretRoom < 0;
            bool isFireOutside = targetRoom < 1;
            // if both turret and fire is outside, we will try to shooot
            if (!isTurretOutside && !isFireOutside) {
                // Different room, but still try shooting at targets in space because
                // it could be a fire in a wall
                // if it's not a wall, we will not shoot at another room
                if (targetRoom != -1 && turretRoom != targetRoom) {
                    return false;
                }
            }
            // we don't have target in sight
            var handler = ProjectileHandler.Get(ammo.MatType.Id);
            if (handler == null) {
                ammo.Stop();
                return false;
            }
            if (!handler.HasLineofSight(Entity.Position, target.Entity, 1)) {
                return false;
            }
            if (target.ThreatType == ThreatIdH.Fire && !IsWoundUpForFire(tick)) {
                return false;
            }
            if (ammo.TryConsume(shotCost)) {
                lastShotTick = tick;
                ShotsFired++;

                var proj = handler.NewProjectileFor(
                    Tile.Position,
                    ApplySlack(target.Entity.Position),
                    Tile,
                    target.Entity);
                S.Sig.FireProjectile.Send(proj);
            }
            return true;
        }

        private Vector2 ApplySlack(Vector2 pos) {
            var slackDegrees = S.Rng.Range(-accuracySlack, accuracySlack);
            return Vectors.Rotate(pos, Entity.Position, slackDegrees);
        }

        // A fire that just started gets time to grow before the cannon engages it. The
        // clock starts the first time this cannon has an actual shot lined up on a
        // fire, and resets when the incident ends
        private bool IsWoundUpForFire(long tick) {
            var cycle = S.Sys.Firex.FireCycle;
            if (windupCycle != cycle) {
                windupCycle = cycle;
                windupUntilTick = tick + windupTicks;
            }
            return tick >= windupUntilTick;
        }

        private bool CanShoot(long tick) {
            if (tick % 5 != shootTick) { return false; }
            // do we have enough charge for one shot
            if (!ammo.CheckIfConfigured()) {
                return false;
            }
            if (ammo.CurrentStock < shotCost) { return false; }
            // we cannot shoot as often
            if (lastShotTick + ticksBetweenShots > tick) { return false; }
            return true;
        }
        private bool CanHit(int turretPosIdx, int targetPosIdx) {
            var sqDist = Pos.SqrDistance(turretPosIdx, targetPosIdx);
            return sqDist <= rangeSqr;
        }

        public bool CanHandle(IThreat threat) {
            if (hasDeficit) { return false; }
            if (!ammo.IsConfigured || ammo.IsEmpty) { return false; }
            if (threat.ThreatType == ThreatIdH.Being) { return true; }
            if (threat.ThreatType == ThreatIdH.Fire) {
                return ammo.MatType.Id == MatId.Water;
            }
            return false;
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elnode.AddAfterTick(this, AfterTickGrid);
                    elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                }
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(FluidCannonPage.PageId, 3f);
                }
            }
        }
        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            ammo.CreateOrUpdateRefillGoal();
        }
    }
}
