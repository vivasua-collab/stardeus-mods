using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ShipConfigWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButton wCloseButton;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WButtonText upgradeBtn;
        [SerializeField] private WImage classImage;
        [SerializeField] private WText classText;
        [SerializeField] private WButtonText maneuverBtn;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private Lifetime lifetime;
        private Lifetime footLife;
        private ShipConfigContext ctx;
        private GameState S;
        private readonly Dictionary<string, WTabButton> tabs = new();
        private ShipConfigTab_Configuration configTab;
        private ShipConfigTab_Checklist checklistTab;
        private ShipConfigTab_Devices devicesTab;
        public void Initialize(GameState s, ShipConfigContext ctx) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.ShipConfig, this);
            S = s;
            this.ctx = ctx;
            configTab = new ShipConfigTab_Configuration(this, s);
            checklistTab = new ShipConfigTab_Checklist(this, s);
            devicesTab = new ShipConfigTab_Devices(this, s);
            upgradeBtn.Load(T.UpgradeShipClass, UpgradeClass);
            wCloseButton.Load(Close);
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            SubPanel.Initialize();
            maneuverBtn.Load(T.VerbMoveShip, OnMoveShip);
            CreateTabs();
        }

        private void UpgradeClass() {
            var defs = ShipClassDef.AllOrdered.ToList();
            S.UIQuery.ShowShipClassChoice.Ask(defs, OnChooseShipClass);
        }

        private void OnChooseShipClass(ShipClassDef shipClass) {
            if (shipClass == null) { return; }
            S.Sys.NPCShip.SetPlayerShipClass(shipClass);
            Load();
        }

        private void CreateTabs() {
            int idx = 0;
            tabs.Clear();
            CreateTab(idx++, T.ShipConfiguration);
            CreateTab(idx++, T.FlightChecklist);
            CreateTab(idx++, T.NounClassPoints);
        }

        private void CreateTab(int idx, string name) {
            // Tabs have no lifetime, they will last for the whole game
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabs.Container);

            w.Load(name, () => ctx.ShowTab(name));
            w.SetSiblingIdx(idx++);
            tabs[name] = w;
        }

        public override void Toggle(bool show) {
            if (show) {
                UIShowing.Set(UIShowingFlags.ShipConfig);
            } else {
                UIShowing.Unset(UIShowingFlags.ShipConfig);
            }
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
                ctx.SelectedTab = null;
            }
        }


        public void Load() {
            D.Warn("Loading ship config widget for tab {0}", ctx.SelectedTab);
            Cleanup();

            classImage.Load(A.Player.Ship.Class.Preview);

            lifetime = ctx.MainLifetime = Lifetime.Create("ShipConfig");
            A.Player.Ship.CombatState.DevicesChangedB.Bind(lifetime, () => {
                var txt = A.Player.Ship.Class.NameT;
                var cs = A.Player.Ship.CombatState;
                var cp = T.NounClassPointsShort_V1.F(
                    Units.XOutOfY(cs.CurrentClassPoints,
                        A.Player.Ship.Class.MaxClassPoints));
                txt = $"{txt} {Texts.GreyParenthesesSmaller(cp)}";
                classText.Load(txt);
            });

            if (ctx.SelectedTab == null) {
                ctx.SelectedTab = T.ShipConfiguration;
            }
            foreach (var tab in tabs) {
                tab.Value.SetSelected(tab.Key == ctx.SelectedTab);
            }

            if (ctx.SelectedTab == T.ShipConfiguration) {
                configTab.Load(lifetime);
            } else if (ctx.SelectedTab == T.FlightChecklist) {
                checklistTab.Load(lifetime);
            } else if (ctx.SelectedTab == T.NounClassPoints) {
                devicesTab.Load(lifetime);
            }
        }

        public void Close() {
            S.UIQuery.ToggleShipConfig.Ask(false);
        }

        public void Cleanup() {
            footLife?.Dispose();
            footLife = null;
            configTab?.Cleanup();
            checklistTab?.Cleanup();
            devicesTab?.Cleanup();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            ctx.MenuLifetime?.Dispose();
            ctx.MenuLifetime = null;
            SubPanel.Close();
        }

        public bool TryCloseWidget() {
            if (SubPanel.IsShowing) {
                SubPanel.Close();
                return true;
            }
            Toggle(false);
            return true;
        }
        private void OnMoveShip() {
            if (!S.Research.IsUnlocked(DefIdsH.ResearchSpaceTravelLocalManeuvering)) {
                S.Sig.ShowErrorMessage.Send($"{T.ResearchNodeLocked}: {Texts.LinkToResearch(DefIdsH.ResearchSpaceTravelLocalManeuvering, T.ResearchLocalmaneuvering)}");
                return;
            }
            MoveShipAction.TryLocalManeuver(maneuverBtn);
        }
    }
}
