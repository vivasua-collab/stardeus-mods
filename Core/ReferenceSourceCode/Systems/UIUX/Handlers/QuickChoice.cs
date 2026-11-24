using System;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class QuickChoice {
        public struct Button {
            public Action OnClick;
            public Func<bool> OnClickValidate;
            public string Tip;
            public Func<string> TipFunc;
            public bool AutoClose;
        }
        /// <summary>
        /// For something like displaying mining depth, mining difficulty
        /// or accumulated mined resource in starmap's SO actions menu
        /// </summary>
        public struct Progress {
            public BFloat ProgressB;
            public B<string> TextB;
            public string Tip;
            public Func<string> TipFunc;
        }
        public struct DynamicTextIcon {
            public Sprite Icon;
            public Color? Tint;
            public B<string> TextB;
            public string Tip;
            public Func<string> TipFunc;
        }
        public struct DynamicText {
            public B<string> TextB;
            public string Tip;
            public Func<string> TipFunc;
        }
        public struct IconButton {
            public Action OnClick;
            public Func<bool> OnClickValidate;
            public Sprite Icon;
            public Color? Tint;
            public string Tip;
            public Func<string> TipFunc;
            public bool AutoClose;
        }
        public struct Toggle {
            public bool Value;
            public string Tip;
            public Func<string> TipFunc;
            public Action<bool> OnToggle;
        }
        public struct Slider {
            public float Value;
            public float Min;
            public float Max;
            public Func<float, string> ValueToText;
            public Action<float> OnValueChanged;
            public string Tip;
            public Sprite Icon;
            public Color? IconTint;
            public Func<string> TipFunc;
        }
        public struct Input {
            public string Placeholder;
            public string Value;
            public string Tip;
            public Func<string> TipFunc;
            public Action<string> OnSubmit;
            public Action<string> OnChange;
            public bool AutoFocus;
        }
        public enum OptionType {
            Label,
            Button,
            IconButton,
            Input,
            Toggle,
            DynamicText,
            DynamicTextIcon,
            Progress,
            Slider,
        }
        public string Label;
        public List<(string name, OptionType type, object action)> Options;
        public List<WBase> SpawnedWidgets;
        public int Count => Options?.Count ?? 0;
        public Action OnUpdate;
        public Action OnClose;
        public RectTransform Widget;
        // Opens with its bottom left on Widget's top left instead of hanging below it, for
        // anchors at the bottom of the screen like the build menu
        public bool AttachAbove;

        private QuickChoice() { }

        public static QuickChoice Create(string label = null, bool collectSpawnedWidgets = false) {
            var qc = new QuickChoice();
            if (collectSpawnedWidgets) {
                qc.SpawnedWidgets = new List<WBase>();
            }
            qc.Label = label;
            return qc;
        }

        public QuickChoice WithLabel(string label) {
            Label = label;
            return this;
        }
        public QuickChoice AddInput(string name, Input input) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Input, input));
            return this;
        }
        public QuickChoice AddDynamicTextIcon(string name, DynamicTextIcon dynText) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.DynamicTextIcon, dynText));
            return this;
        }
        public QuickChoice AddDynamicText(string name, DynamicText dynText) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.DynamicText, dynText));
            return this;
        }
        public QuickChoice AddSlider(string name, Slider slider) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Slider, slider));
            return this;
        }
        public QuickChoice AddProgress(string name, Progress prog) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Progress, prog));
            return this;
        }
        public QuickChoice AddToggle(string name, Toggle toggle) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Toggle, toggle));
            return this;
        }

        public QuickChoice AddLabel(string name) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Label, null));
            return this;
        }

        public QuickChoice AddButton(string name, Button button) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Button, button));
            return this;
        }

        public QuickChoice AddIconButton(string name, IconButton iconButton) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.IconButton, iconButton));
            return this;
        }
        public QuickChoice AddButton(string name, Action action) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Button, new Button() {
                OnClick = action,
                AutoClose = true,
            }));
            return this;
        }
        public QuickChoice AddIconButton(string iconId, string name, Action action, string tip = null) {
            var icon = Images.Sprite(iconId);
            return AddIconButton(icon, name, action, tip);
        }
        public QuickChoice AddIconButton(Sprite icon, string name, Action action, string tip = null) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.IconButton, new IconButton() {
                OnClick = action,
                Icon = icon,
                AutoClose = true,
                Tip = tip,
            }));
            return this;
        }
        public QuickChoice AddButton(string name, Action action, bool autoClose) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Button, new Button() {
                OnClick = action,
                AutoClose = autoClose,
            }));
            return this;
        }
        public QuickChoice AddButton(string name, Action action, string tip) {
            if (Options == null) {
                Options = new();
            }
            Options.Add((name, OptionType.Button, new Button() {
                OnClick = action,
                Tip = tip,
                AutoClose = true,
            }));
            return this;
        }

        public void Clear() {
            Label = null;
            OnUpdate = null;
            Options?.Clear();
        }

        override public string ToString() {
            return $"QC: {Label} ({Count} options)";
        }

        public void Show() {
            A.S.UIQuery.ShowQuickChoice.Ask(this);
        }
        public void Close() {
            A.S.UIQuery.CancelQuickChoice.Ask();
        }
    }
}
