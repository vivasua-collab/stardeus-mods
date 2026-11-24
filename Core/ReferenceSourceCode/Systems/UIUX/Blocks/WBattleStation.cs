using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBattleStation : WBase, ICustomPoolable {
        [SerializeField] private WBeingAvatar wAvatar;
        [SerializeField] private WImage wImgAvatarPlaceholder;
        [SerializeField] private WProgressText wProgressCharge;
        [SerializeField] private WButtonTextLongPress wButtonFire;
        [SerializeField] private WButtonIcon wButtonWeapon;
        [SerializeField] private WText wTextWeaponCount;
        [SerializeField] private UIHoverCallback weaponHover;
        [SerializeField] private GameObject wButtonFireCorners;
        [NonSerialized] public BattleStationComp Comp;
        public bool IsFireButtonHeldDown => wButtonFire != null && wButtonFire.IsHeldDown;
        private int shortcutIndex;
        private bool shortcutWasVirtualMouse;
        private bool shortcutHadKeyboard;
        private string fireTextWithShortcut;
        private bool lastFireWarnOverload;
        private string cachedFireLabelBase;
        private string cachedFireLabel;

        public void Load(BattleStationComp comp, Lifetime parentLife, int shortcutIndex = 0) {
            this.Comp = comp;
            this.shortcutIndex = shortcutIndex;
            fireTextWithShortcut = null;
            cachedFireLabelBase = null;
            cachedFireLabel = null;
            weaponHover.SetCallback(OnHoverWeaponIcon, null, true);
            wButtonFire.Load(null, Tunables.ShipCombatManualFireHoldDuration, OnClickFire, OnHoldFire,
                ignorePointerExit: true);
            // UIUtils.AddTipFunc(parentLife, wButtonFire,
            //     () => ShipCombatWidgetUtils.GetManualFireTip(comp));
            UIUtils.AddTipFunc(parentLife, this,
                () => ShipCombatWidgetUtils.GetManualFireTip(comp));
            // wImgAvatarPlaceholder.Load(IconId.CBodyGeneric);
            wAvatar.SetActiveFast(false);
            comp.AssignedWeapons.Bind(parentLife, (cnt) => {
                wTextWeaponCount.Load(Units.XOutOfY(cnt, comp.TotalWeaponCapacity));
                if (cnt == 0 && comp.WeaponDef != null) {
                    wImgAvatarPlaceholder.Load(IconId.CWarning);
                } else {
                    wImgAvatarPlaceholder.Load(IconId.CBodyGeneric);
                }
            });
            comp.Operatable.OperatorB.Bind(parentLife, (op) => {
                var isManualMode = comp.EffectiveManualFireMode;
                wAvatar.SetActiveFast(op != null);
                wImgAvatarPlaceholder.SetActiveFast(op == null);
                wButtonFireCorners.SetActiveFast(op == null || isManualMode);
                if (op != null) {
                    if (!wButtonFire.IsHeldDown && !isManualMode) {
                        wButtonFire.SetActiveFast(false);
                    }
                    wAvatar.Load(parentLife, A.S, op.Data, isSecondary: true);
                    wProgressCharge.SetText(isManualMode ? FireText() : T.Auto);
                } else {
                    wButtonFire.SetActiveFast(true);
                    wProgressCharge.SetText(FireText());
                }
            });
            comp.WeaponDefB.Bind(parentLife, (def) => {
                if (def != null) {
                    wButtonWeapon.Load(def.Def.Preview, OnClickStation);
                } else {
                    wButtonWeapon.Load(IconId.CNotConfigured, OnClickStation);
                }
            });
            UpdateState(0, 0, 0, 0, 0);
        }

        private string FireText() {
            var isVirtualMouse = VirtualMouseInput.IsCurrent;
            var hasKeyboard = Keyboard.current != null;
            if (fireTextWithShortcut != null
                    && shortcutWasVirtualMouse == isVirtualMouse
                    && shortcutHadKeyboard == hasKeyboard) {
                return fireTextWithShortcut;
            }
            shortcutWasVirtualMouse = isVirtualMouse;
            shortcutHadKeyboard = hasKeyboard;
            if (shortcutIndex <= 0 || shortcutIndex > 10 || isVirtualMouse || !hasKeyboard) {
                fireTextWithShortcut = T.VerbFireWeaponShort;
            } else {
                fireTextWithShortcut = $"{GlyphMap.Sprite("Keyboard", $"f{shortcutIndex}")} {T.VerbFireWeaponShort}";
            }
            return fireTextWithShortcut;
        }

        private string FireLabel() {
            var baseText = FireText();
            var warn = Comp.GetManualFireFluxPreview(out _, out var willOverload) && willOverload;
            if (cachedFireLabel != null
                    && lastFireWarnOverload == warn
                    && ReferenceEquals(cachedFireLabelBase, baseText)) {
                return cachedFireLabel;
            }
            lastFireWarnOverload = warn;
            cachedFireLabelBase = baseText;
            cachedFireLabel = warn ? $"{GlyphMap.SpriteWarning} {baseText}" : baseText;
            return cachedFireLabel;
        }

        private void OnHoldFire(float prog) {
            if (G.ClockSpeed != ClockSpeed.Normal) {
                A.S.Clock.SetSpeed(ClockSpeed.Normal);
            }
        }

        private void OnHoverWeaponIcon(bool on) {
            EntityUtils.ShowEntityLink(null, Comp.Tile, on);
        }

        private void OnClickStation() {
            EntityUtils.SelectOrFocus(Comp.Tile);
        }

        private void OnClickFire() {
            if (!Comp.TryManualFire()) {
                UISounds.PlayActionDenied();
            } else {
                wButtonFire.SetActiveFast(false);
                UISounds.PlayDigitalConfirm();
            }
        }

        public void SimulateFireHold(bool isDown) {
            wButtonFire.SimulateHold(isDown);
        }

        public void ResetSimulatedFireHold() {
            wButtonFire.ResetSimulatedHold();
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (Comp == null) { return; }
            EntityUtils.ShowEntityLink(null, Comp.Tile, true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (Comp == null) { return; }
            EntityUtils.HideEntityLink();
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            Comp = null;
            gameObject.SetActiveFast(false);
        }

        private static string ventOpenLabel;

        public void UpdateState(int ready, int firing, float chargingPct, float windingPct, int total) {
            if (Comp == null) {
                D.Err("Trying to update WBattleStation state with no comp: {0}",
                    D.GOPath(transform));
                return;
            }
            var hasOperator = Comp.IsOperatorPresent && !Comp.EffectiveManualFireMode;
            string chargeText;
            var canFire = false;
            // Weapons hold fire at ships while the vent is open (ShipWeapon.IsHeldByVent)
            var ship = A.Player.Ship;
            if (total > 0 && ship.IsInCombat
                    && ship.CombatState.Flux.State == ShipFluxState.Venting) {
                chargeText = ventOpenLabel ??= Texts.Yellow(T.VentOpen);
            } else if (hasOperator) {
                chargeText = Texts.Blue(T.Auto);
            } else {
                if (total == 0) {
                    chargeText = Texts.Grey(T.Idle);
                } else if (ready >= total) {
                    chargeText = FireLabel();
                    canFire = true;
                } else if (firing > 0f) {
                    chargeText = T.VerbWeaponsFiringShort;
                } else if (windingPct > 0f) {
                    chargeText = Texts.Grey(T.VerbWindingUpShort);
                } else if (chargingPct > 0f) {
                    chargeText = Texts.Grey(T.BatteryStateCharging);
                } else {
                    chargeText = Texts.Red(T.Idle);
                }
            }
            if (canFire || !wButtonFire.IsHeldDown) {
                wButtonFire.SetActiveFast(canFire);
            }
            float prog;
            if (total > 0 && !hasOperator) {
                if (ready > 0) {
                    prog = ready / (float) total;
                } else if (firing > 0) {
                    prog = firing / (float) total;
                } else if (windingPct > 0f) {
                    prog = windingPct;
                } else if (chargingPct > 0) {
                    prog = chargingPct;
                } else {
                    prog = 0f;
                }
            } else {
                prog = 0f;
            }
            wProgressCharge.Load(chargeText, prog);
        }
    }
}
