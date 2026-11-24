using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI;

namespace Game.Components {
    public sealed class BridgeControlsComp : BaseComponent<BridgeControlsComp>,
            IDefPowerProvider, IOperatorWorkSource {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BridgeControlsComp());
        }
        private const float DefaultEnergyCost = 20f;
        private float energyCost;
        public bool IsOperatorRequired;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            IsOperatorRequired = Config.GetBool(PropertyIdH.IsOperatorRequired, false);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * BridgeControls";
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    enode.AddAfterTick(this, AfterTickGrid);
                    enode.SetConsumerWantedInput(energyCost);
                    elNode = enode;
                    if (elNode.IsConsuming) {
                        S.BB.Set(BBKeyH.BridgeControlsPing, G.Ticks);
                    }
                }
            }
        }

        public void AfterTickGrid() {
            //D.Warn("Received BC ping. {0}. Grid: {1}", eNode.Node, eNode.Grid);
            if (!elNode.IsConsuming || !elNode.IsPowerable) {
                CheckOperator(false);
                return;
            }
            S.BB.Set(BBKeyH.BridgeControlsPing, G.Ticks);
            CheckOperator(IsFlightWanted);
        }

        // a flight to make or a fight to evade in
        private bool IsFlightWanted => S.Sys.Ship.NeedsOperator || A.Player?.Ship?.IsInCombat == true;
        public bool IsOperatorWorking => IsOperational && IsFlightWanted;

        public override void OnLateReady(bool wasLoaded) {
            if (operatable == null && !Entity.TryGetComponent(out operatable)) {
                D.Err("BridgeControls required operator but has not OperatableDevice component in def! {0}",
                    Entity.DefinitionId);
                return;
            }
            operatable.OnGoalCancel = OnOperatableGoalCancel;
        }

        private OperatableDeviceComp operatable;
        public Being Operator => operatable?.Operator;
        /// <summary>
        /// Whether this bridge is genuinely functional right now (constructed, not being
        /// relocated, and drawing power) - used to gate the piloting evade bonus so a
        /// kept-but-unpowered operator doesn't still grant it.
        /// </summary>
        public bool IsOperational =>
            Tile.IsConstructed
            && !Tile.IsPendingMove
            && elNode != null
            && elNode.IsPowerable
            && elNode.IsConsuming;
        public bool IsAutomated => operatable != null && operatable.IsAutomated;

        private void CheckOperator(bool isWanted) {
            // This is for autopilot, not for not flying
            if (!IsOperatorRequired) {
                if (isWanted) {
                    S.BB.Set(BBKeyH.BridgeControlsOperatorPing, G.Ticks);
                }
                return;
            }
            if (isWanted) {
                operatable.HailOperator(false);
                if (operatable.Operator != null) {
                    S.BB.Set(BBKeyH.BridgeControlsOperatorPing, G.Ticks);
                }
            } else {
                if (operatable.KeepOperator) {
                    if (operatable.Operator != null) {
                        S.BB.Set(BBKeyH.BridgeControlsOperatorPing, G.Ticks);
                    }
                } else {
                    operatable.DismissOperator(true);
                }
            }
        }

        private void OnOperatableGoalCancel(AIGoal goal) {
            S.Sys.Ship.CancelFlight();
            S.Sys.ShipNav.SetWantHyperspace(false);
            if (A.IsInHyperspace) {
                S.Sys.ShipNav.ExitHyperspace(T.ShipNoBridgeControlsOperator);
            }
        }
    }
}
