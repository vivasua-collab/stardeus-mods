using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX;
using UnityEngine;
using System;
using Game.Systems.Creatures;
using KL.Utils;
using Game.UI;
using Game.Systems.AI;
using KL.Grid;

namespace Game.Components {
    public sealed class BotDockComp : BaseComponent<BotDockComp>,
            IRelocatable, IDefPowerProvider, IRanged {
        private const float DefaultChargeCost = 1f;
        public const int MaxWorkRadius = 50;

        // The assigned bot only takes colony tasks this close to the dock, 0 for anywhere.
        // Why and where it is checked: Docs/Architecture/Systems/BotDockSystem.md
        public int WorkRadius { get; private set; }
        public int WorkRadiusSqr { get; private set; }
        public float Range => WorkRadius;
        public Color RadiusColor => Consts.ColorWorkRadiusRange;
        public bool RadiusSameArea => false;

        public void SetWorkRadius(int radius) {
            WorkRadius = Mathf.Clamp(radius, 0, MaxWorkRadius);
            WorkRadiusSqr = WorkRadius * WorkRadius;
        }

        public bool IsInWorkArea(int pos) {
            return WorkRadiusSqr == 0 || Pos.SqrDistance(Tile.PosIdx, pos) <= WorkRadiusSqr;
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost));
        }
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BotDockComp());
        }

        // Assign the dock to the BeingData rather than Being entity
        // This way we can despawn / respawn the being with a new id and
        // the dock will remain correctly assigned
        public uint AssignedBeingDataId { get; private set; }
        public void SetAssignedBeingData(uint id) {
            AssignedBeingDataId = id;
            if (id == 0) {
                assignedDockable = null;
            }
        }
        private uint dockedBeingDataId;
        public bool IsAssigned => AssignedBeingDataId != 0;

        private BotDockableComp assignedDockable;
        public BotDockableComp AssignedDockable {
            get {
                if (AssignedBeingDataId == 0) { return null; }
                if (assignedDockable != null) {
                    // The assigned bot can be despawned/respawned (e.g. expeditions)
                    // with a new Being entity and a NEW BotDockableComp, while keeping
                    // the same BeingData and BeingData.Id. The old cached component
                    // still reports the same BeingDataId, so comparing ids alone misses
                    // the swap and we keep handing back a dead component - CanDock then
                    // fails because old instance ≠ new instance, trapping the bot in a
                    // recharge-pending loop. Also verify the cached dockable belongs to
                    // the CURRENT live being by checking BeingData.Being.
                    var cachedBeing = assignedDockable.Being;
                    if (cachedBeing == null || cachedBeing.Data == null
                            || cachedBeing.Data.Id != AssignedBeingDataId
                            || cachedBeing.Data.Being != cachedBeing) {
                        assignedDockable = null;
                    }
                }
                if (assignedDockable == null) {
                    var bd = S.Query.FindBeingData.Ask(AssignedBeingDataId);
                    if (bd == null || bd.HasStateFlag(BeingState.Dead)) {
                        if (AssignedBeingDataId != 0) {
                            D.Warn("Unassigning dead dockable for {0}", this);
                        }
                        AssignedBeingDataId = 0;
                        return null;
                    }
                    if (bd.Being == null) {
                        // We won't unassign, it could be off the map
                        return null;
                    }
                    assignedDockable = bd.Being.AIAgent.Dockable;
                }
                return assignedDockable;
            }
            // set {
            //     if (value == null) {
            //         if (AssignedBeingDataId != 0) {
            //             D.Warn("Unassigning dockable for {0}", this);
            //         }
            //         AssignedBeingDataId = 0;
            //         assignedDockable = null;
            //     } else {
            //         if (AssignedBeingDataId == 0) {
            //             D.Warn("Assigning dockable for {0}", this);
            //         }
            //         AssignedBeingDataId = value.BeingDataId;
            //         assignedDockable = value;
            //     }
            //     UpdateUIBlock(true);
            // }
        }
        public bool AutoAssign;

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private bool isRegistered;
        private ElectricNodeComp elNode;
        private float chargePerHour;
        private float chargeCost;
        private float chargePerGridTick;
        private bool isCharging;
        public bool IsCharging => isCharging;
        #if !PRODUCTION_BUILD
        public Action<BotDockComp, BotDockableComp, bool> OnCharge;
        #endif

        public bool IsAssignable(bool skipAssigned, bool auto) {
            if (skipAssigned && AssignedBeingDataId != 0) {
                return false;
            }
            if (auto && !AutoAssign) { return false; }
            if (!Tile.IsConstructed) { return false;}
            return elNode.IsPowered && !elNode.HasDeficit;
        }


        protected override void OnConfig() {
            AutoAssign = Config.GetBool(PropertyIdH.AutoAssign, true);
            chargePerHour = Config.GetFloat(PropertyIdH.ChargePerHour, 5f);
            chargePerGridTick = chargePerHour / Consts.TicksPerHour * 10f;
            chargeCost = Config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("DockedBDId", dockedBeingDataId);
            data.SetUInt("AssignedBeingDataId", AssignedBeingDataId);
            data.SetBool("AutoAssign", AutoAssign);
            data.SetInt("WorkRadius", WorkRadius);
        }

        protected override void OnLoad(ComponentData data) {
            dockedBeingDataId = data.GetUInt("DockedBDId", 0);
            AssignedBeingDataId = data.GetUInt("AssignedBeingDataId", 0);
            AutoAssign = data.GetBool("AutoAssign", true);
            SetWorkRadius(data.GetInt("WorkRadius", 0));
        }

        public override string ToString() {
            return $" * Dock[Ass:{AssignedBeingDataId}|{Tile.Position}]";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed) {
                RegisterDock(true);
            }
        }

        private void RegisterDock(bool yes) {
            if (yes != isRegistered) {
                isRegistered = yes;
                S.Sig.RegisterBotDock.Send(this, yes);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    elNode.SetConsumerWantedInput(0f);
                    isCharging = false;
                    enode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                RegisterDock(true);
            } else if (message == MsgIdH.HealthWentToZero) {
                RegisterDock(false);
            }
        }

        private void AfterTickGrid() {
            if (dockedBeingDataId == 0 && AssignedBeingDataId != 0) {
                // Auto-dock an assigned bot resting on the dock tile even if no AI
                // docking action put it there (e.g. it discharged and got
                // immobilized while just standing on its own dock) so it still
                // charges instead of being stuck waiting for emergency recharge.
                var assigned = AssignedDockable;
                if (assigned?.Being != null && assigned.Being.PosIdx == Tile.PosIdx) {
                    TryDock(assigned);
                }
            }
            if (dockedBeingDataId != 0) {
                if (CheckDockable()) {
                    ChargeDockable();
                }
            } else if (isCharging) {
                isCharging = false;
                elNode.SetConsumerWantedInput(0f);
            }
        }

        private void ChargeDockable() {
            var docked = AssignedDockable;
            var stats = docked.Being.Data.StatsBeing;
            var charge = stats.BatteryCharge;
            var cap = stats.BatteryCapacity;
            // bot went specifically for a recharge
            var isRecharging = docked.Being.AIAgent.CurrentAction?.IdH
                    == AIActionIdleDock.RechargeActIdH;
            var maxCharge = isRecharging ? 1.0f : 0.95f;
            var minCharge = isRecharging ? 0.99f : 0.9f;
            if (isCharging) {
                if (charge.Value >= cap.Value * maxCharge) {
                    // end charging
                    elNode.SetConsumerWantedInput(0f);
                    isCharging = false;
                    #if !PRODUCTION_BUILD
                    OnCharge?.Invoke(this, docked, false);
                    #endif
                } else {
                    var bc = stats.BatteryConsumption;
                    // compensate battery consumption for grid tick (10 ticks) + charge
                    charge.TryIncrease(bc.Value * 10f + chargePerGridTick);
                }
            } else if (charge.Value < cap.Value * minCharge) {
                // start charging
                elNode.SetConsumerWantedInput(chargeCost);
                isCharging = true;
                #if !PRODUCTION_BUILD
                OnCharge?.Invoke(this, docked, true);
                #endif
            }
        }

        public override void OnRemove() {
            RegisterDock(false);
        }

        public void UpdateUIBlock(bool wasUpdated) {
            stateChangedB?.Invoke();
        }

        public bool IsDocked(BotDockableComp dockable) {
            return dockedBeingDataId != 0 && dockedBeingDataId == dockable.BeingDataId;
        }

        public void RelocateTo(Entity target) {
            if (!target.TryGetComponent<BotDockComp>(out var other)) { return; }
            other.SetWorkRadius(WorkRadius);
            if (AssignedDockable == null || AssignedBeingDataId == 0) { return; }
            var bd = S.Query.FindBeingData.Ask(AssignedBeingDataId);
            if (bd != null) {
                S.Query.AssignBotDock.Ask(bd, other);
            }
        }

        public bool Dock(BotDockableComp dockable) {
            if (!Pos.AreAdjacent(dockable.Being.PosIdx, Tile.PosIdx)) {
                D.Err("Trying to dock a being that is not on top of dock! {0} / {1}",
                    Tile, dockable.Being);
                return false;
            }
            if (TryDock(dockable)) {
                dockable.Being.Motion.TeleportTo(Tile.Position);
                dockable.Being.Graphics.SetFacing(Tile.Transform.Rotation);
                UpdateUIBlock(true);
                dockable.UpdateUIBlock(true);
                return true;
            }
            return false;
        }

        public void Undock(bool manual) {
            if (manual && AssignedDockable != null) {
                // don't auto-dock again immediately
                AssignedDockable.Being.AIAgent.BB.SetCooldown(
                    AIActionIdleDock.IdleActIdH, G.Ticks,
                    Consts.TicksPer30Minutes, Consts.TicksPerHour);
            }
            dockedBeingDataId = 0;
            AssignedDockable?.UpdateUIBlock(true);
            UpdateUIBlock(true);
            if (isCharging) {
                isCharging = false;
                elNode.SetConsumerWantedInput(0f);
            }
        }

        private bool CheckDockable() {
            var assigned = AssignedDockable;
            if (assigned == null || assigned.Being == null) {
                if (isCharging) {
                    isCharging = false;
                    elNode.SetConsumerWantedInput(0f);
                }
                return false;
            }
            if (AssignedBeingDataId != dockedBeingDataId) {
                D.Err("Wrong being was docked! {0} / Assigned: {1}",
                    dockedBeingDataId, assigned);
                Undock(false);
                return false;
            } else if (!CanDock(assigned) || assigned.Being.PosIdx != Tile.PosIdx) {
                Undock(false);
                return false;
            }
            return true;
        }

        public bool CanDock(BotDockableComp dockable) {
            if (dockable != null && AssignedDockable == dockable) {
                return elNode.IsPowered && !elNode.HasDeficit;
            }
            // D.Err("Can't dock: {0}", dockable);
            return false;
        }

        private bool TryDock(BotDockableComp dockable) {
            if (dockable.Being.PosIdx != Tile.PosIdx) {
                return false;
            }
            if (CanDock(dockable)) {
                dockedBeingDataId = dockable.BeingDataId;
                return true;
            }
            return false;
        }
    }
}
