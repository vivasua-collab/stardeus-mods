using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipCapacitorSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipCapacitorSys());
        }

        private const string SysId = "ShipCapacitor";
        public override string Id => SysId;

        private readonly List<ShipCapacitor> playerCapacitors = new();
        private readonly List<ShipCapacitor> enemyCapacitors = new();
        public override void Unload() {
            playerCapacitors.Clear();
            enemyCapacitors.Clear();
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Query.RegisterShipCapacitor.SetResponder(OnRegisterCapacitor);
            state.Clock.OnTick.AddListener(OnTick);
            state.Sig.NPCShipSpawnedInGameView.AddListener(OnShipSpawnInGameView);
        }

        private void OnShipSpawnInGameView(NPCShip ship, bool add) {
            if (add) {
                foreach (var cap in ship.CombatState.Capacitors) {
                    enemyCapacitors.Add(cap);
                }
            } else {
                foreach (var cap in ship.CombatState.Capacitors) {
                    enemyCapacitors.Remove(cap);
                    cap.Reset();
                }
            }
        }

        private bool OnRegisterCapacitor(ShipCapacitor capacitor, bool add) {
            if (add) {
                if (capacitor.Device.Ship.IsPlayer) {
                    playerCapacitors.Add(capacitor);
                }
            } else {
                if (capacitor.Device.Ship.IsPlayer) {
                    playerCapacitors.Remove(capacitor);
                } else {
                    enemyCapacitors.Remove(capacitor);
                }
                capacitor.Reset();
            }
            S.Sig.ShipDevicePhaseChanged.Send(capacitor);
            return true;
        }

        private void OnTick(long ticks) {
            TickCapacitors(ticks);
        }

        private void TickCapacitors(long ticks) {
            var shieldState = S.Query.GetPlayerShield.Ask();
            // Capacitors only help a shield that is taking hits. A shield held up against a
            // magnetic storm absorbs nothing, so it must not keep them drawing power for days
            var forceUp = shieldState.Item1 && S.Sys.Sec.HasActiveSpaceThreats;
            foreach (var capacitor in playerCapacitors) {
                capacitor.Tick(S, ticks, forceUp: forceUp);
            }

            foreach (var capacitor in enemyCapacitors) {
                var cs = capacitor.Device.Ship.CombatState;
                capacitor.Tick(S, ticks);
            }
        }
    }
}
