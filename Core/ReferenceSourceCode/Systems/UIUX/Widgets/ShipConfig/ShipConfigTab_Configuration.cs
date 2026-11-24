using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipConfigTab_Configuration {
        private readonly ShipConfigWidget parent;
        private readonly GameState S;
        private Lifetime lifetime;
        private Lifetime weaponLife;
        private bool weaponExpanded;
        private readonly HashSet<string> expandedTargetGroups = new();
        private readonly BCallback fluxChangedB = new();

        private List<string> _forwardDirOpts;
        private List<string> forwardDirOpts => _forwardDirOpts ??= new() {
            $"{T.ShipDirection}: {T.Auto}",
            $"{T.ShipDirection}: {T.DirectionSouth}",
            $"{T.ShipDirection}: {T.DirectionWest}",
            $"{T.ShipDirection}: {T.DirectionNorth}",
            $"{T.ShipDirection}: {T.DirectionEast}",
        };

        public ShipConfigTab_Configuration(ShipConfigWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public void Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("ShipConfig.Configuration");

            var block = UISpawn.Block(lifetime, parent.WItems, 0);
            int idx = 0;

            AddShipTag(block, idx++);
            AddWeaponSettings(block, idx++);
            AddForwardDirection(block, idx++);
        }

        private void AddShipTag(WContentBlock block, int idx) {
            var ship = A.Player.Ship;
            var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx);
            w.Load(IconId.CBookmark, $"{ship.Tag.Id} ({ship.Tag.DescT})", T.NounShipTag, () => {
                if (ship.IsInCombat) {
                    S.Sig.ShowErrorMessage.Send(T.NotAvailableDuringShipCombat);
                    return;
                }
                ShowTagSubPanel(w);
            });
        }

        private void ShowTagSubPanel(WBase anchor) {
            var subPanel = parent.SubPanel;
            if (subPanel.IsShowing && subPanel.IsShowingFor(anchor)) {
                subPanel.Close();
                return;
            }

            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.NounShipTag);
            subPanel.ShowFooter(false);

            var tagBlock = UISpawn.Block(l, subPanel.Container, 0);
            int idx = 0;

            var curTag = A.Player.Ship.Tag;
            foreach (var tag in ShipTagDef.All.Values.OrderBy(c => c.TagT)) {
                var icon = tag == curTag ? IconId.CBookmark : IconId.CBookmarkInactive;
                var name = tag == curTag ? Texts.Accented(tag.DescT, false) : tag.DescT;

                var tagW = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, tagBlock.Container, idx++);

                if (tag.IsCompatibleWithPlayer(S)) {
                    var capturedTag = tag;
                    tagW.Load(icon, name, capturedTag.TagT, () => {
                        A.Player.Ship.Tag = capturedTag;
                        S.UIQuery.RebuildShipConfig.Ask();
                    });
                } else {
                    tagW.Load(icon, name, Texts.Red(T.NotAvailable), () => {
                        S.Sig.ShowErrorMessage.Send(T.ShipDoesNotMeetRequirements);
                    });
                }

                UIUtils.AddTipFunc(l, tagW, () => tag.Tooltip(A.Player.Ship));
            }

            subPanel.Show(anchor);
        }

        private void AddWeaponSettings(WContentBlock block, int idx) {
            var listBtn = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContent2ListButton, block.Container, idx);
            listBtn.Load(IconId.CAttackTarget, T.NounWeaponSettings, null);
            listBtn.LoadOnToggle(lifetime, (l, container, willShow, wasLoaded) => {
                return OnToggleWeaponSettings(container, willShow, wasLoaded);
            }, weaponExpanded);
        }

        private bool OnToggleWeaponSettings(RectTransform container, bool willShow,
                bool wasLoaded) {
            weaponExpanded = willShow;
            if (!willShow) {
                weaponLife?.Dispose();
                weaponLife = null;
                return false;
            }

            weaponLife?.Dispose();
            weaponLife = lifetime.CreateChild("ShipConfig.Weapon");

            var ws = A.Player.Ship.CombatState.WeaponSettings;
            int idx = 0;

            // Fire Mode toggle
            var fireModeW = UISpawn.Widget<WToggleText>(weaponLife,
                UIPrefabId.WContent2Toggle, container, idx++);
            fireModeW.Load(T.WeaponsAutofire, ws.FireMode == ShipWeaponFireMode.Automatic, isOn => {
                ws.FireMode = isOn ? ShipWeaponFireMode.Automatic : ShipWeaponFireMode.Manual;
                S.UIQuery.RebuildShipConfig.Ask();
                // Notify battle station widgets to rebuild so the Fire button
                // appears/disappears immediately (even when combat is paused).
                var stations = A.Player.Ship.CombatState.BattleStations;
                if (stations.Count > 0) {
                    S.Sig.BattleStationChanged.Send(stations[0]);
                }
            });

            // Autofire Flux Threshold (only when auto fire)
            if (ws.FireMode == ShipWeaponFireMode.Automatic) {
                var fluxW = UISpawn.Widget<WSliderWithIconText2>(weaponLife,
                    UIPrefabId.WContent2Text2Slider, container, idx++);
                fluxW.Item.Load(IconId.CFlux, T.AutofireFluxThreshold);
                fluxW.Slider.Load(ws.ShootFluxThreshold * 100f, v => {
                    ws.ShootFluxThreshold = v * 0.01f;
                    fluxChangedB.Invoke();
                }, 0f, 100f);
                fluxW.Slider.SetValueTextFunc(v => Units.Percentage(v * 0.01f));
                fluxChangedB.Bind(weaponLife, () => {
                    if (IsShootFluxThresholdTooLow(null)) {
                        fluxW.Item.SetText(Texts.Red(T.AutofireFluxThreshold));
                    } else {
                        fluxW.Item.SetText(T.AutofireFluxThreshold);
                    }
                });
                UIUtils.AddTipFunc(weaponLife, fluxW, AutoFireFluxTresholdTip);
            }

            // Auto Target Priorities
            AddAutoTargetPriorities(container, ref idx, ws);

            return true;
        }

        private string AutoFireFluxTresholdTip() {
            bool isTooLow = IsShootFluxThresholdTooLow(null);
            if (isTooLow) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                sb.Append(T.AutofireFluxThresholdDesc);
                sb.Append(GlyphMap.BRHR);
                sb.Append(Texts.Accented(T.Warning));
                sb.Append(": ");
                sb.Append(Texts.Red(T.ValueTooLow));
                sb.AppendFormat("\n{0}: {1}", T.ToleratedFluxHard, Texts.Red(Units.Percentage(
                    A.Player.Ship.CombatState.FluxSettings.ToleratedFluxHard)));
                return sb.ToString();
            }
            return T.AutofireFluxThresholdDesc;
        }
        private bool IsShootFluxThresholdTooLow(ShipWeaponSettings settings) {
            settings ??= A.Player.Ship.CombatState.WeaponSettings;
            var fs = A.Player.Ship.CombatState.FluxSettings;
            var isTooLow = fs.AutoVentFlux && fs.ToleratedFluxHard >= settings.ShootFluxThreshold;
            return isTooLow;
        }

        private void AddAutoTargetPriorities(RectTransform container, ref int idx,
                ShipWeaponSettings ws) {
            var groups = ShipWeaponTargetTypeDef.All.Values
                .OrderBy(v => v.NonShipTarget)
                .ThenBy(v => v.GroupNameT)
                .ThenBy(v => v.GroupId)
                .ThenBy(v => v.NameT)
                .GroupBy(v => v.GroupNameT);

            foreach (var group in groups) {
                var groupName = group.Key;
                var types = group.ToList();

                var listBtn = UISpawn.Widget<WContentListButton>(weaponLife,
                    UIPrefabId.WContent2ListButton, container, idx++);
                listBtn.Load(IconId.CTarget, T.AutoTargetPriorities, groupName);
                listBtn.LoadOnToggle(weaponLife, (l, groupContainer, willShow, wasLoaded) => {
                    if (!willShow) {
                        expandedTargetGroups.Remove(groupName);
                        return wasLoaded;
                    }
                    expandedTargetGroups.Add(groupName);
                    if (wasLoaded) { return true; }

                    int subIdx = 0;
                    foreach (var type in types) {
                        var capturedType = type;
                        int curPrio = ws.GetPriorityFor(type);

                        var prioW = UISpawn.Widget<WSliderWithIconText2>(weaponLife,
                            UIPrefabId.WContent2Text2Slider, groupContainer, subIdx++);
                        prioW.Item.Load(type.Icon, type.NameT,
                            Units.XOutOfY(curPrio, 5));
                        prioW.Slider.Load(curPrio, v => {
                            var p = Mathf.RoundToInt(v);
                            ws.SetPriorityFor(capturedType, p);
                            prioW.Item.SetText2(Units.XOutOfY(p, 5));
                        }, 0f, 5f);
                        prioW.Slider.SetValueTextFunc(
                            v => Units.XOutOfY(Mathf.RoundToInt(v), 5));
                        var tip = capturedType.SkipDuringShipCombat
                            ? $"{T.JobPriorityHint}{GlyphMap.BRHR}{T.ShipWeaponTargetSkippedDuringShipCombat}"
                            : T.JobPriorityHint;
                        UIUtils.AddTip(weaponLife, prioW, tip);
                    }
                    return true;
                }, expandedTargetGroups.Contains(groupName));
            }
        }

        private void AddForwardDirection(WContentBlock block, int idx) {
            var dirW = UISpawn.Widget<WDropdownWithIcon>(lifetime,
                UIPrefabId.WContent2Dropdown, block.Container, idx + 1);
            var nav = S.Sys.ShipNav;
            var curDir = nav.ShipDirection;
            var selected = nav.AutoForwardDirectionEnabled ? 0 : (int) curDir + 1;
            dirW.Icon.Load(Facing.IconFor(curDir));

            if (A.Player.Ship.IsInCombat) {
                dirW.Dropdown.Load(forwardDirOpts, selected, _ => {
                    S.Sig.ShowErrorMessage.Send(T.NotAvailableDuringShipCombat);
                    var current = nav.AutoForwardDirectionEnabled
                        ? 0
                        : (int) nav.ShipDirection + 1;
                    dirW.Dropdown.SetValue(current, false);
                });
            } else {
                dirW.Dropdown.Load(forwardDirOpts, selected, v => {
                    if (v == 0) {
                        nav.SetAutoForwardDirection(true);
                    } else {
                        nav.SetAutoForwardDirection(false);
                        nav.SetForwardDir((Facing.Type) (v - 1));
                    }
                    dirW.Icon.Load(Facing.IconFor(nav.ShipDirection));
                });
            }

            UIUtils.AddTip(lifetime, dirW, T.ShipForwardDirectionTip);
        }

        public void Cleanup() {
            weaponLife?.Dispose();
            weaponLife = null;
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
