using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class DiplomacyWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        private GameState S;

        private Lifetime lifetime;
        private readonly List<WFaction> rows = new();
        private bool needsRebuild;
        private bool rebuildScheduled;
        private long lastRefreshHour = -1;
        private bool isListeningToTick;

        public IReadOnlyList<WFaction> Rows => rows;

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Diplomacy, this);
            S = s;
            wPanelHeader.Load(T.NounDiplomacy, Close);
            wItems.DestroyChildren();
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Diplomacy, show);
            base.Toggle(show);
            // Remove first so a repeated Toggle(true) cannot subscribe twice.
            // FactionRelationChanged is owned by DiplomacyHandler, which stays subscribed
            // for the whole session and forwards to us only while we are showing.
            StopListeningToTick();
            if (show) {
                if (!isListeningToTick) {
                    isListeningToTick = true;
                    S.Clock.OnTick.AddListener(OnTick, gameObject);
                }
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        private void StopListeningToTick() {
            if (isListeningToTick) {
                isListeningToTick = false;
                S.Clock.OnTick.RemoveListener(OnTick);
            }
        }

        private void Load() {
            Cleanup();
            lifetime = Lifetime.Create("Diplomacy");
            needsRebuild = false;
            lastRefreshHour = S.Clock.Ticks / Consts.TicksPerHour;
            int idx = 0;
            // Our own faction goes first: it has no standing or actions of its own, it is
            // there to show at a glance who we are allied with and who is hostile.
            if (FactionDef.Player != null) {
                SpawnRow(FactionDef.Player, idx++);
            }
            foreach (var fac in FactionDef.AllOrdered) {
                if (fac.IsPlayer || fac.IsAntiPlayer) { continue; }
                SpawnRow(fac, idx++);
            }
            // Rows come from the widget pool, so a respawned WFaction still carries the
            // geometry of whichever faction last used that instance. Its own layout chain
            // cannot correct that: every level pairs a ContentSizeFitter with a layout group
            // that does not control child height, so each level reads sizes one rebuild
            // behind the level below it and the stale sizes survive. Depth-first is the
            // part that matters here - a single top-level rebuild would not reach them.
            UIUtils.RebuildLayoutRecursive(wItems.Container);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.verticalNormalizedPosition = 1f;
        }

        private void SpawnRow(FactionDef fac, int idx) {
            var w = UISpawn.Widget<WFaction>(lifetime, UIPrefabId.WFaction,
                wItems.Container, idx);
            w.Load(S, lifetime, fac, RefreshActions);
            rows.Add(w);
        }

        /// <summary>
        /// Called by DiplomacyHandler while this widget is showing.
        ///
        /// Signals dispatch synchronously, so rebuilding here could despawn the very row
        /// whose button callback is still on the stack. Defer to the next UI frame, which
        /// - unlike the game clock - still runs while the game is paused.
        /// </summary>
        public void OnRelationChanged() {
            needsRebuild = true;
            if (rebuildScheduled) { return; }
            rebuildScheduled = true;
            Coroutines.OnNextFrame(RebuildIfDirty);
        }

        private void RebuildIfDirty() {
            rebuildScheduled = false;
            if (!needsRebuild) { return; }
            if (!gameObject.activeSelf) { return; }
            Load();
        }

        /// <summary>
        /// Cooldowns are measured in game ticks, so it is correct for this to stop while
        /// the game is paused.
        /// </summary>
        private void OnTick(long ticks) {
            var hour = ticks / Consts.TicksPerHour;
            if (hour == lastRefreshHour) { return; }
            lastRefreshHour = hour;
            RefreshActions();
        }

        private void RefreshActions() {
            for (int i = 0; i < rows.Count; i++) {
                rows[i].RefreshActions();
            }
        }

        private void Cleanup() {
            rows.Clear();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void Close() {
            S.UIQuery.ToggleDiplomacy.Ask(false);
        }

        public bool TryCloseWidget() {
            if (gameObject.activeSelf) {
                Toggle(false);
                return true;
            }
            return false;
        }
    }
}
