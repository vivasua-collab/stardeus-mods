using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class UnconstructedTileTab : BaseEntityTab {
        public const string TabId = "UnconstructedTile";

        [SelfInit]
        private static void Register() {
            RegisterTab(new UnconstructedTileTab());
        }

        private Lifetime matsLife;
        private Tile tile;
        private ConstructableComp constructable;
        private GameState S;
        private bool matsExpanded = true;

        public override string TabName => T.EnergyOverview;

        public override int Order => -20;

        public override bool ShowFor(GameState s, Tile tile) {
            if (tile.IsConstructed) { return false; }
            if (tile.Constructable.IsBrokenDown) { return false; }
            if (tile.Reinstallable?.IsPending == true) { return false; }
            // LoadFooter needs the goal. It used to reload the panel when there was none,
            // which rebuilt this same tab and recursed until the game froze (STA-1004)
            if (!tile.Constructable.TryGetGoal(out _)) { return false; }
            S = s;
            this.tile = tile;
            constructable = tile.Constructable;
            return true;
        }

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            constructable.IsFinishedB.Bind(parentLifetime, (isFinished) => {
                if (isFinished) {
                    // delay the reload because some components can fail
                    D.Warn("Will reload entity panel after construction finished for: {0}", tile);
                    Coroutines.OnNextFrame(() => {
                        if (!parentLifetime.IsDisposed) {
                            ReloadPanel();
                        }
                    });
                }
            }, false);
        }

        public override bool LoadFooter(WGroup group) {
            if (!constructable.TryGetGoal(out var goal)) {
                return false;
            }
            var idx = 0;
            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime, UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => {
                goal.SetPriority(G.Ticks, UIUtils.AdjustPriority0To5(goal.GoalPriority, 1));
            });
            prio.Down.Load(IconId.WArrowDown, () => {
                goal.SetPriority(G.Ticks, UIUtils.AdjustPriority0To5(goal.GoalPriority, -1));
            });
            prio.Texts.Load(T.TaskPriority, null);
            UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);
            goal.GoalPriorityB.Bind(lifetime, (p) => {
                prio.Texts.SetText2(Units.XOutOfY(p, 5));
                if (p == 0) {
                    prio.Texts.SetText(Texts.Red(T.TaskPriority));
                } else {
                    prio.Texts.SetText(T.TaskPriority);
                }
            });

            var clear = UISpawn.Widget<WButtonIconText>(lifetime, UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            clear.Load(IconId.CForbid, T.CancelTask, Cancel);
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            header.Label.Load(T.JobTypeConstruction);
            constructable.WorkDonePctB.Bind(lifetime, (done) => {
                header.Progress.SetPercent(done, false);
                header.Progress.SetText(Units.XOutOfY((int) constructable.WorkDone, constructable.WorkAmount));
            });
            UIUtils.AddTipFunc(lifetime, header, () => T.ConstructionProgress_V2.F(
                constructable.WorkDone, constructable.WorkAmount));

            var wMats = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContentListButton, block.Container, idx++);
            wMats.Load(IconId.CConstructionPending, T.ContainsMaterials, null);
            wMats.LoadOnToggle(lifetime, ToggleMats, matsExpanded);
            constructable.MatsChangedB.Bind(lifetime, () => {
                int cnt = 0;
                int total = 0;
                foreach (var mat in constructable.RawMaterials) {
                    total++;
                    if (mat.Diff == 0) {
                        cnt++;
                    }
                }
                wMats.SetText2(Units.XOutOfY(cnt, total));
            });
            return true;
        }

        private bool ToggleMats(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded) {
            matsExpanded = willShow;
            if (!willShow) {
                return wasLoaded;
            }

            if (matsLife != null) {
                matsLife.Dispose();
            }
            matsLife = life.CreateChild("Unbuilt.Mats");
            var mats = constructable.RawMaterials;
            int idx = 0;
            for (int i = 0; i < mats.Length; i++) {
                Mat mat = mats[i];
                var w = UISpawn.Widget<WIconText2Progress>(matsLife,
                    UIPrefabId.WContentProgress, container, idx++);
                w.Load(mat.Type.IconId, mat.Type.NameT, 0f);
                var loopI = i;
                constructable.MatsChangedB.Bind(matsLife, () => {
                    // refresh the struct
                    var mat = mats[loopI];
                    w.SetProgress((float)mat.StackSize / mat.MaxStackSize,
                        Units.XOutOfY(mat.StackSize, mat.MaxStackSize));
                });
                UIUtils.AddTipFunc(matsLife, w, () => {
                    return $"{T.OwnedCount}: {S.Sys.Inventory.AvailableCount(mat.Type)}";
                });
                UIUtils.AddCodexTooltip(matsLife, container.gameObject, w, mat.Type.Def);
            }

            return true;
        }

        private void Cancel() {
            constructable.CancelConstruction();
        }

        protected override void Cleanup() {
            S = null;
            tile = null;
            constructable = null;
        }

        public override string Id => TabId;
    }
}
