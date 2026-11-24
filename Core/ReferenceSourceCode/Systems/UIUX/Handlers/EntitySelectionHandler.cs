using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class EntitySelectionHandler : BaseWidgetHandler<EntityPanelMultiWidget> {
        // An entity whose task is recreated every few ticks would otherwise rebuild the
        // panel and the bar every frame
        public const float GoalRefreshCooldown = 0.25f;
        private List<Being> selectedBeings;
        private List<Obj> selectedObjs;
        private List<Tile> selectedTiles;
        private int beingsCount;
        private int objsCount;
        private int tilesCount;
        private bool wasUpdated;
        private bool skipNextAutoClose;
        // Being without an entity that the entity panel is showing. Owned by the panel
        // via UISig.DetachedBeingPanel, never set or cleared from selection changes.
        private BeingData detachedBeing;
        // Detached being whose actions the bottom bar shows, so that reopening its
        // panel counts as a refresh rather than a selection change
        private BeingData shownDetachedBeing;
        private bool detachedPanelClosed;
        // What the bar was built from: tasks and being states (InDevice, Dead) come and go
        // without whoever changed them rebuilding the bar, so it watches them itself
        private IReadOnlyList<Entity> barEntities;
        private readonly List<AIGoal> barGoals = new();
        private readonly List<BeingState> barStates = new();
        private float nextGoalCheck;
        private readonly HashSet<Species> playedSelectSounds = new();
        public EntitySelectionHandler(GameState s, EntityPanelMultiWidget widget) : base(s, widget) {
            widget.Initialize();
            s.Sig.MultiSelect.AddListener(OnMultiSelect);
            s.Sig.BeingsSelected.AddListener(OnBeingsSelected);
            s.Sig.TilesSelected.AddListener(OnTilesSelected);
            s.Sig.ObjSelected.AddListener(OnObjSelected);
            s.UIQuery.RebuildEntityActions.SetResponder(() => RebuildEntityActions(false));
            s.UIQuery.SuppressEntityPanelAutoClose.SetResponder(() => {
                skipNextAutoClose = true;
                return true;
            });
            s.UISig.DetachedBeingPanel.AddListener(OnDetachedBeingPanel);
        }

        private void OnDetachedBeingPanel(BeingData being) {
            detachedBeing = being;
            if (being != null) {
                detachedPanelClosed = false;
                BuildActionMenuFor(being, being != shownDetachedBeing);
                shownDetachedBeing = being;
                return;
            }
            // Handled in Update: a panel reload closes and reopens the same being
            // right away, which must not slide the bottom bar
            detachedPanelClosed = true;
        }

        private void OnDetachedBeingPanelClosed() {
            shownDetachedBeing = null;
            // Something can still be selected, the panel can open for a detached being
            // without deselecting
            if (!RebuildEntityActions(true)) {
                ClearActionMenu();
            }
        }

        private bool RebuildEntityActions(bool animate) {
            Coroutines.OnFrame(1, UIHoverFix.RefreshHover);
            if (detachedBeing != null) {
                BuildActionMenuFor(detachedBeing, animate);
                return true;
            }
            if (!Arrays.IsEmpty(selectedBeings)) {
                BuildActionMenuFor(selectedBeings, animate);
                return true;
            } else if (!Arrays.IsEmpty(selectedTiles)) {
                BuildActionMenuFor(selectedTiles, animate);
                return true;
            } else if (!Arrays.IsEmpty(selectedObjs)) {
                BuildActionMenuFor(selectedObjs, animate);
                return true;
            }
            return false;
        }

        private void OnObjSelected(List<Obj> objs) {
            var cnt = objs.Count;
            if (objsCount + cnt > 0) {
                objsCount = cnt;
                selectedObjs = objs;
                wasUpdated = true;
                if (objs.Count == 1) {
                    Sounds.PlayTileSelect(objs[0].Position);
                } else {
                    Sounds.PlayTileSelect(The.Bindings.Mouse.WorldPosition);
                }
            }
            // D.Warn("Objs selected: {0}", cnt);
        }

        private void OnTilesSelected(List<Tile> tiles) {
            var cnt = tiles.Count;
            if (tilesCount + cnt > 0) {
                tilesCount = cnt;
                selectedTiles = tiles;
                wasUpdated = true;
                if (tiles.Count == 1) {
                    Sounds.PlayTileSelect(tiles[0].Position);
                } else {
                    Sounds.PlayTileSelect(The.Bindings.Mouse.WorldPosition);
                }
            }
            // D.Warn("Tiles selected: {0}", cnt);
        }

        private void OnBeingsSelected(List<Being> beings) {
            var cnt = beings.Count;
            if (beingsCount + cnt > 0) {
                PlaySelectedBeingSounds(beings);
                beingsCount = cnt;
                selectedBeings = beings;
                wasUpdated = true;
            }
            // D.Warn("Beings selected: {0}", cnt);
        }

        private void PlaySelectedBeingSounds(List<Being> beings) {
            foreach (var b in beings) {
                if (playedSelectSounds.Add(b.Species)) {
                    BeingUtils.PlaySelectSoundFor(b);
                }
            }
            playedSelectSounds.Clear();
        }

        private void OnMultiSelect(List<Being> beings, List<Obj> objs, List<Tile> tiles) {
            OnBeingsSelected(beings);
            OnTilesSelected(tiles);
            OnObjSelected(objs);
        }

        public void Update() {
            if (detachedPanelClosed) {
                detachedPanelClosed = false;
                OnDetachedBeingPanelClosed();
            }
            if (!wasUpdated) {
                RebuildIfOutdated();
                return;
            }
            wasUpdated = false;
            // Consume the one-shot suppress flag (set when clicking an expedition being
            // while something on the map was selected — we don't want the deferred empty
            // selection to close the panel ShowProfile just opened).
            bool suppressClose = skipNextAutoClose;
            skipNextAutoClose = false;

            bool needsRebuild = false;
            if (selectedBeings != null) {
                for (int i = selectedBeings.Count - 1; i >= 0; i--) {
                    if (!selectedBeings[i].IsActive) {
                        selectedBeings.RemoveAt(i);
                        needsRebuild = true;
                    }
                }
            }
            if (selectedObjs != null) {
                for (int i = selectedObjs.Count - 1; i >= 0; i--) {
                    if (!selectedObjs[i].IsActive) {
                        selectedObjs.RemoveAt(i);
                        needsRebuild = true;
                    }
                }
            }
            if (selectedTiles != null) {
                for (int i = selectedTiles.Count - 1; i >= 0; i--) {
                    if (!selectedTiles[i].IsActive) {
                        selectedTiles.RemoveAt(i);
                        needsRebuild = true;
                    }
                }
            }
            if (needsRebuild) {
                RebuildEntityActions(false);
                return;
            }

            beingsCount = selectedBeings?.Count ?? 0;

            if (beingsCount > 0) {
                if (beingsCount == 1) {
                    S.UIQuery.ShowBeingPanel.Ask(selectedBeings[0].Data);
                } else {
                    S.UIQuery.HideEntityPanel.Ask();
                    widget.ShowBeings(S, selectedBeings);
                }
                BuildActionMenuFor(selectedBeings, true);
            } else if ((tilesCount = selectedTiles?.Count ?? 0) > 0) {
                if (tilesCount == 1) {
                    S.UIQuery.ShowTilePanel.Ask(EntityUtils.ParentObj(selectedTiles[0]));
                } else {
                    S.UIQuery.HideEntityPanel.Ask();
                    widget.ShowTiles(S, selectedTiles);
                }
                BuildActionMenuFor(selectedTiles, true);
            } else if ((objsCount = selectedObjs?.Count ?? 0) > 0) {
                if (objsCount == 1) {
                    S.UIQuery.ShowObjPanel.Ask(selectedObjs[0].Data);
                } else {
                    S.UIQuery.HideEntityPanel.Ask();
                    widget.ShowObjs(S, selectedObjs);
                }
                BuildActionMenuFor(selectedObjs, true);
            } else {
                if (!suppressClose) {
                    S.UIQuery.HideEntityPanel.Ask();
                }
                // Panel is still showing a detached being (its close would have nulled
                // this), so the bar has to keep that being's actions
                if (detachedBeing == null) {
                    ClearActionMenu();
                }
                widget.Close();
            }
        }

        private void ClearActionMenu() {
            RememberBuiltFrom(null);
            S.UIQuery.ShowEntityActions.Ask(null, true);
            S.UISig.SelectionActionsShown.Send(null);
        }

        // Which single entity's actions just took over the bar, for the ring that points
        // there (EntityActionsHintHandler). A refresh is not a new selection.
        private void AnnounceShown(IReadOnlyList<Entity> entities, int actionCount,
                bool animate) {
            if (!animate) { return; }
            Entity shown = null;
            if (entities != null && entities.Count == 1 && actionCount > 0) {
                shown = entities[0] is Tile tile ? EntityUtils.ParentObj(tile) : entities[0];
            }
            S.UISig.SelectionActionsShown.Send(shown);
        }

        // A new run's tutorial leaves out actions a new player does not need yet
        // (HudRevealRules); their hotkeys come back with the buttons
        private bool IsShownByReveal(BaseEntityAction action) {
            return !HudRevealRules.TryGetActionKey(action.Id, out var key)
                || S.UIQuery.IsHudRevealed.Ask(key);
        }

        private void BuildActionMenuFor(List<Tile> entities, bool animate) {
            List<EntityActionData> actions = new();
            foreach (var action in Registries.EntityActions.All.Values) {
                if (IsShownByReveal(action) && action.ApplyFor(S, entities, out var data)) {
                    actions.Add(data);
                }
            }
            actions.Sort(CompareActions);
            RememberBuiltFrom(entities);
            S.UIQuery.ShowEntityActions.Ask(actions, animate);
            AnnounceShown(entities, actions.Count, animate);
        }
        private void BuildActionMenuFor(List<Obj> entities, bool animate) {
            List<EntityActionData> actions = new();
            foreach (var action in Registries.EntityActions.All.Values) {
                if (IsShownByReveal(action) && action.ApplyFor(S, entities, out var data)) {
                    actions.Add(data);
                }
            }
            actions.Sort(CompareActions);
            RememberBuiltFrom(entities);
            S.UIQuery.ShowEntityActions.Ask(actions, animate);
            AnnounceShown(entities, actions.Count, animate);
        }
        private void BuildActionMenuFor(BeingData being, bool animate) {
            List<EntityActionData> actions = new();
            foreach (var action in Registries.EntityActions.All.Values) {
                if (IsShownByReveal(action) && action.ApplyForDetached(S, being, out var data)) {
                    actions.Add(data);
                }
            }
            actions.Sort(CompareActions);
            // A detached being has no entity, so no goal to watch
            RememberBuiltFrom(null);
            S.UIQuery.ShowEntityActions.Ask(actions, animate);
            AnnounceShown(null, actions.Count, animate);
        }
        private void BuildActionMenuFor(List<Being> entities, bool animate) {
            List<EntityActionData> actions = new();
            foreach (var action in Registries.EntityActions.All.Values) {
                if (IsShownByReveal(action) && action.ApplyFor(S, entities, out var data)) {
                    actions.Add(data);
                }
            }
            actions.Sort(CompareActions);
            RememberBuiltFrom(entities);
            S.UIQuery.ShowEntityActions.Ask(actions, animate);
            AnnounceShown(entities, actions.Count, animate);
        }

        // Every bar build records the goals and states, so a rebuild somebody else already forced this
        // frame leaves nothing outdated and is never repeated here
        private void RememberBuiltFrom(IReadOnlyList<Entity> entities) {
            barEntities = entities;
            barGoals.Clear();
            barStates.Clear();
            if (entities == null) { return; }
            for (int i = 0; i < entities.Count; i++) {
                barGoals.Add(GoalOf(entities[i]));
                barStates.Add(StateOf(entities[i]));
            }
        }

        private void RebuildIfOutdated() {
            if (Time.unscaledTime < nextGoalCheck || !BarIsOutdated()) { return; }
            nextGoalCheck = Time.unscaledTime + GoalRefreshCooldown;
            RebuildEntityActions(false);
        }

        private bool BarIsOutdated() {
            if (barEntities == null) { return false; }
            if (barEntities.Count != barGoals.Count) { return true; }
            for (int i = 0; i < barGoals.Count; i++) {
                if (GoalOf(barEntities[i]) != barGoals[i]) { return true; }
                if (StateOf(barEntities[i]) != barStates[i]) { return true; }
            }
            return false;
        }

        // Leaving a device clears InDevice without touching the being's own goal (a surgery
        // hub's task sits on the hub), and InDevice hides most being actions
        private static BeingState StateOf(Entity entity) {
            return entity is Being b && b.HasData ? b.Data.StateFlags : BeingState.Uninitialized;
        }

        // Actions resolve a selected tile to its parent object, and that is where the goal is
        private static AIGoal GoalOf(Entity entity) {
            if (entity is Tile tile) {
                entity = EntityUtils.ParentObj(tile);
            }
            return entity?.Goal;
        }

        private int CompareActions(EntityActionData a, EntityActionData b) {
            var priorityComparison = b.Priority.CompareTo(a.Priority);
            if (priorityComparison != 0) {
                return priorityComparison;
            }
            var orderComparison = a.Order.CompareTo(b.Order);
            if (orderComparison != 0) {
                return orderComparison;
            }
            // List.Sort is unstable. Actions sharing a hotkey rely on which button
            // comes first (Draft before Undraft), so ties need a fixed order.
            return string.CompareOrdinal(a.Action.Id, b.Action.Id);
        }
    }
}
