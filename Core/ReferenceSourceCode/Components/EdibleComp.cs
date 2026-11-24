using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;
using KL.Grid;
using System.Text;

namespace Game.Components {
    public sealed class EdibleComp : BaseComponent<EdibleComp>, IStatProvider,
            IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new EdibleComp());
        }

        public float Remaining;
        public float Nutrition;
        public bool IsVegan { get; private set; }

        public static bool IsVeganFood(MatType type) {
            var config = type?.Def?.ComponentConfigFor(ComponentIdH.Edible, false);
            return config?.GetBool(PropertyIdH.IsVegan, false) == true;
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                if (Remaining < Nutrition && Obj.Pile.StackSize == 1) {
                    if (!Obj.Pile.TryTreatAsObj(true)) {
                        Remaining = Nutrition;
                    }
                    ShowInfoBar(Consts.ColorChartYellow,
                        Remaining / Nutrition, T.FoodBar);
                }
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.CarryEnded) {
                UpdateAmt();
            }
        }

        protected override void OnConfig() {
            Nutrition = Config.GetFloat(PropertyIdH.Amount);
            IsVegan = Config.GetBool(PropertyIdH.IsVegan, false);
            Remaining = Nutrition;
        }

        /// <returns>true if there's more food left</returns>
        public bool TryConsume(float amt, Being feeder, AIGoal goal) {
            if (feeder != null && Pos.SqrDistance(Obj.PosIdx, feeder.PosIdx) > 4) {
                D.ErrSoft("Trying to eat from too far away: {0}, {1}", Obj, feeder);
                return false;
            }
            if (Obj.Pile.StackSize != 1) {
                if (Obj.Pile.StackSize > 1) {
                    // happens if consumed food somehow got stacked
                    D.ErrSoft("Trying to consume edible when stack size is > 1: {0} | treat as obj: {1} | {2}",
                        Obj.Pile, Obj.Pile.TreatAsObj, goal);
                    // Treat the obj as a material again
                    Obj.Pile.TryTreatAsObj(false);
                }
                // don't consume this anymore
                return false;
            }
            bool wasAlreadyTreatedAsObj = Obj.Pile.TreatAsObj;
            if (!Obj.Pile.TryTreatAsObj(true)) {
                return false;
            }
            if (!wasAlreadyTreatedAsObj) {
                var matType = Obj.Pile.Type;
                if (matType != null) {
                    S.Sig.MatConsumed.Send(matType, 1);
                }
            }
            // Not forbidden: TreatAsObj already keeps a half eaten meal from being stored,
            // and a forbidden one would never be finished
            Remaining -= amt * Tunables.FoodConsumptionRate;
            UpdateAmt();
            if (Remaining > 0f) {
                return true;
            }
            var pile = Obj.Pile;
            if (pile.Obj.Carrier == feeder) {
                feeder?.Graphics.Drop(Obj, true);
            }
            pile.Take(pile.Type, null, 1, goal);
            HideInfoBar();
            return false;
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        private void UpdateAmt() {
            if (Remaining < Nutrition) {
                int rem = Mathf.CeilToInt(Remaining * 100f);
                var total = Mathf.CeilToInt(Nutrition * 100f);
                var bar = ShowInfoBar(Consts.ColorChartYellow, Remaining / Nutrition,
                    rem, total, $"{T.FoodNutritionAmt}: {Units.XOutOfY(rem, total)}");
                bar.IsPretranslated = true;
                stateChangedB?.Invoke();
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Remaining", Remaining);
        }

        protected override void OnLoad(ComponentData data) {
            Remaining = data.GetFloat("Remaining", 1f);
        }

        public override string ToString() {
            return $" * Edible [{Remaining}/{Nutrition}]";
        }

        public string StatsCategoryName => T.FoodNutritionAmt;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var pct = Nutrition > 0f ? Remaining / Nutrition : 0f;
            var rem = Mathf.CeilToInt(Remaining * 100f);
            var total = Mathf.CeilToInt(Nutrition * 100f);
            var row = UISpawn.Widget<WIconText2Progress>(life,
                UIPrefabId.WContentProgress, block.Container, idx++);
            row.Load(IconId.CFood, T.FoodNutritionAmt, pct);
            row.Item.SetText2(Units.XOutOfY(rem, total));
            UIUtils.AddTip(life, row, T.FoodNutritionAmtTip);
            StateChangedB.Bind(life, () => {
                var p = Nutrition > 0f ? Remaining / Nutrition : 0f;
                row.SetPercent(p, false);
                row.Item.SetText2(Units.XOutOfY(
                    Mathf.CeilToInt(Remaining * 100f),
                    Mathf.CeilToInt(Nutrition * 100f)));
            }, false);
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            var config = def.ComponentConfigFor(ComponentIdH.Edible, false);
            Def.PrependAdditionalInfo(sb, addedAnything);
            sb.Append(config.GetBool(PropertyIdH.IsVegan, false)
                ? T.FoodTypeVegan
                : T.FoodTypeCarnivore);
            sb.Append(", ");

            sb.Append(T.FoodNutritionAmt);
            sb.Append(": ");
            int amt = Mathf.RoundToInt(config.GetFloat(PropertyIdH.Amount) * 100);
            sb.Append(Texts.WithColor(Units.Num(amt), Texts.TMPColorYellow));
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
            // nothing to do
        }
    }
}
