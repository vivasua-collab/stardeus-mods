using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BattleStationTab : BaseEntityTab {
        public const string TabId = "BattleStation";
        private const string SubId = "BattleStationWeaponDef";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BattleStationTab());
        }

        private BattleStationComp bs;
        private GameState S;
        private WBase typeWidget;

        public override string Id => TabId;
        public override int Order => 15;
        public override string TabName => T.NounBattleStation;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<BattleStationComp>(out var b)) { return false; }
            bs = b;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            int gIdx = 0;

            var typeBlock = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var typeHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, typeBlock.Container, idx++);
            typeHeader.Load(T.NounShipWeapon);

            var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, typeBlock.Container, idx++);
            typeWidget = w;
            w.Load(IconId.CAttackTarget, WeaponDefName(bs.WeaponDef),
                Texts.Grey(T.VerbAssign), () => ShowWeaponDefSubPanel(w));

            // Target type preference dropdown
            var targetTypes = ShipWeaponTargetTypeDef.All.Values
                .Where(t => !t.NonShipTarget)
                .OrderBy(t => t.GroupNameT)
                .ThenBy(t => t.GroupId)
                .ThenBy(t => t.NameT)
                .ThenBy(t => t.Id)
                .ToList();
            var targetOpts = new List<string> { T.Auto };
            targetOpts.AddRange(targetTypes.Select(t => t.NameT));

            var targetBlock = UISpawn.Block(lifetime, group, gIdx++);
            int tIdx = 0;
            var targetHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, targetBlock.Container, tIdx++);
            targetHeader.Load(T.NounPreferredTarget);

            var targetDropdown = UISpawn.Widget<WDropdownWithIcon>(lifetime,
                UIPrefabId.WContent2Dropdown, targetBlock.Container, tIdx++);
            var currentPref = bs.PreferredTargetType;
            var selectedIdx = currentPref == null ? 0 : (targetTypes.IndexOf(currentPref) + 1);
            targetDropdown.Icon.Load(currentPref?.Icon ?? IconId.CTarget);
            targetDropdown.Dropdown.Load(targetOpts, selectedIdx, v => {
                if (v == 0) {
                    bs.SetPreferredTargetType(null);
                    targetDropdown.Icon.Load(IconId.CTarget);
                } else {
                    var def = targetTypes[v - 1];
                    bs.SetPreferredTargetType(def);
                    targetDropdown.Icon.Load(def.Icon);
                }
            });
            UIUtils.AddTip(lifetime, targetDropdown, T.NounPreferredTargetTip);

            var statusBlock = UISpawn.Block(lifetime, group, gIdx++);
            int sIdx = 0;

            var opRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, statusBlock.Container, sIdx++);
            if (bs.IsOperatorPresent) {
                var opName = bs.Operatable?.Operator?.Data.Mind.Name.ShortName ?? T.NounOperator;
                opRow.Load(IconId.CCheck, T.NounOperator, opName);
            } else {
                opRow.Load(IconId.CWarning, T.NounOperator, Texts.Grey(T.AdjUnassigned));
            }

            var weaponsRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContent2Progress, statusBlock.Container, sIdx++);
            bs.AssignedWeapons.Bind(lifetime, count => {
                var capacity = bs.TotalWeaponCapacity;
                weaponsRow.Load(IconId.CCount, T.NounWeapons,
                    capacity > 0 ? (float)count / capacity : 0f,
                    Units.XOutOfY(count, capacity));
            });

            if (bs.IsOperatorPresent && bs.OperatorAccuracyBonus != 1f) {
                var bonusRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, statusBlock.Container, sIdx++);
                bonusRow.Load(IconId.CCheck, T.NounOperatorBonus,
                    Units.Percentage(bs.OperatorAccuracyBonus));
            }

            if (SubPanelWidget.IsOpenFor == SubId && typeWidget != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowWeaponDefSubPanel(typeWidget);
                var btn = typeWidget;
                Coroutines.OnNextFrame(() => {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        private void ShowWeaponDefSubPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            if (subPanel.IsShowingFor(forWidget)) {
                subPanel.Close();
                return;
            }
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.NounShipWeapon);

            int bIdx = 0;
            if (bs.WeaponDef != null) {
                var clearBtn = UISpawn.Widget<WButtonText>(l,
                        UIPrefabId.WPanelButton_Red,
                    subPanel.Footer.Container, 0);
                clearBtn.Load(T.VerbUnassign, () => {
                    bs.SetWeaponDef(null);
                    ReloadTab();
                    subPanel.Close();
                });
                subPanel.ShowFooter(true);
            } else {
                subPanel.ShowFooter(false);
            }


            var block = UISpawn.Block(l, subPanel.Container, bIdx++);
            int idx = 0;


            var cs = S.Universe.Player.Ship.CombatState;

            var defCounts = new Dictionary<ShipWeaponDef, int>();
            var defAssCounts = new Dictionary<ShipWeaponDef, int>();
            foreach (var w in cs.Weapons) {
                if (w.IsPointDefense) { continue; }
                defCounts.TryGetValue(w.WeaponDef, out var cnt);
                defCounts[w.WeaponDef] = cnt + 1;
                if (!w.HasNoStation) {
                    defAssCounts.TryGetValue(w.WeaponDef, out var assCnt);
                    defAssCounts[w.WeaponDef] = assCnt + 1;
                }
            }

            bool addedAvailableHeader = false;
            foreach (var kv in defCounts) {
                var def = kv.Key;
                var weaponCount = kv.Value;
                defAssCounts.TryGetValue(def, out var assCnt);
                // fully assigned
                if (assCnt >= weaponCount) {
                    continue;
                }
                if (!addedAvailableHeader) {
                    addedAvailableHeader = true;
                    var header = UISpawn.Widget<WText>(l,
                        UIPrefabId.WContent2Header, block.Container, idx++);
                    header.Load(T.AdjAvailable);
                }
                var capturedDef = def;
                var isSelected = bs.WeaponDef == def;

                var row = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                var icon = isSelected ? IconId.CCheck : IconId.CAttackTarget;
                var label = def.Def.NameT;
                row.Load(icon, label, Units.XOutOfY(assCnt, kv.Value), () => {
                    bs.SetWeaponDef(capturedDef);
                    subPanel.Close();
                    ReloadTab();
                });
                row.SetInteractable(!isSelected);
            }

            bool addedFullAssHeader = false;

            // Fully assigned
            // TODO extract duplicate code from this and loop above
            foreach (var kv in defCounts) {
                var def = kv.Key;
                var weaponCount = kv.Value;
                defAssCounts.TryGetValue(def, out var assCnt);
                // not fully assigned
                if (assCnt < weaponCount) {
                    continue;
                }
                if (!addedFullAssHeader) {
                    if (addedAvailableHeader) {
                        // we need a new block!
                        block = UISpawn.Block(l, subPanel.Container, bIdx++);
                        idx = 0;
                    }
                    addedFullAssHeader = true;
                    var header = UISpawn.Widget<WText>(l,
                        UIPrefabId.WContent2Header, block.Container, idx++);
                    header.Load(T.FullyAssigned);
                }
                var capturedDef = def;
                var isSelected = bs.WeaponDef == def;

                var row = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                var icon = isSelected ? IconId.CCheck : IconId.CAttackTarget;
                var label = def.Def.NameT;
                row.Load(icon, label, Texts.Green(Units.XOutOfY(assCnt, kv.Value)), () => {
                    bs.SetWeaponDef(capturedDef);
                    subPanel.Close();
                    ReloadTab();
                });
                row.SetInteractable(!isSelected);
            }

            subPanel.Show(forWidget, SubId);
        }

        public override bool LoadFooter(WGroup group) {
            var toggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, 0);
            toggle.Load(T.NounManualFire, bs.ManualFireMode, isOn => {
                bs.ManualFireMode = isOn;
                S.Sig.BattleStationChanged.Send(bs);
            });
            UIUtils.AddTip(lifetime, toggle, T.ManualFireTip);
            return true;
        }

        private static string WeaponDefName(ShipWeaponDef def) {
            if (def == null) { return Texts.Grey(T.AdjUnassigned); }
            return def.Def.NameT;
        }

        protected override void Cleanup() {
            typeWidget = null;
            bs = null;
            S = null;
        }
    }
}
