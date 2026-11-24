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
    public abstract class BaseEntityWarning {
        protected static void RegisterWarning(BaseEntityWarning action) {
            Registries.EntityWarnings.Add(action.Id, action);
        }
        public abstract string Id { get; }
        public abstract int Order { get; }
        protected Lifetime lifetime;

        public virtual bool AddWarningOverlay => false;
        public virtual (string icon, string text) WarningOverlayData => (null, null);

        public virtual bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            return false;
        }
        public virtual bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            return false;
        }
        public virtual bool ShowFor(GameState s, ObjData obj, out bool blockActions) {
            blockActions = false;
            return false;
        }
        public abstract void LoadContent(Lifetime life, WGroup group, ref int idx);
        public abstract void Unload();
        public static int CompareWarnings(BaseEntityWarning a, BaseEntityWarning b) {
            return a.Order.CompareTo(b.Order);
        }
    }
}
