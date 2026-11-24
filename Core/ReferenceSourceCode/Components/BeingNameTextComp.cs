#if UNITY_EDITOR
// #define DEBUG_AI
// #define DEBUG_COMBAT
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Pool;
using KL.Text;
using KL.Utils;
using UnityEngine;
using Game.Input;
using System;

namespace Game.Components {
    public sealed class BeingNameTextComp : BaseComponent<BeingNameTextComp>,
            ITickProgressListener {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingNameTextComp());
        }

        private Vector2 offset;
        private GameTextTMP nameText;
        private GameTextTMP sayText;
        private double sayTimeout;
        private string cachedName;
        private Vector2 sayOffset;
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            TickProgress(0f);
        }
        private string decoratedName {
            get {
                if (cachedName == null) {
                    string nameWithColor;
                    if (Being.IsDead) {
                        nameWithColor = currentName;
                    } else if (Being.AIAgent.DirectControl.IsUnderDirectControl) {
                        nameWithColor = Texts.Underscore(currentName);
                    } else {
                        nameWithColor = currentName;
                    }
                    if (color.a > 0f && color != Consts.ColorWhite) {
                        nameWithColor = Texts.TryWithColor(nameWithColor, Colors.ToHex(color, '#'));
                    }

                    if (string.IsNullOrWhiteSpace(glyph)
                            // Don't show glyphs when immersive icons are enabled
                            || The.Prefs.GetBool(Pref.GImmersiveIcons, PrefH.GImmersiveIcons, false)) {
                        cachedName = nameWithColor;
                    } else {
                        cachedName = $"{GlyphMap.Sprite("IconsColor", glyph)} {nameWithColor}";
                    }
                }
                return cachedName;
            }
        }
        private string currentName;
        private string glyph;
        private Color color;

        public void Hide() {
            nameText?.Destroy();
            nameText = null;
        }

        private bool wantsShowName;
        private bool wantsHideName;
        private bool isSubscribed;

        public void Show() {
            SetName(currentName);
        }

        public void SetGlyph(string glyph) {
            if (glyph != this.glyph || cachedName == null) {
                this.glyph = glyph;
                cachedName = null;
                if (nameText != null) {
                    Show();
                }
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.BeingsLoaded, CheckState);
            } else {
                Receive(this, MsgIdH.EntityDeselected);
            }
        }

        public void CheckState() {
            if (!Being.HasData) { return; }
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.BeingNameFontSizeChanged.AddListener(OnFontSizeChanged);
            }
            var body = Being.Data.Body;
            if (Being.IsDead) {
                SetColor(Texts.TextColorGrey);
                SetGlyph("Skull");
                return;
            }

            // Shared with the being lists (DecoratedBeingName), so both mark the same beings
            if (BeingUtils.ReadsAsCrew(Being.Data)) {
                SetColor(Texts.TextColorWhite);
                if (body.CheckWounds(out var canHeal, out var isCrippled)) {
                    if (canHeal) {
                        SetGlyph("Blood");
                    } else if (isCrippled) {
                        SetGlyph("Crutch");
                    }
                    return;
                }
            } else {
                var eFac = Being.Data.EffectiveFaction;
                // D.Warn("Effective faction: {0} -> {1} ({2})", Being, eFac, fac);
                if (eFac.IsPlayer) {
                    SetColor(Texts.TextColorWhite);
                } else if (Being.Data.IsGuest) {
                    SetColor(Texts.TextColorGreen);
                } else {
                    SetColor(Texts.TextColorRed);
                }
                if (!eFac.IsInternal) {
                    SetGlyph($"Faction_{eFac.Id}");
                    return;
                }
            }
            SetGlyph(null);
            if (nameText != null) {
                Show();
            }
        }

        private void OnFontSizeChanged(float size) {
            if (nameText == null) { return; }
            // D.Warn("Font size: {0} (ortho: {1})", size, RenderUtils.CameraOrtho);
            nameText.SetFontSize(size);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.EntitySelected) {
                if (The.Prefs.GetBool(Pref.GShowOnlySelectedNames, PrefH.GShowOnlySelectedNames, false)) {
                    wantsShowName = true;
                    wantsHideName = false;
                    if (G.ClockSpeed == ClockSpeed.Stopped) {
                        TickProgress(0f);
                    }
                }
            } else if (message == MsgIdH.EntityDeselected) {
                if (The.Prefs.GetBool(Pref.GShowOnlySelectedNames, PrefH.GShowOnlySelectedNames, false)) {
                    wantsShowName = false;
                    wantsHideName = true;
                    if (G.ClockSpeed == ClockSpeed.Stopped) {
                        TickProgress(0f);
                    }
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                SetGlyph("Skull");
            } else if (message == MsgIdH.FactionChanged) {
                CheckState();
            } else if (message == MsgIdH.BodyConditionChanged || message == MsgIdH.BeingFinishedLoading) {
                CheckState();
            } else if (message == MsgIdH.DirectControlOn || message == MsgIdH.DirectControlOff) {
                if (nameText != null) {
                    cachedName = null;
                    Show();
                }
            }
        }

        public void ShowOnSelectChanged(bool showOnlySelected) {
            if (showOnlySelected) {
                wantsShowName = false;
                wantsHideName = true;
            } else {
                wantsShowName = true;
                wantsHideName = false;
            }
            TickProgress(0f);
        }

        public void SetName(string name) {
            if (name != currentName) {
                cachedName = null;
            }
            currentName = name;
            if (nameText == null) {
                var pos = Being.Motion.Position.Exact + offset;
                nameText = GameTextTMP.Create("BeingNameText", name, pos);
                float fontSize;
                if (RenderUtils.CameraOrtho > 0f) {
                    fontSize = S.Query.GetBeingNameFontSize.Ask();
                } else {
                    fontSize = 2f;
                }
                OnFontSizeChanged(fontSize);
            }
            nameText.SetText(decoratedName);
        }

        public void Debug(string text) {
            if (text == null) {
                nameText.SetText(decoratedName);
            } else {
                nameText.SetText($"{decoratedName}\n{text}");
            }
        }

        public void SetColor(Color color) {
            if (this.color != color) {
                this.color = color;
                cachedName = null;
            }
        }

        protected override void OnConfig() {
            offset = Config.GetVector2(PropertyIdH.Offset, Vector2.zero);
            var offY = Config.GetFloat(PropertyIdH.SpeechOffset, 0.5f);
            sayOffset = new Vector2(0f, offY);
        }

        public override void OnRemove() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig.BeingNameFontSizeChanged.RemoveListener(OnFontSizeChanged);
            }
            nameText?.Destroy();
            nameText = null;
            sayText?.Destroy();
            sayText = null;
            sayTimeout = 0;
        }

        public void TickProgress(float percent) {
            if (wantsHideName && nameText != null) {
                wantsHideName = false;
                Hide();
            } else if (wantsShowName && nameText == null) {
                wantsShowName = false;
                Show();
            }
            var bp = Being.Graphics.Position;
            UpdateSayText(bp);
            if (nameText == null) { return; }
            // get the sprite set from being graphics and query the position
            var pos = new Vector3(bp.x + offset.x, bp.y + offset.y, 0);
            nameText.MoveTo(pos);
            #if DEBUG_COMBAT
            var name = Being.AIAgent.CurrentAction?.ToString() ?? "No Act";
            name += $" LOS: {Being.AIAgent.CombatUnit.HasLineOfSight}";
            name += $" T: {Being.AIAgent.CombatUnit.AttackTarget?.Name ?? "No"}";
            name += $" St: {Being.AIAgent.CombatUnit.StateName}";
            if (Being.AIAgent.CombatUnit.IsSuppressed) {
                name += " Suppr!";
            }
            nameText.SetText(name);
            #endif
            #if DEBUG_AI
            if (Being.AIAgent.CurrentAction != null) {
                nameText.SetText(Being.AIAgent.CurrentAction.ToString());
            } else {
                nameText.SetText("No action");
            }
            #endif
        }

        public override string ToString() {
            return string.Format(" * BeingNameText [{0}]", currentName);
        }

        private void UpdateSayText(Vector2 graphicsPos) {
            if (sayText == null) { return; }
            if (sayTimeout < GameTimer.realtimeSinceStartupAsDouble) {
                sayText.Destroy();
                sayText = null;
            } else {
                sayText.MoveTo(graphicsPos + sayOffset);
            }
        }

        public void Say(string text, float timeoutSeconds) {
            if (sayText == null) {
                sayText = GameTextTMP.Create("OverheadMessage", text,
                    Being.Graphics.Position + sayOffset);
            } else {
                sayText.SetText(text);
            }
            sayTimeout = GameTimer.realtimeSinceStartupAsDouble + timeoutSeconds;
        }

        public void ResetName() {
            if (nameText != null) {
                cachedName = null;
                Show();
            }
        }
    }
}
