using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Systems.Turrets;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipWeaponSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipWeaponSys());
        }
        private const string SysId = "ShipWeapon";
        public override string Id => SysId;
        public readonly List<ShipWeaponShot> ActiveShots = new();
        public readonly List<ShipWeapon> EnemyWeapons = new();
        public readonly List<ShipWeapon> PlayerWeapons = new();

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Query.ShootShipWeapon.SetResponder(OnShootWeapon);
            state.Query.RegisterShipWeapon.SetResponder(OnRegisterWeapon);
            state.Clock.OnTickProgress.AddListener(OnTickProgress);
            state.Clock.OnTick.AddListener(OnTick);
            state.Sig.BeforeSave.AddListener(OnBeforeSave);
            state.Sig.NPCShipSpawnedInGameView.AddListener(OnShipSpawnInGameView);
            Ready.When(WhenH.ShipViewDataRestored, RestoreData);
        }

        private void RestoreData() {
            foreach (var w in PlayerWeapons) {
                RestoreWeaponTarget(w);
            }
            foreach (var w in EnemyWeapons) {
                RestoreWeaponTarget(w);
            }
        }

        private void RestoreWeaponTarget(ShipWeapon w) {
            if (w.TargetData.Item1 != 0) {
                w.TargetShip = S.Query.NPCShipById.Ask(w.TargetData.Item1);
            }
            if (w.TargetData.Item2 != 0) {
                if (w.TargetShip == null) {
                    D.Err("Trying to deserialize ship weapon target when ship is null! {0}: {1}",
                        w, w.TargetData);
                } else {
                    var dev = S.Query.FindShipDevice.Ask(w.TargetShip,
                        w.TargetData.Item2);
                    if (dev != null) {
                        w.Target = dev.Device;
                    } else {
                        D.Err("Couldn't find target device: {0}", w.TargetData);
                    }
                }
            }
            if (w.TargetData.Item3 != 0) {
                var ntt = S.FindEntity<Entity>(w.TargetData.Item3);
                if (ntt != null) {
                    if (ntt.TryGetComponent<IShipCombatTurretTarget>(out var comp)) {
                        w.NonShipTarget = comp;
                    } else {
                        D.Err("Couldn't find target entity: {0}", w.TargetData.Item3);
                    }
                }
            }
            w.TargetData = (0, 0, 0);
        }

        private void OnShipSpawnInGameView(NPCShip ship, bool add) {
            if (add) {
                foreach (var w in ship.CombatState.Weapons) {
                    EnemyWeapons.Add(w);
                }
            } else {
                foreach (var w in ship.CombatState.Weapons) {
                    EnemyWeapons.Remove(w);
                    w.Reset();
                }
            }
        }

        private void OnTick(long ticks) {
            TickWeapons(ticks);
        }

        private bool OnRegisterWeapon(ShipWeapon weapon, bool add) {
            if (add) {
                if (weapon.Device.Ship.IsPlayer) {
                    PlayerWeapons.Add(weapon);
                }
            } else {
                if (weapon.Device.Ship.IsPlayer) {
                    PlayerWeapons.Remove(weapon);
                } else {
                    EnemyWeapons.Remove(weapon);
                }
                weapon.Reset();
            }
            S.Sig.ShipDevicePhaseChanged.Send(weapon);
            return true;
        }

        private void OnBeforeSave(GameState state, bool willExit) {
            if (!willExit) { return; }
            foreach (var shot in ActiveShots) {
                shot.OnEndAnimate?.Invoke(S, shot);
            }
            ActiveShots.Clear();
        }

        private void OnTickProgress(float progress) {
            var dt = GameTimer.deltaTime;
            UpdateShots(dt);
        }

        private void TickWeapons(long ticks) {
            var needsUIRebuild = false;
            foreach (var weapon in PlayerWeapons) {
                needsUIRebuild |= weapon.Tick(S, ticks);
            }
            foreach (var weapon in EnemyWeapons) {
                needsUIRebuild |= weapon.Tick(S, ticks);
            }
        }

        private void UpdateShots(float dt) {
            for (int i = ActiveShots.Count - 1; i >= 0; i--) {
                var shot = ActiveShots[i];
                shot.TimeTillEndAnimate -= dt;
                if (shot.TimeTillEndAnimate <= 0f) {
                    ActiveShots.RemoveAt(i);
                    shot.OnEndAnimate?.Invoke(S, shot);
                }
            }
        }

        //continue here - ship weapons have to register here and do shooty things

        private bool OnShootWeapon(ShipWeaponShot shot) {
            shot.Execute(S);
            if (G.ClockSpeed > ClockSpeed.Fast2) {
                shot.TimeTillEndAnimate = 0f;
                shot.OnEndAnimate?.Invoke(S, shot);
            } else {
                ActiveShots.Add(shot);
            }
            return true;
        }
    }
}
