using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipCombatStationsWidget : BaseWidget {
        [SerializeField] private WGroup widgetsContainer;
        private GameState S;
        private Lifetime lifetime;
        private Lifetime bsLife;
        private readonly List<WBattleStation> spawnedStations = new();
        private bool pendingRebuild;

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            widgetsContainer.DestroyChildren();
            Register(UIWidgetId.ShipCombatStations, this);
            S = s;
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("ShipCombatStations");
            RebuildBattleStations();
        }

        private bool AnyStationHoldingFire() {
            foreach (var s in spawnedStations) {
                if (s.IsFireButtonHeldDown) { return true; }
            }
            return false;
        }

        public void RebuildBattleStations() {
            if (AnyStationHoldingFire()) {
                pendingRebuild = true;
                return;
            }
            pendingRebuild = false;
            if (bsLife != null) {
                bsLife.Dispose();
            }
            if (lifetime == null || lifetime.IsDisposed) {
                return;
            }
            spawnedStations.Clear();
            bsLife = lifetime.CreateChild("stations.bs");
            int idx = 0;
            var allStations = S.Components.AllByType<BattleStationComp>();
            if (!Arrays.IsEmpty(allStations)) {
                foreach (var component in allStations) {
                    var bs = (BattleStationComp) component;
                    if (!bs.IsConstructed) { continue; }
                    if (!bs.Tile.ENode.IsPowerable) { continue; }
                    var shortcutIndex = idx < 10 ? idx + 1 : 0;
                    var w = UISpawn.Widget<WBattleStation>(bsLife,
                        UIPrefabId.WBattleStation, widgetsContainer.Container, idx++);
                    w.Load(bs, bsLife, shortcutIndex);
                    spawnedStations.Add(w);
                }
            } else {
                var w = UISpawn.Widget<WIconText>(bsLife, UIPrefabId.WWarningBannerWrapped,
                    widgetsContainer.Container, idx++);
                w.Load(IconId.CWarning, T.MustBuildBattleStationsTip_V1.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation)));
            }
        }

        public void SimulateFireHold(int stationIndex, bool isDown) {
            if (stationIndex < 0 || stationIndex >= spawnedStations.Count) {
                return;
            }
            spawnedStations[stationIndex].SimulateFireHold(isDown);
        }

        public void ResetSimulatedFireHolds() {
            for (int i = spawnedStations.Count - 1; i >= 0; i--) {
                spawnedStations[i].ResetSimulatedFireHold();
            }
        }

        private readonly Dictionary<int, (int ready, int firing, float winding, float charging, int total)> counts = new();

        public void UpdateBattleStations() {
            if (lifetime == null) { return; }
            if (pendingRebuild && !AnyStationHoldingFire()) {
                RebuildBattleStations();
                return;
            }
            counts.Clear();
            foreach (var weapon in A.Player.Ship.CombatState.Weapons) {
                var bs = weapon.BackingStation;
                if (bs == null) { continue; }
                if (!weapon.CanShoot || !weapon.IsReachable) { continue; }
                var id = bs.EntityId;
                if (!counts.TryGetValue(id, out var cnts)) {
                    cnts = default;
                }
                cnts.total++;
                switch (weapon.Phase) {
                    case ShipWeaponPhase.Windup: cnts.winding += weapon.PhasePercent; break;
                    case ShipWeaponPhase.Ready: cnts.ready++; break;
                    case ShipWeaponPhase.Firing: cnts.firing++; break;
                    case ShipWeaponPhase.Charging: cnts.charging += weapon.PhasePercent; break;
                    case ShipWeaponPhase.Disabled:
                    case ShipWeaponPhase.OutOfAmmo:
                    case ShipWeaponPhase.Inactive: break;
                    default: D.Err("Unhandled weapon phase in update battle stations: {0}", weapon.Phase); break;
                }
                counts[id] = cnts;
            }
            for (int i = spawnedStations.Count - 1; i >= 0; i--) {
                var station = spawnedStations[i];
                if (!counts.TryGetValue(station.Comp.EntityId, out var cnts)) {
                    cnts = default;
                }
                if (cnts.total > 0) {
                    station.UpdateState(cnts.ready, cnts.firing, cnts.charging / cnts.total, cnts.winding / cnts.total, cnts.total);
                } else {
                    station.UpdateState(0, 0, 0f, 0f, 0);
                }
            }
        }

        private void Cleanup() {
            ResetSimulatedFireHolds();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            spawnedStations.Clear();
        }
    }
}
