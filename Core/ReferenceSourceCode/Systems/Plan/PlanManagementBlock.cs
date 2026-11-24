using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.Plan {
    public static class PlanManagementBlock {
        public static void Load(Lifetime life, WPanel widget, GameState s, int bIdx) {
            var block = UISpawn.Block(life, widget.Content, bIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.UiGroupManagement);

            var savePlanBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            savePlanBtn.Load(IconId.WSaveGame, T.SavePlan, null, () => ShowSavePlanChoice(s, savePlanBtn));

            var loadPlanBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            loadPlanBtn.Load(IconId.WLoadGame, T.LoadPlan, null, () => {
                var names = PlanFileOps.GetNames();
                ShowLoadPlanChoice(s, loadPlanBtn, names);
            });

            var generateBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            generateBtn.Load(IconId.WSpaceShip, T.GeneratePlan, null, () => GeneratePlan(s));

            var manageBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            manageBtn.Load(IconId.WConfig, T.ManageSavedPlans, null, () => ShowManageChoice(s, manageBtn));
        }


        private static void ShowSavePlanChoice(GameState s, WBase anchor) {
            string planName = s.SaveName;
            var qc = QuickChoice.Create(T.SavePlan);
            qc.Widget = anchor.Rect;
            qc.AddInput(T.NounFileName, new QuickChoice.Input {
                Placeholder = T.EnterName,
                AutoFocus = true,
                OnChange = (v) => planName = v,
                OnSubmit = (v) => {
                    planName = v;
                    if (!string.IsNullOrWhiteSpace(planName)) {
                        DoSave(s, planName);
                        s.UIQuery.CancelQuickChoice.Ask();
                    }
                }
            });
            qc.AddButton(T.MenuSave, new QuickChoice.Button {
                OnClickValidate = () => {
                    if (string.IsNullOrWhiteSpace(planName)) { return false; }
                    DoSave(s, planName);
                    return true;
                },
                AutoClose = true
            });
            s.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private static void DoSave(GameState s, string planName) {
            PlanFileOps.Save(s.Map.Plan.Data, s.GridWidth, s.GridHeight, planName);
        }

        private static void ShowLoadPlanChoice(GameState s, WBase anchor, List<string> names) {
            var qc = QuickChoice.Create(T.LoadPlan);
            qc.Widget = anchor.Rect;
            if (names.Count > 0) {
                foreach (var name in names) {
                    var n = name;
                    qc.AddButton(n, () => {
                        var plan = s.Map.Plan.Data;
                        for (int i = 0; i < s.GridSize; i++) {
                            plan[i] = 0;
                        }
                        PlanFileOps.LoadInto(n, plan, s.GridWidth, s.GridHeight);
                        s.Sig.UpdatePlan.Send();
                    });
                }
            } else {
                qc.AddButton(T.NoSavedPlans, () => {
                    Files.EnsureDir(PlanFileOps.PlansDir);
                    Files.OpenOSFolder(PlanFileOps.PlansDir);
                });
            }
            s.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private static void ShowManageChoice(GameState s, WBase anchor) {
            var qc = QuickChoice.Create(T.ManageSavedPlans);
            qc.Widget = anchor.Rect;
            qc.AddButton(T.DeleteAllPlans, () => {
                Files.ClearDirectory(PlanFileOps.PlansDir);
            });
            qc.AddButton(T.OpenPlansDirectory, () => {
                Files.EnsureDir(PlanFileOps.PlansDir);
                Files.OpenOSFolder(PlanFileOps.PlansDir);
            });
            s.UIQuery.ShowQuickChoice.Ask(qc);
        }
        internal static void ErasePlan(GameState s) {
            var plan = s.Map.Plan.Data;
            for (int i = 0; i < s.GridSize; i++) {
                plan[i] = 0;
            }
            s.Sig.UpdatePlan.Send();
        }

        private static void GeneratePlan(GameState s) {
            var plan = s.Map.Plan.Data;
            for (int i = 0; i < s.GridSize; i++) {
                plan[i] = 0;
            }
            var floorData = s.Map.Grids[WorldLayer.Floor].Data;
            var wallData = s.Map.Grids[WorldLayer.Walls].Data;
            var ints = Caches.IntSet;
            ints.Clear();
            for (int i = 0; i < s.GridSize; i++) {
                if (floorData[i] != null && floorData[i].Integrity.CanHaveFullIntegrity) {
                    ints.Add(i);
                }
            }
            PosRect.KeepOutlineRef(ints);
            foreach (var i in ints) {
                plan[i] = 1;
            }
            for (int i = 0; i < s.GridSize; i++) {
                if (wallData[i] != null && wallData[i].Door == null) {
                    plan[i] = 2;
                }
            }
            s.Sig.UpdatePlan.Send();
        }
    }
}
