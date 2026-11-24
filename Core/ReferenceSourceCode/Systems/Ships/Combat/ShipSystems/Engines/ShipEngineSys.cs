using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Security.Policy;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipEngineSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipEngineSys());
        }
        private const string SysId = "ShipEngine";
        public override string Id => SysId;
        private readonly List<ShipEngine> enemyEngines = new();
        private readonly List<ShipEngine> playerEngines = new();

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Query.RegisterShipEngine.SetResponder(OnRegisterEngine);
            state.Clock.OnTick.AddListener(OnTick);
            state.Sig.NPCShipSpawnedInGameView.AddListener(OnShipSpawnInGameView);
        }

        private void OnShipSpawnInGameView(NPCShip ship, bool add) {
            if (add) {
                foreach (var e in ship.CombatState.Engines) {
                    enemyEngines.Add(e);
                }
            } else {
                foreach (var e in ship.CombatState.Engines) {
                    enemyEngines.Remove(e);
                    e.Reset();
                }
            }
        }

        private void OnTick(long ticks) {
            TickEngines(ticks);
        }

        private bool OnRegisterEngine(ShipEngine engine, bool add) {
            if (add) {
                if (engine.Device.Ship.IsPlayer) {
                    playerEngines.Add(engine);
                }
            } else {
                if (engine.Device.Ship.IsPlayer) {
                    playerEngines.Remove(engine);
                } else {
                    enemyEngines.Remove(engine);
                }
                engine.Reset();
            }
            S.Sig.ShipDevicePhaseChanged.Send(engine);
            return true;
        }

        private float maxHyperdriveCharge;
        private bool canHyperjump;

        private void TickEngines(long ticks) {
            bool needsUIRebuild = false;
            float maxCharge = 0f;
            bool canJump = false;
            foreach (var engine in playerEngines) {
                needsUIRebuild |= engine.Tick(S, ticks);
                if (!canJump) {
                    if (engine.EngineDef.IsHyperdrive) {
                        if (engine.Phase == ShipEnginePhase.Active) {
                            maxCharge = 1f;
                            canJump = true;
                        } else if (engine.Phase == ShipEnginePhase.Charging) {
                            maxCharge = Mathf.Max(maxCharge, engine.PhasePercent);
                        }
                    }
                }
            }
            if (maxCharge != maxHyperdriveCharge || canHyperjump != canJump) {
                maxHyperdriveCharge = maxCharge;
                canHyperjump = canJump;
                S.Sig.HyperspaceCombatStateChanged.Send(maxCharge, canJump);
            }
            foreach (var engine in enemyEngines) {
                needsUIRebuild |= engine.Tick(S, ticks);
            }
        }
    }
}
