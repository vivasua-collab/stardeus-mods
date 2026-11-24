using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Rendering;
using Game.Scenarios;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ModifiersWidget : BaseWidget, IContextPopup {
        [SerializeField] private WButtonText btnHeader;
        [SerializeField] private WGroup container;
        [SerializeField] private WGroup footer;
        public Action OnClose;
        private GameObject panelBlock;
        private GameSetupData data;
        private Lifetime parentLife;
        private Lifetime lifetime;
        private WGroup activeContent;
        private WGroup activeFooter;
        public int UniquePopupHash => Primes.Prime012;
        public bool ShowInScreenCenter => false;

        public void Initialize(GameSetupData data, GameObject block) {
            this.data = data;
            panelBlock = block;
            container.DestroyChildren();
            if (btnHeader != null) btnHeader.Load(T.NounCustomRules, Close);
            footer.DestroyChildren();
            block.SetActiveFast(false);
        }

        public void Show(Lifetime parentLife) {
            this.parentLife = parentLife;
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild("ModifiersWidget");
            activeContent = container;
            activeFooter = footer;
            LoadModifiers();
            if (panelBlock != null) panelBlock.SetActiveFast(true);
        }

        private void LoadModifiers() {
            AddFooterButton(0, T.ResetToDefaults, OnReset);

            var block = UISpawn.Widget<WGroup>(lifetime, UIPrefabId.WContentBlock_FreeHeight,
                activeContent.Container, 0);

            int idx = 0;
            activeFooter.SetActiveFast(true);

            string lastGroup = null;
            var isCloneGame = A.IsSaveTemplate;
            var isSetup = G.IsDemoMode && !G.HasEnteredGame;

            foreach (var m in Modifier.All
                    .Where(m => !m.SkipModifiersPanel)
                    .OrderBy(m => m.SortOrder).ThenBy(m => m.NameT)) {
                try {
                    if (!isSetup && !m.CanChangeInGame && !(isCloneGame && m.ForceShowInCloneGame)) {
                        continue;
                    }
                    if (m is IScenarioModifier sm && !sm.ShowInScenario(data.Scenario)) {
                        // Setup is the only time these are drawn, so honouring the scenario
                        // here is the whole point of IScenarioModifier. Clone games are the
                        // exception: CloneGameUI.EnsureCrew builds its crew from the wrecked
                        // scenario, not from the save's own scenario, so its species sliders
                        // stay meaningful.
                        if (!(isCloneGame && m.ForceShowInCloneGame)) {
                            continue;
                        }
                    }
                    if (m.SkipSandbox && data.Scenario.IsSandbox) {
                        if (!(isCloneGame && m.ForceShowInCloneGame)) {
                            continue;
                        }
                    }
                    if (m.GroupName != lastGroup) {
                        lastGroup = m.GroupName;
                        AddHeader(idx++, m.GroupName, block);
                    }
                    m.CreateNewGameControls(lifetime, idx++, block.Container);
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed building modifier control: {0}", m.NameT);
                }
            }
            RebuildLayout();
        }
        private void OnReset() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild("Modifiers");
            Modifier.ResetAll(data);
            LoadModifiers();
        }

        private WText AddHeader(int idx, string text, WGroup container) {
            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WTabPanelItem_Header,
                container.Container, idx);
            header.Load(text);
            return header;
        }
        private WButtonText AddFooterButton(int idx, string txt, Action onClick, string tip = null) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, activeFooter.Container, idx);
            btn.Load(txt, onClick);
            UIUtils.AddTip(lifetime, btn, tip);
            return btn;
        }

        public void Close() {
            lifetime?.Dispose();
            lifetime = null;
            if (panelBlock != null) panelBlock.SetActiveFast(false);
            OnClose?.Invoke();
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            parentLife = lifetime;
            this.lifetime?.Dispose();
            this.lifetime = lifetime.CreateChild("ModifiersWidget");
            data = new GameSetupData(A.S.Scenario);
            data.Difficulty = A.S.Difficulty;
            data.StoryGen = A.S.Query.GetStoryGen.Ask();
            activeContent = wContent;
            activeFooter = wFooter;
            LoadModifiers();
            return T.Modifiers;
        }

        public void OnPopupClosed() {
        }

        public void OnPopupOpened() {
        }
    }
}
