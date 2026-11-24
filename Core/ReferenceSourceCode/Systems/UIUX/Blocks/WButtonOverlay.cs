using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class WButtonOverlay : WButtonIconText {
        [SerializeField] private string activeIconColorId;
        private Image parentIcon;
        private Sprite parentSpriteInitial;
        private GameState S;
        private OverlayInfo overlay;
        private bool isActive;
        public void Load(GameState s, OverlayInfo overlay, Image parentIcon, int idx) {
            if (parentIcon == null) {
                D.Err("Parent icon null! {0}", this);
            }
            S = s;
            this.overlay = overlay;
            this.parentIcon = parentIcon;
            SetGlyph(overlay, idx);
            var nameWithGlyph = overlay.TooltipText;
            if (overlay.ShortcutGlyph != null) {
                nameWithGlyph = $"{overlay.ShortcutGlyph} {nameWithGlyph}";
            }
            Load(overlay.Icon, nameWithGlyph, ToggleOverlay);
            s.Sig.ToggleOverlay.AddListener(OnToggleOverlay, gameObject);
            parentSpriteInitial = parentIcon.sprite;
        }
        public void SetGlyph(OverlayInfo info, int shortcut) {
            // D.Warn("Loading overlay with shortcut: {0}: {1}", shortcut, info.Id);
            if (info.ShortcutGlyph == null) {
                if (info.ShortcutAction != ActionType.None) {
                    info.ShortcutGlyph = The.Bindings.GetBinding(
                        info.ShortcutAction).AllControlGlyphs(out var _);
                    info.ShortcutGlyphWithTip = The.Bindings.GetControlNameWithTip(
                        info.ShortcutAction);
                } else if (shortcut >= 0) {
                    if (shortcut == 0) {
                        D.Err("Shortcut set to 0 for overlay: {0}", info.Id);
                        shortcut = 10;
                    }
                    if (!VirtualMouseInput.IsCurrent) {
                        // don't show F11+ shortcuts
                        if (shortcut < 10 && Keyboard.current != null) {
                            info.ShortcutGlyph = GlyphMap.Sprite("Keyboard", $"f{shortcut}");
                            var tip = $"{Keyboard.current.displayName}: F{shortcut}";
                            tip = Texts.ToBase64Url(tip);
                            info.ShortcutGlyphWithTip = $"<nobr><link=\"tip:{tip}\">{info.ShortcutGlyph}</link></nobr>";
                        }
                    }
                }
            }
            if (info.ShortcutGlyph == null) {
                info.IconTooltip = info.TooltipText;
            } else {
                info.IconTooltip = string.Format("{0}{1}",
                    info.TooltipText, info.ShortcutGlyph);
            }
        }

        private void OnToggleOverlay(OverlayInfo ov, bool on) {
            var isThis = ov == overlay;
            if (on) {
                if (isThis) {
                    if (!isActive) {
                        isActive = true;
                        SetTint(The.UITheme.GetColor(activeIconColorId, this));
                        parentIcon.sprite = image.sprite;
                        parentIcon.color = image.color;
                    }
                } else {
                    Deactivate();
                }
            } else {
                if (isThis) {
                    Deactivate();
                }
            }
        }

        private void ToggleOverlay() {
            S.Sig.ToggleOverlay.Send(overlay, !overlay.IsShowing);
        }

        private void Deactivate() {
            if (isActive) {
                isActive = false;
                SetTint(Consts.ColorWhite);
                if (parentIcon.sprite == image.sprite) {
                    parentIcon.sprite = parentSpriteInitial;
                    parentIcon.color = Consts.ColorWhite;
                }
            }
        }

        public override string ToString() {
            return $"WButtonOverlay[{overlay.Id}]";
        }
    }
}
