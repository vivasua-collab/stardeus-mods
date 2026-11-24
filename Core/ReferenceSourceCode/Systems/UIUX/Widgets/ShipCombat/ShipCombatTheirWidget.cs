using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipCombatTheirWidget : BaseWidget {
        [SerializeField] private WButton wButtonNextEnemy;
        [SerializeField] private WButton wButtonPrevEnemy;
        [SerializeField] private WText wTextHeader;
        [SerializeField] private WText2 wTextClassPoints;
        [SerializeField] private WProgressText2Stacked wProgressFlux;
        [SerializeField] private WProgressText2 wProgressShield;
        [SerializeField] private WProgressText2 wProgressHP;
        [SerializeField] private WText2 wTextThrusters;
        [SerializeField] private WText2 wTextFluxCapacitors;
        [SerializeField] private WText2 wTextFluxVents;
        [SerializeField] private WText wTextWeaponsHeader;
        [SerializeField] private WGroup wContainerWeapons;
        [SerializeField] private WGroup wButtonGroup;
        [SerializeField] private WRawImage wEnemyShipPIP;
        [SerializeField] private GameObject wPIPGameObject;
        [SerializeField] private WButton wPIPButton;
        private GameState S;
        private Lifetime lifetime;
        private Lifetime enemyLifetime;
        private NPCShip currentEnemy;
        private PIPCamera pip;
        private bool showPIP;
        private readonly List<WProgressText2> weaponRows = new();
        /// <summary>
        /// Everything the player can cycle through: enemies first, then the NPC ships
        /// fighting for the player. The player's own ship has its own widget.
        /// </summary>
        private readonly List<NPCShip> shownShips = new();
        private Lifetime weaponsLifetime;
        private ShipCombatWidgetUtils.FluxVentStats fluxVentStats;
        private bool hasFluxVentStats;
        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.ShipCombatTheir, this);
            SetColors();
            wPIPButton.Load(OnClickPIP);
            S = s;
        }

        private void OnClickPIP() {
            currentEnemy?.View?.JumpCamera();
        }

        private void HailCurrentShip() {
            if (currentEnemy == null || currentEnemy.IsDerelict) {
                UISounds.PlayActionDenied();
                return;
            }
            SOActionHail.HailShip(S, currentEnemy);
        }

        public void SetColors() {
            var fillColor = The.UITheme.GetColor("ShipCombatFluxSoft", "ShipCombatMini");
            var fill2Color = The.UITheme.GetColor("ShipCombatFluxHard", "ShipCombatMini");
            var shieldColor = The.UITheme.GetColor("ShipCombatShields", "ShipCombatMini");
            // weaponColor = The.UITheme.GetColor("ShipCombatWeapon", "ShipCombatMini");
            // weaponInactiveColor = The.UITheme.GetColor("ShipCombatWeaponInactive", "ShipCombatMini");
            var hpColor = The.UITheme.GetColor("ShipCombatHP", "ShipCombatMini");

            wProgressFlux.Progress.SetFillColor(fillColor);
            wProgressFlux.SetStackedColor(fill2Color);
            wProgressHP.SetFillColor(hpColor);
            wProgressShield.SetFillColor(shieldColor);
            if (currentEnemy != null) {
                RebuildWeapons(currentEnemy);
            }
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
            lifetime = Lifetime.Create("ShipCombatTheir");
            wTextWeaponsHeader.Load(T.MenuWeapons);
            wButtonNextEnemy.Load(NextEnemy);
            wButtonPrevEnemy.Load(PrevEnemy);
            var hailBtn = UISpawn.Widget<WBottomBarButton>(lifetime,
                UIPrefabId.WBottomBarButton, wButtonGroup.Container, 0);
            hailBtn.Load(IconId.CCallOngoing, T.VerbHail, HailCurrentShip);
            S.Sig.ShipDevicePhaseChanged.AddListener(OnDevicePhaseChanged, gameObject);
            S.Sig.ShipViewVisibilityChanged.AddListener(OnShipViewVisibilityChanged, gameObject);
            S.Sig.ShipCombatEngagedWith.AddListener(OnShipCombatEngagedWith, gameObject);
            S.Sig.ShipCombatAllyChanged.AddListener(OnShipCombatAllyChanged, gameObject);
            var ships = RebuildShownShips();
            if (ships.Count > 0) {
                var focusedEnemy = S.Universe.Player.Ship.CombatState.FocusedEnemy;
                SwitchToEnemy(focusedEnemy != null && focusedEnemy.IsInCombat
                    && ships.Contains(focusedEnemy) ? focusedEnemy : ships[0]);
            } else {
                wPIPGameObject?.SetActiveFast(false);
            }
        }

        private List<NPCShip> RebuildShownShips() {
            shownShips.Clear();
            shownShips.AddRange(S.Query.GetEnemyShips.Ask());
            foreach (var ally in S.Query.GetAllyShips.Ask()) {
                if (ally.IsPlayer) { continue; }
                shownShips.Add(ally);
            }
            return shownShips;
        }

        private void OnShipCombatEngagedWith(NPCShip ship, bool isInCombat) {
            OnCombatRosterChanged(ship, isInCombat);
        }

        private void OnShipCombatAllyChanged(NPCShip ship, bool isAlly) {
            OnCombatRosterChanged(ship, isAlly);
        }

        private void OnCombatRosterChanged(NPCShip ship, bool joined) {
            var ships = RebuildShownShips();
            if (UpdateEnemySwitchButtons(ships)) {
                RebuildLayout();
            }
            if (joined || ship != currentEnemy) { return; }
            if (ships.Count > 0) {
                SwitchToEnemy(ships[0]);
            }
        }

        private void NextEnemy() {
            var ships = RebuildShownShips();
            if (ships.Count < 2) { return; }
            var idx = ships.IndexOf(currentEnemy);
            SwitchToEnemy(ships[(idx + 1) % ships.Count]);
        }

        private void PrevEnemy() {
            var ships = RebuildShownShips();
            if (ships.Count < 2) { return; }
            var idx = ships.IndexOf(currentEnemy);
            SwitchToEnemy(ships[(idx - 1 + ships.Count) % ships.Count]);
        }

        public void SwitchToEnemy(NPCShip enemy) {
            if (enemyLifetime != null) {
                enemyLifetime.Dispose();
                enemyLifetime = null;
                weaponsLifetime = null;
            }
            hasFluxVentStats = false;
            currentEnemy = enemy;
            var isAlly = enemy.IsCombatAlly;
            // Keep combat AI focused on the enemy the player is looking at. Looking at
            // an ally must not disarm the player, so the last enemy stays focused.
            if (!isAlly) {
                var playerCS = S.Universe.Player.Ship.CombatState;
                playerCS.FocusedEnemy = enemy;
                ShipCombatAI.RefocusWeapons(playerCS, enemy);
            }
            enemyLifetime = lifetime.CreateChild("ShipCombatTheir.enemy");
            weaponRows.Clear();

            UpdateEnemySwitchButtons(RebuildShownShips());

            wTextHeader.Load(isAlly
                ? $"{enemy.NameFull} {Texts.Green($"[{T.NounAlly}]")}"
                : enemy.NameFull);
            UIUtils.AddTipFunc(enemyLifetime, wTextHeader, () => currentEnemy?.FullTooltip(true));

            UIUtils.AddTipFunc(enemyLifetime, wProgressFlux, () => GetFluxTip(currentEnemy));
            var flux = enemy.CombatState.Flux;
            flux.OnChanged.Bind(enemyLifetime, () => {
                UpdateFlux(currentEnemy);
                UpdateFluxVents(currentEnemy);
            });
            UpdateFlux(enemy);

            enemy.CombatState.OnHPChanged.Bind(enemyLifetime, () => UpdateHP(currentEnemy));
            UpdateHP(enemy);

            UIUtils.AddTipFunc(enemyLifetime, wTextClassPoints, () => ShipCombatWidgetUtils.GetClassPointsTip(currentEnemy));
            UIUtils.AddTipFunc(enemyLifetime, wTextThrusters, () => ShipCombatWidgetUtils.GetThrustersTip(currentEnemy));
            UIUtils.AddTipFunc(enemyLifetime, wTextFluxCapacitors,
                () => ShipCombatWidgetUtils.GetFluxCapacitorsTip(currentEnemy));
            UIUtils.AddTipFunc(enemyLifetime, wTextFluxVents, () => ShipCombatWidgetUtils.GetFluxVentsTip(currentEnemy));

            UpdateClassPoints(enemy);
            UpdateShield(enemy);
            UpdateThrusters(enemy);
            UpdateCapacitors(enemy);
            RebuildWeapons(enemy);
            SetupPIP(enemy);
            RebuildLayout();
        }

        private bool UpdateEnemySwitchButtons(List<NPCShip> ships) {
            var show = ships.Count > 1;
            var changed = wButtonPrevEnemy.gameObject.activeSelf != show
                || wButtonNextEnemy.gameObject.activeSelf != show;
            wButtonPrevEnemy.SetActiveFast(show);
            wButtonNextEnemy.SetActiveFast(show);
            return changed;
        }

        private void SetupPIP(NPCShip enemy) {
            if (pip == null) {
                var prefab = ResourceLibrary.FindPrefab<PIPCamera>();
                pip = PrefabPool.Spawn(prefab, ResourceLibrary.Root);
                pip.SetAspectRatio(wEnemyShipPIP.Rect.rect.width / wEnemyShipPIP.Rect.rect.height);
            }
            var view = enemy.View;
            showPIP = !view.IsVisible;
            // subject first: it decides which views belong on the PIP layer, and the
            // activation below is what makes them move there
            pip.SetSubject(S, enemy);
            pip.SetActive(S, showPIP);
            pip.MoveGameCam(view.transform.position + new Vector3(0, 0, -200),
                view.Ship.Width * 0.7f);
            pip.MoveStarfieldCam(view.Slot.Position * 0.25f);
            UpdatePIPDisplay();
        }

        private void UpdatePIPDisplay() {
            if (wPIPGameObject == null) { return; }
            wPIPGameObject.SetActiveFast(showPIP && pip?.Texture != null);
            if (showPIP && pip?.Texture != null) {
                wEnemyShipPIP.Load(pip.Texture);
            }
        }

        private void OnShipViewVisibilityChanged(ShipView view, bool isVisible) {
            if (currentEnemy == null || view.Ship != currentEnemy) { return; }
            showPIP = !isVisible;
            pip?.SetActive(S, showPIP);
            UpdatePIPDisplay();
        }

        public void OnTick() {
            if (currentEnemy == null) { return; }
            var shields = currentEnemy.CombatState.Shields;
            for (int i = shields.Count - 1; i >= 0; i--) {
                if (shields[i].Phase == ShipShieldPhase.Charging) {
                    UpdateShield(currentEnemy);
                    return;
                }
            }
        }

        private void OnDevicePhaseChanged(IShipDevice device) {
            if (currentEnemy == null || device.Device.Ship != currentEnemy) { return; }
            if (device is ShipCapacitor) {
                UpdateCapacitors(currentEnemy);
            } else if (device is ShipShield) {
                UpdateShield(currentEnemy);
            } else if (device is ShipEngine) {
                UpdateThrusters(currentEnemy);
            } else if (device is ShipVent) {
                UpdateFluxVents(currentEnemy);
            } else if (device is ShipWeapon) {
                var groupCount = currentEnemy.CombatState.Weapons
                    .Where(w => !w.IsPointDefense)
                    .Select(w => w.WeaponDef.Def.NameT)
                    .Distinct()
                    .Count();
                if (groupCount != weaponRows.Count) {
                    RebuildWeapons(currentEnemy);
                } else {
                    UpdateWeaponRows(currentEnemy);
                }
            }
            UpdateClassPoints(currentEnemy);
        }

        private void UpdateFlux(NPCShip ship) {
            var flux = ship.CombatState.Flux;
            var fluxLabel = flux.State == ShipFluxState.Overload ? T.FluxOverload : T.NounFlux;
            wProgressFlux.Progress.Load(fluxLabel,
                Units.XOutOfY(flux.Hard + flux.Soft, flux.Capacity), flux.TotalPercent);
            wProgressFlux.SetStackedProgress(flux.HardPercent);
        }

        private void UpdateHP(NPCShip ship) {
            var hp = ship.CombatState.HP;
            var maxHP = ship.CombatState.MaxHP;
            var pct = maxHP > 0 ? hp / maxHP : 0f;
            wProgressHP.Load(T.NounHullPointsShort, Units.XOutOfY((int) hp, maxHP), pct);
        }

        private void UpdateClassPoints(NPCShip ship) {
            var cp = ship.CombatState.CurrentClassPoints;
            var mp = ship.Class.MaxClassPoints;
            var cpName = cp > mp ? Texts.Red(T.NounClassPoints) : T.NounClassPoints;
            wTextClassPoints.Load(cpName, Units.XOutOfY(cp, mp));
        }

        private void UpdateShield(NPCShip ship) {
            var shields = ship.CombatState.Shields;
            var total = shields.Count;
            if (total == 0) {
                wProgressShield.gameObject.SetActiveFast(false);
                return;
            }
            wProgressShield.gameObject.SetActiveFast(true);
            int up = 0;
            float bestChargePct = 0f;
            for (int i = shields.Count - 1; i >= 0; i--) {
                var s = shields[i];
                if (s.Phase == ShipShieldPhase.Active) {
                    up++;
                } else if (s.Phase == ShipShieldPhase.Charging && s.PhasePercent > bestChargePct) {
                    bestChargePct = s.PhasePercent;
                }
            }
            float pct;
            string text;
            if (up > 0) {
                pct = up / (float) total;
                text = up == total ? T.Active : Units.XOutOfY(up, total);
            } else if (bestChargePct > 0f) {
                pct = bestChargePct;
                text = Units.Percentage(bestChargePct);
            } else {
                pct = 0f;
                text = Units.XOutOfY(0, total);
            }
            wProgressShield.Load(T.NounShields, text, pct);
        }

        private void UpdateThrusters(NPCShip ship) {
            var thrCnt = ship.CombatState.NumThrustersActiveTotal;
            wTextThrusters.Load(
                $"{T.NounThrusters}: {Units.XOutOfY(thrCnt.Item1, thrCnt.Item2)}",
                $"{T.NounEvade}: {Units.Percentage(ship.CombatState.EvasionPercent)}");
        }

        private void UpdateCapacitors(NPCShip ship) {
            var stats = ShipCombatWidgetUtils.GetFluxCapacitorStats(ship);
            if (stats.Total == 0) {
                wTextFluxCapacitors.gameObject.SetActiveFast(false);
                return;
            }
            wTextFluxCapacitors.gameObject.SetActiveFast(true);
            wTextFluxCapacitors.Load(
                $"{T.NounFluxCapacitors}: {Units.XOutOfY(stats.Active, stats.Total)}",
                ShipCombatWidgetUtils.GetFluxCapacitorBonusText(stats));
        }

        private void UpdateFluxVents(NPCShip ship) {
            var stats = ShipCombatWidgetUtils.GetFluxVentStats(ship);
            if (hasFluxVentStats
                    && ShipCombatWidgetUtils.SameFluxVentStats(stats, fluxVentStats)) {
                return;
            }
            hasFluxVentStats = true;
            fluxVentStats = stats;
            if (stats.Total == 0) {
                wTextFluxVents.gameObject.SetActiveFast(false);
                return;
            }
            wTextFluxVents.gameObject.SetActiveFast(true);
            wTextFluxVents.Load(
                $"{T.NounFluxVents}: {Units.XOutOfY(stats.Active, stats.Total)}",
                ShipCombatWidgetUtils.GetFluxVentBonusText(stats));
        }

        private void RebuildWeapons(NPCShip ship) {
            weaponRows.Clear();
            weaponsLifetime?.Dispose();
            weaponsLifetime = enemyLifetime.CreateChild("ShipCombatTheir.weapons");
            int idx = 0;
            wTextWeaponsHeader.SetSiblingIdx(idx++);
            foreach (var group in ship.CombatState.Weapons
                    .Where(w => !w.IsPointDefense)
                    .GroupBy(w => w.WeaponDef.Def.NameT)
                    .OrderBy(g => g.Key)) {
                var row = UISpawn.Widget<WProgressText2>(weaponsLifetime,
                    UIPrefabId.WProgressBarText2Narrow, wContainerWeapons.Container, idx++);
                weaponRows.Add(row);
                var captured = group.ToList();
                UIUtils.AddTipFunc(weaponsLifetime, row, () => GetWeaponGroupTip(captured));
                LoadWeaponRow(row, group.Key, captured);
            }
        }

        private void UpdateWeaponRows(NPCShip ship) {
            var groups = ship.CombatState.Weapons
                .Where(w => !w.IsPointDefense)
                .GroupBy(w => w.WeaponDef.Def.NameT)
                .OrderBy(g => g.Key)
                .ToList();
            for (int i = 0; i < Maths.Min(weaponRows.Count, groups.Count); i++) {
                LoadWeaponRow(weaponRows[i], groups[i].Key, groups[i].ToList());
            }
        }

        private void LoadWeaponRow(WProgressText2 row, string name, List<ShipWeapon> weapons) {
            int active = 0;
            foreach (var w in weapons) {
                if (w.IsActive) { active++; }
            }
            int total = weapons.Count;
            row.Load(name, Units.XOutOfY(active, total), total > 0 ? active / (float) total : 0f);
        }

        private string GetWeaponGroupTip(List<ShipWeapon> weapons) {
            if (weapons.Count == 0) { return string.Empty; }
            var def = weapons[0].WeaponDef;
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.SubTitle(def.Def.NameT));
            sb.AppendLine(def.Def.DescT);
            sb.AppendLine();
            sb.AppendFormat("{0}: {1}\n", T.NounDamage, Texts.Accented(Units.Num(def.Damage)));
            sb.AppendFormat("{0}: {1}\n", T.NounWeaponPowerShort,
                Texts.Accented(Units.Num(def.CombatStrength)));
            return sb.ToString();
        }

        private string GetFluxTip(NPCShip ship) {
            return ShipCombatWidgetUtils.GetFluxTip(ship, includeSettings: true);
        }

        private void Cleanup() {
            if (lifetime == null) { return; }
            S.Sig.ShipDevicePhaseChanged.RemoveListener(OnDevicePhaseChanged);
            S.Sig.ShipViewVisibilityChanged.RemoveListener(OnShipViewVisibilityChanged);
            S.Sig.ShipCombatEngagedWith.RemoveListener(OnShipCombatEngagedWith);
            S.Sig.ShipCombatAllyChanged.RemoveListener(OnShipCombatAllyChanged);
            if (pip != null) {
                pip.Cleanup();
                PrefabPool.Despawn(pip.gameObject);
                pip = null;
            }
            enemyLifetime = null;
            weaponsLifetime = null;
            lifetime.Dispose();
            lifetime = null;
            currentEnemy = null;
            hasFluxVentStats = false;
            weaponRows.Clear();
            shownShips.Clear();
        }
    }
}
