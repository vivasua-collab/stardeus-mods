using System;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    public sealed class OverlayInfo {
        public readonly int Priority;
        public OverlayType Type;
        public readonly string Id;
        public readonly int IdH;
        public readonly string Icon;
        public string ShortcutGlyph;
        public string ShortcutGlyphWithTip;
        public ActionType ShortcutAction;
        public readonly RenderTexture Texture;
        public readonly bool NeedsEnvTooltip;
        public Action<StringBuilder, int> AppendToEnvTooltip;
        private string tooltipText;
        /// <summary>
        /// Don't use this! use IconTooltip, that will include shortcuts
        /// </summary>
        public string TooltipText {
            get => tooltipText ??= $"overlay.tip.{Id}".T();
            set {
                tooltipText = value;
                fullTooltip = null;
            }
        }

        private string AddCtrlTip(string text) {
            if (string.IsNullOrEmpty(text)) { return null;}
            var ctrl = The.Bindings.GetControlName(ActionType.CtrlModifier);
            return $"{Texts.Accented(text)}\n{Texts.Accented2(T.HoldButtonForMoreInfo_V1.F(ctrl))}";
        }
        public bool IsShowing;
        // While on, the scene keeps its lighting and tint under this overlay, because the
        // overlay currently draws nothing that needs a flat scene to read against
        public bool KeepsLights;
        public string IconTooltip;
        private string hint;
        private string fullTooltip;

        public string ShowTooltip() {
            if (hint == null) {
                var tkey = $"tut.basic.controls.step.overlay.{Id.ToLowerInvariant()}";
                hint = tkey.T();
            }
            return fullTooltip ??= $"{Texts.Accented(IconTooltip)}{GlyphMap.BRHR}{hint}";
        }
        public OverlayInfo(OverlayType type, string id, string defaultIcon,
                bool needsEnvTooltip = false) {
            Id = id;
            IdH = Hashes.S(id);
            Type = type;
            if (type != OverlayType.Hidden) {
                var priorityKey = $"OverlayPriority_{id}";
                if (!Tunable.TryInt(priorityKey, out Priority)) {
                    D.Err("Overlay priority is not defined in UIOverlays.json: {0}",
                        priorityKey);
                    Priority = 1000;
                }
                if (!Tunable.TryString($"OverlayIcon_{id}", out Icon)) {
                    Icon = defaultIcon;
                }
            }
            NeedsEnvTooltip = needsEnvTooltip;
        }

        public OverlayInfo(OverlayType type, string id, string icon,
                ref RenderTexture texture, bool needsEnvTooltip = false)
                    : this(type, id, icon, needsEnvTooltip) {
            Texture = texture;
        }

        public OverlayInfo WithEnvTooltip(Action<StringBuilder, int> envTip) {
            AppendToEnvTooltip = envTip;
            return this;
        }


        public OverlayInfo WithTooltipText(string text) {
            tooltipText = text;
            return this;
        }
        public OverlayInfo WithShortcutAction(ActionType act) {
            ShortcutAction = act;
            return this;
        }
    }
}
