using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class BaseSettingsPage {
        private WTabButton btn;
        protected WGroup container;
        protected WGroup footer;
        protected RectTransform contentRect => container.Container;
        private Lifetime parentLifetime;
        protected Lifetime lifetime;

        public virtual bool IsHidden => false;
        protected static void RegisterPage(BaseSettingsPage page) {
            Registries.SettingsPages.Add(page.Id, page);
        }
        public abstract string Id { get; }
        public abstract int Order { get; }
        public abstract string TabName { get; }
        public void SetButton(WTabButton button) {
            btn = button;
        }
        public void ShowTab(Lifetime parentLife, WGroup container, WGroup footer) {
            btn.SetSelected(true);
            this.container = container;
            this.footer = footer;
            CreateLifetime(parentLife);
            LoadTab();
        }

        public virtual void Update() {}

        public void Unload() {
            btn.SetSelected(false);
            UnloadTab();
            container = null;
            footer = null;
        }
        protected void ShowFooter(bool v) {
            footer?.SetActiveFast(v);
        }

        protected void UnloadTab() {
            BeforeUnloadTab();
            // Otherwise global prefs reach disk only on a clean exit, and a crash loses them
            The.SaveGlobalSettings();
            // otherwise Apply button will not get disposed
            footer?.SetActiveFast(true);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        protected void ReloadTab() {
            UnloadTab();
            CreateLifetime(parentLifetime);
            LoadTab();
        }

        public void CreateLifetime(Lifetime parent) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            parentLifetime = parent;
            lifetime = parent.CreateChild("SettingsPage: " + Id);
        }

        protected void AddTip(WBase owner, string tip) {
            if (!string.IsNullOrEmpty(tip)) {
                Tooltip.CreateFor(lifetime, owner, owner.Rect, Texts.Beautify(tip));
            }
        }
        protected string GStr(string prop, string def) {
            return The.Prefs.GetStringAutoHash(prop, def);
        }
        protected void SetGStr(string prop, string val) {
            The.Prefs.SetString(prop, val);
        }

        protected bool GBool(string prop, bool def) {
            return The.Prefs.GetBoolAutoHash(prop, def);
        }
        protected void SetGBool(string prop, bool val) {
            The.Prefs.SetBool(prop, val);
        }
        protected int GInt(string prop, int def) {
            return The.Prefs.GetIntAutoHash(prop, def);
        }
        protected void SetGInt(string prop, int val) {
            The.Prefs.SetInt(prop, val);
        }
        protected float GFloat(string prop, float def) {
            return The.Prefs.GetFloatAutoHash(prop, def);
        }
        protected void SetGFloat(string prop, float val) {
            The.Prefs.SetFloat(prop, val);
        }
        protected WText AddHeader(int idx, string text) {
            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WTabPanelItem_Header, contentRect, idx);
            header.Load(text);
            return header;
        }
        protected WDropdownWithLabel AddDropdown(int idx, string txt, List<string> opts,
                int selected, Action<int> onChange, string tip = null) {
            var w = UISpawn.Widget<WDropdownWithLabel>(lifetime,
                UIPrefabId.WTabPanelItem_Dropdown, contentRect, idx);
            w.Label.Load(txt);
            w.Dropdown.Load(opts, selected, onChange);
            AddTip(w, tip);
            return w;
        }
        protected WButtonText AddButton(int idx, string txt, Action onClick, string tip = null) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WTabPanelItem_Button, contentRect, idx);
            btn.Load(txt, onClick);
            AddTip(btn, tip);
            return btn;
        }
        protected WSliderWithLabel AddSliderWithRange(int idx, string txt, float curVal, float from, float to,
                Action<float> onChange, string tip = null) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, contentRect, idx);
            slider.Label.Load(txt);
            slider.Slider.Load(curVal, onChange, from, to);
            AddTip(slider, tip);

            return slider;
        }
        protected WSliderWithLabel AddSlider(int idx, string txt, float curVal,
                Action<float> onChange, string tip = null) {
            var slider = UISpawn.Widget<WSliderWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider, contentRect, idx);
            slider.Label.Load(txt);
            slider.Slider.Load(curVal, onChange);
            AddTip(slider, tip);

            return slider;
        }
        protected WSliderRangeWithLabel AddRangeSlider(int idx, string txt, float curValFrom, float curValTo,
                float fromFrom, float fromTo, float toFrom, float toTo,
                Action<float, float> onChange, string tip = null) {
            var slider = UISpawn.Widget<WSliderRangeWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Slider_Range, contentRect, idx);
            slider.Label.Load(txt);
            // The "from" slider runs right to left in the prefab, so its raw value is the
            // mirror image of the range start within [fromFrom, fromTo], both ways
            float MirrorFrom(float v) => fromFrom + fromTo - v;
            slider.SliderFrom.Load(MirrorFrom(curValFrom),
                (v) => onChange(MirrorFrom(v), slider.SliderTo.Value), fromFrom, fromTo);
            slider.SliderTo.Load(curValTo,
                (v) => onChange(MirrorFrom(slider.SliderFrom.Value), v), toFrom, toTo);

            AddTip(slider, tip);

            return slider;
        }
        protected WInputWithLabel AddInput(int idx, string txt, string placeholder, string value, Action<string> onChange, string tip = null) {
            var input = UISpawn.Widget<WInputWithLabel>(lifetime, UIPrefabId.WTabPanelItem_Input, contentRect, idx);
            input.Label.Load(txt);
            input.Input.Load(placeholder, value, onChange);
            AddTip(input, tip);
            return input;
        }
        protected WToggleText AddToggle(int idx, string txt1, bool v1, Action<bool> onChange1, string tip1 = null) {
            var toggle = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WTabPanelItem_Toggle, contentRect, idx);
            toggle.Load(txt1, v1, onChange1);
            AddTip(toggle, tip1);
            return toggle;
        }

        protected WToggleText AddToggle(int idx, string txt, string prefKey, bool defaultValue, string tip = null) {
            return AddToggle(idx, txt, GBool(prefKey, defaultValue), (v) => SetGBool(prefKey, v), tip);
        }

        protected WToggleWithButton AddToggleWithButton(int idx, string toggleTxt, bool toggleOn, Action<bool> onToggle, string toggleTip,
            string btnTxt, Action btnOnClick, string btnTip = null) {
            var w = UISpawn.Widget<WToggleWithButton>(lifetime, UIPrefabId.WTabPanelItem_ToggleButtonSmall, contentRect, idx);
            w.Toggle.Load(toggleTxt, toggleOn, onToggle);
            AddTip(w.Toggle, toggleTip);
            w.Button.Load(btnTxt, btnOnClick);
            AddTip(w.Button, btnTip);
            return w;
        }
        protected WText2WithButton AddText2WithButton(int idx, string txt, string txt2, string txtTip,
            string btnTxt, Action btnOnClick, string btnTip = null) {
            var w = UISpawn.Widget<WText2WithButton>(lifetime, UIPrefabId.WTabPanelItem_Text2Button, contentRect, idx);
            w.Text2.Load(txt, txt2);
            AddTip(w.Text2, txtTip);
            w.Button.Load(btnTxt, btnOnClick);
            AddTip(w.Button, btnTip);
            return w;
        }
        protected WProgressWithButton AddProgressWithButton(int idx, string progressTxt, float progress, string progressTip,
            string btnTxt, Action btnOnClick, string btnTip = null) {
            var w = UISpawn.Widget<WProgressWithButton>(lifetime, UIPrefabId.WTabPanelItem_ProgressButtonSmall, contentRect, idx);
            w.Progress.Load(progressTxt, progress);
            AddTip(w.Progress, progressTip);
            w.Button.Load(btnTxt, btnOnClick);
            AddTip(w.Button, btnTip);
            return w;
        }

        protected WToggle2 AddTogglePair(int idx, string txt1, bool v1, Action<bool> onChange1, string tip1,
                string txt2, bool v2, Action<bool> onChange2, string tip2 = null) {
            var toggles = UISpawn.Widget<WToggle2>(lifetime, UIPrefabId.WTabPanelItem_Toggle2, contentRect, idx);
            toggles.Toggle1.Load(txt1, v1, onChange1);
            toggles.Toggle2.Load(txt2, v2,  onChange2);
            AddTip(toggles.Toggle1, tip1);
            AddTip(toggles.Toggle2, tip2);
            return toggles;
        }

        protected WToggle2 AddTogglePair(int idx,
                string txt1, string prefKey1, bool default1, string tip1,
                string txt2, string prefKey2, bool default2, string tip2 = null) {
            return AddTogglePair(idx,
                txt1, GBool(prefKey1, default1), (v) => SetGBool(prefKey1, v), tip1,
                txt2, GBool(prefKey2, default2), (v) => SetGBool(prefKey2, v), tip2);
        }



        protected WButtonText AddFooterButton(int idx, string txt, Action onClick, string tip = null) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, footer.Container, idx);
            btn.Load(txt, onClick);
            AddTip(btn, tip);
            return btn;
        }

        protected virtual void BeforeUnloadTab() { }

        protected abstract void LoadTab();
    }
}
