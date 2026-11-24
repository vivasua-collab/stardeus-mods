using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using Game.Rendering;
using KL.Utils;
using KL.Randomness;
using Game.Components;
using Game.Data.Factions;
using Game.Visuals;
using System;
using Game.Systems.Creatures;
using Game.Systems.AI.Combat;
using Game.Systems.Combat;
using KL.Collections;
using Game.Mods;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Systems.Turrets {
    public sealed class AntiPersonnelTurretComp : BaseComponent<AntiPersonnelTurretComp>,
            IRangedShowOthers, IRangedAreaOutline, IAttackMethodProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AntiPersonnelTurretComp());
        }
        private RotatingTurretComp turretComp;
        private RotatingTurret turret;
        private float range;
        public float RangeSqr { get; private set; }
        public int RangeGroupId => JobTypeIdH.Shooting;
        public float RangeOthersMaxSqrDist => RangeSqr;
        public float Range => range;
        public Color RadiusColor => Consts.ColorTurretRange;
        public bool RadiusSameArea => true;
        public string ItemName => Tile.Definition.NameT;
        public string SlotName => T.EquipmentSlotBody;
        public float Quality => Tile.Health.HealthPercent;
        public bool IsBodyPart => false;
        private AIAgentComp targetAgent;
        private int ticksBetweenShots;
        private float maxAccuracy;
        private int shotTickMod;
        private AttackMethod attack;

        public void SetTarget(ITurretTarget target) {
            if (turret.Target == target) { return; }
            // D.Warn("Setting turret target: {0}: {1} -> {2}",
            //     this, Target, target);
            if (targetAgent != null) {
                targetAgent.CombatUnit.TurretsTargetingThisUnit.Remove(turret.AttackableTurret);
            }
            if (target is AIAgentComp agent) {
                turret.Target = target;
                targetAgent = agent;
                agent.CombatUnit.TurretsTargetingThisUnit.Add(turret.AttackableTurret);
            } else {
                turret.Target = null;
                targetAgent = null;
            }
        }

        protected override void OnConfig() {
            // D.Warn("Shot sound: {0}", shotSound);
            ticksBetweenShots = Mathf.RoundToInt(Config.GetFloat(PropertyIdH.MinutesBetweenShots, 1)
                * Consts.TicksPerMinute);
            shotTickMod = Rng.URange(0, ticksBetweenShots);
            range = Config.GetFloat(PropertyIdH.Range, 10f);
            maxAccuracy = Config.GetFloat(PropertyIdH.MaxAccuracy);
            var dps = Config.GetFloat(PropertyIdH.DPS, 10f);
            RangeSqr = range * range;
            attack = AttackMethod.CreateFor(this, new AttackMethodData() {
                DPS = dps,
                AccuracyCurve = TurretsSys.TurretAccuracyCurve,
                RangeMin = 1f,
                RangeMax = range,
                AccuracyMax = maxAccuracy,
                AttackAction = AttackActionRanged.ActId,
                BleedChance = 0.5f,
                CooldownSec = ticksBetweenShots * 0.5f,
                ChargeSec = ticksBetweenShots * 0.5f,
                NameT = T.AttackMethodShoot,
                Type = DamageType.Piercing,
                Id = "Shoot",
                StunChanceBio = 0.3f,
                StunChanceRobo = 0.1f,
            });
        }

        public override void OnLateReady(bool wasLoaded) {
            if (!entity.TryGetComponent(out turretComp)) {
                D.Err("TurretAntiPersonnel requires RotatingTurret component! {0}",
                    Tile.Definition.Id);
                return;
            }

            turret = turretComp.Turret;
            turret.Type |= TurretType.AntiPersonnel;
            turret.TryShoot = TryShoot;
            turret.CheckTarget = CheckTarget;
            turret.SetTarget = SetTarget;
            turret.Range = range;
            turret.RangeSqr = RangeSqr;
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(MiniTurretPage.PageId, 3f);
                }
            }
        }

        public bool TryShoot(long ticks) {
            if (turret.Target == null) {
                return false;
            }
            if (ticks % ticksBetweenShots != shotTickMod) {
                // wait for your turn
                return false;
            }

            targetAgent.CombatUnit.NoticeAttack(turret.AttackableTurret);
            var hitPos = turret.Target.Position;

            var distToTarget = Vectors.Distance(Tile.Position, hitPos);
            var distPct = Maths.InverseLerpUnsafe(0f, range, distToTarget);
            var accuracy = attack.AccuracyCurve.Evaluate(distPct);
            var adjAccuracy = accuracy * maxAccuracy;

            bool hit = false;
            if (Rng.UChance(adjAccuracy)) {
                // a shot on target can still be caught by someone in the line of fire, a
                // colonist included (StrayShot)
                if (StrayShot.TryCatchOnTheWay(S, attack, turret.AttackableTurret,
                        FactionDef.Player, Tile.Position, targetAgent.CombatUnit,
                        out var caughtAt)) {
                    hitPos = caughtAt;
                } else {
                    hit = AttackAction.DealDamageTo(S, attack, turret.AttackableTurret,
                        targetAgent.CombatUnit);
                }
                if (hit) {
                    // the turret's StunChanceBio/StunChanceRobo were authored but never rolled,
                    // since turrets bypass AttackMethod.TryAttack
                    attack.TryStun(targetAgent.CombatUnit, Rng.Unseeded);
                }
            } else {
                // a miss flies on, as a gunner's does (StrayShot). Turrets fight for the crew
                // and ignore cover
                hitPos = StrayShot.Resolve(S, attack, turret.AttackableTurret,
                    FactionDef.Player, Tile.Position, targetAgent.CombatUnit, adjAccuracy, 1f,
                    adjAccuracy);
            }

            if (!hit) {
                var decalPos = hitPos + Rng.UInsideUnitCircle() * 0.15f;
                S.Sys.Dirt.Add(DirtData.Create(decalPos, Images.RandomMatching(
                    GraphicsId.DirtFireMark01, 2), Rng.URange(1f, 2f)));
            }

            // we tried shooting, not necessarily hit
            return true;
        }

        public bool HasRequiredAbilities(CombatUnit unit) {
            return false;
        }

        public void CheckTarget() {
            if (turret.Target != null && (targetAgent.Being.IsDead
                    || targetAgent.BeingData.Body.IsIncapacitated)) {
                SetTarget(null);
            }
        }
    }
}
