using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using KL.Randomness;
using Game.Components;
using Game.Components.Upgrades;
using System;

namespace Game.Systems.Nanobots {
    public sealed class NanobotHiveComp : BaseComponent<NanobotHiveComp>, IRangedShowOthers,
            IRangedAreaOutline, IEnergyEfficiencyUpgradeable, ISpeedUpgradeable,
            IDefPowerProvider {
        private const float DefaultEnergyCost = 15f;

        public ElectricNodeComp ENode => elNode;
        public int RangeGroupId => Tile.Definition.IdH;
        public float RangeOthersMaxSqrDist { get; private set; }
        public float Range { get; private set; }
        public float RangeSqr;
        public Color RadiusColor => Consts.ColorTurretRange;
        public bool RadiusSameArea => true;
        private const float HaulingFuelMultiplier = 0.1f;
        private float pendingFuelMultiplier;
        private float fuelPerWorkUnit;
        private double nextSoundTime;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new NanobotHiveComp());
        }
        public RefillableStorageComp Fuel;
        private float energyCost;
        private bool isRegistered;
        private float workSpeed;
        public bool ShowParticles;
        protected override void OnConfig() {
            Range = Config.GetInt(PropertyIdH.Range, 15);
            RangeSqr = Range * Range;
            RangeOthersMaxSqrDist = 0;
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            fuelPerWorkUnit = Config.GetFloat(PropertyIdH.FuelPerDay, 1f);
            fuelPerWorkUnit /= Consts.TicksPerDay;
            // multiplying * 10 because electricity ticks
            fuelPerWorkUnit *= 10;
            workSpeed = Config.GetFloat(PropertyIdH.Speed, 1f);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * NanobotHub";
        }

        public override void OnLateReady(bool wasLoaded) {
            Entity.TryGetComponent(out Fuel);
            if (Tile.IsConstructed) {
                RegisterHub(true);
            }
        }
        private ElectricNodeComp elNode;
        private Entity lastWorkedOn;
        public bool CanWork;
        private float upgradeEnergyEfficiency = 1f;
        private float upgradeSpeed = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;
        public bool IsSpeedUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} -> {1}",
                    amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    if (Fuel == null) {
                        // constant burn
                        elnode.SetConsumerWantedInput(GetActualEnergyCost());
                    }
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                RegisterHub(true);
            } else if (message == MsgIdH.HealthWentToZero) {
                RegisterHub(false);
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
                CanWork = false;
            }
        }
        private void RegisterHub(bool yes) {
            if (!yes) {
                CanWork = false;
                pendingFuelMultiplier = 0f;
            }
            if (yes != isRegistered) {
                isRegistered = yes;
                S.Sig.RegisterNanobotHive.Send(this, yes);
                if (yes) {
                    The.AchievementTracker.Unlock(
                        Achievements.AchievementNanobotHive.AchievementId);
                }
            }
        }

        private float GetActualEnergyCost() {
            var actualEnergyCost = energyCost;
            if (upgradeEnergyEfficiency > 1f) {
                actualEnergyCost *= 1f / upgradeEnergyEfficiency;
            }
            return actualEnergyCost;
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable || elNode.HasDeficit) {
                CanWork = false;
                return;
            }

            if (Fuel.IsEmpty) {
                CanWork = false;
                Fuel.NotifyLack();
                Fuel.CreateOrUpdateRefillGoal();
                elNode.SetConsumerWantedInput(0f);
                return;
            } else {
                CanWork = true;
                elNode.SetConsumerWantedInput(GetActualEnergyCost());
            }

            if (pendingFuelMultiplier > 0f) {
                var fuelMultiplier = pendingFuelMultiplier;
                pendingFuelMultiplier = 0f;
                ShowParticles = true;
                if (Fuel.TryConsume(fuelPerWorkUnit * fuelMultiplier)) {
                    Fuel.ClearDeficitFlag();
                    Fuel.CreateOrUpdateRefillGoal();
                } else {
                    // no fuel = no oxygen
                    elNode.SetConsumerWantedInput(0f);
                    CanWork = false;
                    return;
                }
            }
        }
        public override void OnRemove() {
            RegisterHub(false);
        }

        public void AddWorkFor(INanobotCompatible workable, long ticks) {
            workable.AddNanobotsEffort(ActualWorkSpeed, out var goal);
            if (goal == null) { return; }
            MarkWorked(workable.Entity);
        }

        public float ActualWorkSpeed {
            get {
                var actualWorkSpeed = workSpeed;
                if (upgradeSpeed > 1f) {
                    actualWorkSpeed *= upgradeSpeed;
                }
                return actualWorkSpeed;
            }
        }

        public void MarkWorked(Entity target) {
            MarkWorked(target, 1f);
        }

        internal void MarkHaulingWorked(Entity target) {
            MarkWorked(target, HaulingFuelMultiplier);
        }

        private void MarkWorked(Entity target, float fuelMultiplier) {
            if (target == null) { return; }
            pendingFuelMultiplier = Mathf.Max(pendingFuelMultiplier,
                fuelMultiplier);
            if (target != lastWorkedOn) {
                lastWorkedOn = target;
                ShowParticles = true;
                nextSoundTime = 0f;
            }
            if (nextSoundTime < GameTimer.realtimeSinceStartupAsDouble) {
                nextSoundTime = GameTimer.realtimeSinceStartupAsDouble
                    + 5f / S.Clock.FXTimeScale;
                Sounds.PlayNanobots(target.Position);
            }
        }
    }
}
