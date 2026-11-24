using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public abstract class BaseEntityTab : IResettable {
        protected static void RegisterTab(BaseEntityTab action) {
            Registries.EntityTabs.Add(action.Id, action);
        }
        public abstract string Id { get; }
        public abstract int Order { get; }
        public abstract string TabName { get; }
        public virtual B<string> TabNameB => null;
        protected Lifetime parentLifetime;
        protected Lifetime lifetime;
        private WTabButton _tabPrefab;
        public virtual bool SuppressWarningOverlay => false;
        // Whether this tab may be shown for a stasis sleeper who was never woken (see
        // StasisUtils.IsUnrevealed). Opt-in, so a new or modded tab cannot leak what the
        // player is not supposed to know about them yet.
        public virtual bool ShowForUnrevealed => false;
        public bool BlockActions;
        protected WTabButton tabPrefab => _tabPrefab
            ??= ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);

        public WTabButton SpawnedButton;
        public WTabButton SpawnTab(Lifetime parentLifetime, RectTransform parent,
                int siblingIdx) {
            var widget = UISpawn.Widget(parentLifetime, tabPrefab, parent, siblingIdx);
            if (TabNameB != null) {
                TabNameB.Bind(parentLifetime, s => SetTabName(widget, s));
            } else {
                SetTabName(widget, TabName);
            }
            SpawnedButton = widget;
            OnSpawnTab(widget, parentLifetime);
            return widget;
        }

        private static void SetTabName(WTabButton widget, string s) {
            if (widget.SetText(s)) {
                widget.RebuildTextLayout();
                LayoutRebuilder.ForceRebuildLayoutImmediate(widget.Rect);
            }
        }

        protected virtual void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) { }

        public virtual bool ShowFor(GameState s, BeingData being) { return false; }
        public virtual bool ShowFor(GameState s, Tile tile) { return false;}
        public virtual bool ShowFor(GameState s, ObjData obj) { return false;}
        protected void ReloadTab() {
            A.S.UIQuery.ReloadEntityTab.Ask(this);
        }
        protected void ReloadPanel() {
            A.S.UIQuery.ReloadCurrentEntity.Ask();
        }

        public static int CompareTabs(BaseEntityTab a, BaseEntityTab b) {
            return a.Order.CompareTo(b.Order);
        }

        public virtual bool LoadTopControls(WGroup group) {
            return false;
        }
        public virtual bool LoadFooter(WGroup group) {
            return false;
        }
        public virtual bool LoadContent(WGroup group) {
            return false;
        }

        protected abstract void Cleanup();
        protected virtual void OnDeactivate() { }

        public void Deactivate() {
            OnDeactivate();
            parentLifetime = null;
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public void Unload() {
            Deactivate();
            Cleanup();
            SpawnedButton = null;
        }

        public void OnReset() {
            Unload();
        }

        public void CreateLifetime(Lifetime parent) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            parentLifetime = parent;
            lifetime = parent.CreateChild($"EntityTab_{Id}");
        }
    }
}
