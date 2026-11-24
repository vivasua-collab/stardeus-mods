using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public abstract class SOAction {
        private static readonly List<string> actionIds = new();
        private static readonly Dictionary<string, SOAction> actions = new();

        [ExecuteOnReload]
        private static void ClearStatics() {
            actionIds.Clear();
            actions.Clear();
        }

        public static void FlushAll() {
            foreach (var action in actions) {
                action.Value.Flush();
            }
        }

        protected static void Register(string actionId, SOAction act) {
            if (actionId != act.Id) {
                D.Err("Trying to register an action with the wrong id! {0} => {1}",
                    actionId, act.Id);
                return;
            }
            if (!actionIds.AddUniqueInefficient(actionId)) {
                D.Err("Action already exists by id: {0}", actionId);
            }
            actions[actionId] = act;
        }

        public static SOAction Get(string actionId) {
            var act = actions.Get(actionId, null);
            if (act == null) {
                D.Err("SOAction not found by id; {0}", actionId);
                return null;
            }
            return act;
        }

        public static void LoadFor(SOActionContext ctx, List<SOAction> res) {
            res.Clear();
            foreach (var a in actions.OrderByDescending(act => act.Value.UIPriority)) {
                var act = a.Value;
                if (act?.IsAvailable(ctx) == true) {
                    act.Load(ctx);
                    res.Add(act);
                }
            }
        }

        public abstract string Id { get; }
        public virtual int UIPriority => 0;
        protected SOActionContext ctx;
        public bool NeedsListRebuild;

        public void Flush() {
            ctx = default;
            NeedsListRebuild = false;
            OnFlush();
        }

        protected virtual void OnFlush() {}
        public abstract bool IsAvailable(SOActionContext ctx);

        public abstract bool AddOptions(QuickChoice choice);

        public void Load(SOActionContext ctx) {
            this.ctx = ctx;
            NeedsListRebuild = false;
        }
    }
}
