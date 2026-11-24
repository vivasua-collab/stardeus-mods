using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ManageTaskAction : BaseEntityAction {
        public const string ActionId = "ManageTask";

        [SelfInit]
        private static void Register() {
            RegisterAction(new ManageTaskAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => T.Tasks;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            return ApplyForSingle(entity, out data);
        }

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            return ApplyForSingle(obj, out data);
        }

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            var parent = EntityUtils.ParentObj(tile);
            return ApplyForSingle(parent, out data);
        }

        private bool ApplyForSingle(Entity entity, out EntityActionData data) {
            if (!HasApplicableGoal(entity)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, entity), Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            return ApplyForManyEntities(beings, out data);
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            return ApplyForManyEntities(objs, out data);
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!CollectApplicableFromTiles(tiles, out var applicable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, applicable), Priority.Low);
            return true;
        }

        private bool ApplyForManyEntities<TEnt>(List<TEnt> entities, out EntityActionData data) where TEnt : Entity {
            if (!CollectApplicable(entities, out var applicable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, applicable), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, Entity entity) {
            var widget = SpawnButton(l, true, p, i);
            string label = TryGetApplicableGoal(entity, out var goal) ? $"{T.JobTypeTask}: {Texts.Accented(goal.Name, false)}" : T.JobTypeTask;
            widget.Load(IconId.CTask, label, () => ShowSingleChoice(entity, widget));
            return widget;
        }

        private WBase CreateMulti<TEnt>(Lifetime l, RectTransform p, int i, List<TEnt> entities) where TEnt : Entity {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(entities.Count, IconId.CTask, T.Tasks, () => ShowMultiChoice(entities, widget));
            return widget;
        }

        private static bool CollectApplicable<TEnt>(List<TEnt> entities, out List<TEnt> applicable) where TEnt : Entity {
            applicable = new List<TEnt>();
            foreach (var entity in entities) {
                if (HasApplicableGoal(entity)) {
                    applicable.Add(entity);
                }
            }
            return applicable.Count > 0;
        }

        private static bool CollectApplicableFromTiles(List<Tile> tiles, out List<Tile> applicable) {
            applicable = new List<Tile>();
            var visited = Caches.IntSet;
            visited.Clear();
            foreach (var tile in tiles) {
                var parent = EntityUtils.ParentObj(tile);
                if (parent == null || !visited.Add(parent.Id)) {
                    continue;
                }
                if (HasApplicableGoal(parent)) {
                    applicable.Add(parent);
                }
            }
            return applicable.Count > 0;
        }

        private static void ShowSingleChoice(Entity entity, WBase anchor) {
            if (!TryGetApplicableGoal(entity, out var goal)) {
                return;
            }

            var qc = QuickChoice.Create(goal.TaskLine);
            qc.Widget = anchor.Rect;

            qc.AddButton(The.Bindings.AddShortcutGlyphTo(T.CancelTask, Toolbox.CancelSelectedTasksBindingId), () => {
                AIGoal.Cancel(goal, G.Ticks, removeImmediately: false);
                UIUtils.RefreshSelectedEntityUI();
            });

            if (AIGoal.IsFailed(goal)) {
                qc.AddButton(T.AdvertRetry, () => {
                    AIGoal.TryRetry(goal, G.Ticks, true, true);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }

            for (int p = 0; p <= 5; p++) {
                var capP = p;
                AddPriorityOption(qc, capP, goal.GoalPriority, () => {
                    goal.SetPriority(G.Ticks, capP, allowZero: true);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }

            qc.Show();
        }

        private static void ShowMultiChoice<TEnt>(List<TEnt> entities, WBase anchor) where TEnt : Entity {
            var goals = CollectGoals(entities);
            if (goals.Count == 0) {
                return;
            }

            var qc = QuickChoice.Create(T.Tasks);
            qc.Widget = anchor.Rect;
            int cnt = goals.Count;

            qc.AddButton($"{The.Bindings.AddShortcutGlyphTo(T.ManageAdsCancel, Toolbox.CancelSelectedTasksBindingId)} ({cnt})", () => {
                foreach (var goal in CollectGoals(entities)) {
                    AIGoal.Cancel(goal, G.Ticks, removeImmediately: false);
                }
                UIUtils.RefreshSelectedEntityUI();
            });

            int failedCnt = 0;
            foreach (var goal in goals) {
                if (AIGoal.IsFailed(goal)) {
                    failedCnt++;
                }
            }
            if (failedCnt > 0) {
                qc.AddButton($"{T.AdvertRetry} ({failedCnt})", () => {
                    foreach (var goal in CollectGoals(entities)) {
                        if (AIGoal.IsFailed(goal)) {
                            AIGoal.TryRetry(goal, G.Ticks, true, true);
                        }
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }

            int commonPriority = goals[0].GoalPriority;
            for (int gi = 1; gi < goals.Count; gi++) {
                if (goals[gi].GoalPriority != commonPriority) {
                    commonPriority = -1;
                    break;
                }
            }

            for (int p = 0; p <= 5; p++) {
                var capP = p;
                AddPriorityOption(qc, capP, commonPriority, () => {
                    foreach (var goal in CollectGoals(entities)) {
                        goal.SetPriority(G.Ticks, capP, allowZero: true);
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }

            qc.Show();
        }

        private static void AddPriorityOption(QuickChoice qc, int p, int currentPriority,
                System.Action onClick) {
            string text = $"{T.VerbSetPriority}: {p}{PriorityQualifier(p)}";
            if (p == currentPriority) {
                var cur = Texts.Accented($"{GlyphMap.ArrowLeft} {T.AdjCurrent}", false);
                text = $"{text} {cur}";
            } else {
            }
            qc.AddButton(text, onClick);
        }

        private static string PriorityQualifier(int p) {
            return p switch {
                0 => " " + Texts.GreyParentheses(T.AdjForbidden),
                1 => " " + Texts.GreyParentheses(T.AdjPriorityLowest),
                5 => " " + Texts.GreyParentheses(T.AdjPriorityHighest),
                _ => "",
            };
        }

        private static List<AIGoal> CollectGoals<TEnt>(List<TEnt> entities) where TEnt : Entity {
            var goals = new List<AIGoal>();
            foreach (var entity in entities) {
                if (TryGetApplicableGoal(entity, out var goal)) {
                    goals.Add(goal);
                }
            }
            return goals;
        }

        private static bool HasApplicableGoal(Entity entity) {
            return TryGetApplicableGoal(entity, out _);
        }

        private static bool TryGetApplicableGoal(Entity entity, out AIGoal goal) {
            goal = null;
            if (entity?.IsActive != true) {
                return false;
            }
            goal = entity.Goal;
            if (goal == null || goal.Type != AIGoalType.Colony) {
                return false;
            }
            return goal.Target?.Entity == entity;
        }
    }
}
