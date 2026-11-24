using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Space;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipShieldSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipShieldSys());
        }
        private const string SysId = "ShipShield";
        public override string Id => SysId;
        private readonly List<ShipShield> enemyShields = new();
        private readonly List<ShipShield> playerShields = new();
        private ForceFieldView playerShieldView;
        /// <summary>
        /// Every NPC ship that has a view in the game map, both sides of the fight.
        /// Shields tick off having a view, not off being an enemy.
        /// </summary>
        private readonly List<NPCShip> shipsInView = new();
        private readonly HashSet<IThreat> shieldThreats = new();

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Query.RegisterShipShield.SetResponder(OnRegisterShield);
            state.Query.ShakePlayerShipShield.SetResponder(ShakePlayerShield);
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.GetPlayerShield.SetResponder(GetPlayerShield);
            state.Query.IsPlayerShieldAnimating.SetResponder(() => playerShieldView.IsAnimating);
            state.Query.IsPlayerShieldedFromStorm.SetResponder(IsShieldedFromStorm);
            state.Sig.NPCShipSpawnedInGameView.AddListener(OnShipSpawnInGameView);
            state.Sig.RegisterThreat.AddListener(OnRegisterThreat);
            state.Sig.AreasRecalculated.AddListener(OnAreasRecalc);
            state.Sig.EveryFrame.AddListener(EveryFrame);
            playerShieldView = new ForceFieldView(S);
            Ready.When(WhenH.ShipViewDataRestored, RestoreData);
        }

        private void EveryFrame() {
            playerShieldView.Update();
        }

        private void OnAreasRecalc() {
            var shields = S.Components.AllByType<ShipShieldComp>();
            if (shields != null) {
                foreach (ShipShieldComp sh in shields) {
                    sh.UpdateEnergyCost();
                }
            }
            if (playerShieldView.IsUp) {
                ShowPlayerShield(true);
            }
        }

        private void OnRegisterThreat(IThreat threat, bool add) {
            if (SecuritySys.IsSpaceThreatType(threat.ThreatType)) {
                if (add) {
                    shieldThreats.Add(threat);
                } else {
                    shieldThreats.Remove(threat);
                }
            }
        }

        private (bool, Vector2, float) GetPlayerShield() {
            if (playerShieldView?.IsUp != true) {
                return (false, default, 0f);
            } else {
                // No protection while the shield is still visibly going up. Center and
                // radius stay valid because shot targeting reads them regardless.
                return (playerShieldView.IsVisualUp, playerShieldView.Center,
                    playerShieldView.Radius);
            }
        }


        // Computed on demand from the shield phases rather than cached per tick: right after a
        // load the grid can tick before the shields do, and a stale "unprotected" would let the
        // storm hit the emitter itself and knock the restored shield down
        private bool IsShieldedFromStorm() {
            if (!SpaceEffectMagneticStorm.IsOngoing(S)) { return false; }
            foreach (var shield in playerShields) {
                // Protection starts with the Active phase, which is tick-based, not with the
                // frame-timed raise animation
                if (shield.Phase == ShipShieldPhase.Active && HasEMPShield(shield)) {
                    return true;
                }
            }
            return false;
        }

        private static bool HasEMPShield(ShipShield shield) {
            return shield.Comp != null && shield.Comp.HasEMPShield;
        }

        private void RestoreData() {
            if (A.Player.Ship.CombatState?.IsShieldUp == true) {
                ShowPlayerShield(instant: true);
            }
            foreach (var ship in shipsInView) {
                if (ship.CombatState.IsShieldUp) {
                    ship.View.ToggleShieldVisual(true, instant: true);
                }
            }
        }

        private bool ShakePlayerShield(float amt) {
            playerShieldView.Shake(amt);
            return true;
        }

        private void OnShipSpawnInGameView(NPCShip ship, bool add) {
            if (add) {
                if (!shipsInView.Contains(ship)) {
                    shipsInView.Add(ship);
                }
                foreach (var sh in ship.CombatState.Shields) {
                    enemyShields.Add(sh);
                }
            } else {
                shipsInView.Remove(ship);
                foreach (var sh in ship.CombatState.Shields) {
                    enemyShields.Remove(sh);
                    sh.Reset();
                }
            }
        }
        private void ShowPlayerShield(bool instant = false) {
            var b = S.Sys.Areas.ShipBounds;
            if (b.IsValid) {
                var ext = b.Extents.magnitude + 1.05f;
                var maxext = S.GridWidth - Consts.MapMargin * 2;
                var center = b.Center();
                if (ext > maxext) {
                    center = S.Grid.Center;
                    ext = maxext;
                }
                playerShieldView.Enable(center, ext, instant);
            }
        }

        private void OnTick(long ticks) {
            TickShields(ticks);
        }

        private bool OnRegisterShield(ShipShield shield, bool add) {
            if (add) {
                if (shield.Device.Ship.IsPlayer) {
                    playerShields.Add(shield);
                }
            } else {
                if (shield.Device.Ship.IsPlayer) {
                    playerShields.Remove(shield);
                } else {
                    enemyShields.Remove(shield);
                }
                shield.Reset();
            }
            S.Sig.ShipDevicePhaseChanged.Send(shield);
            return true;
        }

        private void TickShields(long ticks) {
            var pShip = A.Player?.Ship;
            if (pShip == null || pShip.CombatState == null) {
                D.Err("Ticking shields while player ship / combat state not ready!");
                return;
            }
            pShip.CombatState.NumShieldsUp = 0;
            var needsUIRebuild = false;

            var hasShieldThreats = shieldThreats.Count > 0;
            // Both the storm story event and the permanent sector storm drive the same keys
            var isMagneticStorm = SpaceEffectMagneticStorm.IsOngoing(S);

            foreach (var shield in playerShields) {
                // An EMP Shield upgrade raises its emitter for the storm, other emitters
                // keep waiting for combat or a space threat
                var isStormShield = isMagneticStorm && HasEMPShield(shield);
                needsUIRebuild |= shield.Tick(S, ticks, hasShieldThreats || isStormShield);
                if (shield.Phase == ShipShieldPhase.Active) {
                    pShip.CombatState.NumShieldsUp++;
                }
            }

            foreach (var ship in shipsInView) {
                ship.CombatState.NumShieldsUp = 0;
            }

            foreach (var shield in enemyShields) {
                var cs = shield.Device.Ship.CombatState;
                needsUIRebuild |= shield.Tick(S, ticks, false);
                if (shield.Phase == ShipShieldPhase.Active) {
                    cs.NumShieldsUp++;
                }
            }

            foreach (var ship in shipsInView) {
                ship.View?.ToggleShieldVisual(ship.CombatState.IsShieldUp);
            }

            if (pShip.CombatState.IsShieldUp && !playerShieldView.IsUp) {
                // A.S.Sys.Log.AddLine($"{T.NounShields} {GlyphMap.ArrowRight} {Texts.Green(T.Active)}");
                ShowPlayerShield();
            } else if (!pShip.CombatState.IsShieldUp && playerShieldView.IsUp) {
                // A.S.Sys.Log.AddLine($"{T.NounShields} {GlyphMap.ArrowRight} {Texts.Red(T.StateDisabled)}");
                playerShieldView.Disable(false);
            }

        }
    }
}
