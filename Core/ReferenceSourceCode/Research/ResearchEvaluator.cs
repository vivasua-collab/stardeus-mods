using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Systems.Crafting;
using KL.Collections;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Research {
    public sealed class ResearchEvaluator {
        private const int maxDepth = 5;
        private const float timeEnergyCostMul = 0.03f;
        private readonly GameState S;
        public ResearchEvaluator(GameState s) {
            S = s;
        }

        private float ValueOf(Def def) {
            if (def.InternalValue > 0f) {
                return def.InternalValue;
            }
            if (Researchable.TreeNode(def)) {
                def.InternalValue = ValueOfResearchable(def);
                return def.InternalValue;
            }
            if (def.MatType != null) {
                def.InternalValue = def.MatType.InternalValue;
                return def.InternalValue;
            }
            if (def.IsConstructable) {
                def.InternalValue = ValueOfConstructable(def);
            } else {
                def.InternalValue = ValueOfCraftables(def, 0, null);
            }
            return def.InternalValue;
        }

        // O(n^2)
        private float ValueOfResearchable(Def def) {
            if (def.InternalValue > 0f) {
                return def.InternalValue;
            }
            var unlockables = def.Unlockables;
            var sum = 0f;
            foreach (var unlockable in unlockables) {
                sum += ValueOf(unlockable);
            }
            return sum;
        }

        private float ValueOfConstructable(Def def) {
            if (def.InternalValue > 0f) {
                return def.InternalValue;
            }
            if (Arrays.IsEmpty(def.ConstructedMats)) {
                D.Err("Constructed mats empty for def: {0}", def);
                return 0f;
            }
            var sum = 0f;
            foreach (var mat in def.ConstructedMats) {
                sum += ValueOf(mat.Type, 0, mat.Type) * mat.StackSize;
            }
            return sum;
        }

        private float ValueOf(MatType matType, int depth, MatType rootMat) {
            if (matType.InternalValue > 0f) {
                return matType.InternalValue;
            }
            if (depth == maxDepth) {
                D.Err("Reached max depth while trying to get material cost. Root item: {0}. Current item: {1}",
                    rootMat, matType);
                return 0f;
            }
            var val = ValueOfCraftables(matType, depth, rootMat);
            return val;
        }

        private float ValueOfCraftables(Def def, int depth, MatType root) {
            if (def.InternalValue > 0f) {
                return def.InternalValue;
            }
            var craftables = Craftable.FindForDef(def);
            if (Arrays.IsEmpty(craftables)) {
                return 0f;
            }
            int num = 0;
            var sum = 0f;
            foreach (var craftable in craftables) {
                // To prevent infinite loops
                if (craftable.IgnoreMarket) { continue; }
                var sumIngrVal = 0f;
                foreach (var ingr in craftable.Ingredients) {
                    var ingrVal = ValueOf(ingr.Type, depth + 1, root)
                        * ingr.StackSize;
                    sumIngrVal += ingrVal;
                }
                if (sumIngrVal > 0f) {
                    num++;
                    sumIngrVal += craftable.ProductionTimeHours
                        * craftable.EnergyCost * timeEnergyCostMul;
                    sum += sumIngrVal;
                }
            }
            if (num <= 0) {
                return 0f;
            }
            var avgCraftVal = sum / num;
            return avgCraftVal;

        }

        private float ValueOfCraftables(MatType matType, int depth, MatType rootMat) {
            if (matType.InternalValue > 0f) {
                return matType.InternalValue;
            }
            float avgCraftVal = ValueOfCraftables(matType.Def, depth, rootMat);
            //D.Warn("Assigning internal value for {0}: {1}", matType, avgCraftVal);
            matType.InternalValue = avgCraftVal;
            return avgCraftVal;
        }

        public void CalculateInternalValue() {
            foreach (var mat in MatType.Known.Values) {
                if (mat.InternalValue < Maths.Epsilon) {
                    if (!mat.IsComposite) {
                        D.Err("Need to assign InternalValue to: {0}", mat.Id);
                        continue;
                    }
                    // resolve and set the value
                    var val = ValueOf(mat, 0, mat);
                    if (val < Maths.Epsilon) {
                        D.Err("Could not resolve value for mat {0}", mat);
                    }
                }
            }

            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract && (!Researchable.TreeNode(def) || def.Tier == 0)) {
                    continue;
                }
                if (def.IsTemplate) { continue; }
                var val = ValueOf(def);
                if (val < Maths.Epsilon) {
                    D.Warn("Could not resolve value for def {0}", def);
                }
            }

            Evaluate(true);
        }

        public void Evaluate(bool print) {
            if (ModifierResearchCost.Current != 1f) {
                D.Warn("Modded research costs, skipping validation");
                return;
            }
            var tierCostsMin = new Dictionary<int, float>();
            var tierCostsMax = new Dictionary<int, float>();
            var defsWithCost = new Dictionary<Def, float>();
            foreach (var def in The.Defs.Defs.Values.Where(v => Researchable.TreeNode(v))
                    .OrderBy(v => v.Tier).ThenBy(v => v.NameT)) {
                if (def.Tier == 0) { continue; }
                float unlockCost = 0f;
                if (def.Researchable.DatoidCosts != null) {
                    foreach (var d in def.Researchable.AdjustedDatoidCosts(S.Difficulty)) {
                        unlockCost += d.Type.InternalValue * d.StackSize;
                    }
                } else {
                    D.Warn("Excluding research from min/max cost calculation: {0}", def.Id);
                    continue;
                }
                if (unlockCost == 0f) {
                    D.Err("Unlock cost is 0 for {0}", def.Id);
                    continue;
                }
                if (!tierCostsMin.TryGetValue(def.Tier, out var cost)) {
                    cost = unlockCost;
                }
                cost = Maths.Min(cost, unlockCost);
                tierCostsMin[def.Tier] = cost;
                if (!tierCostsMax.TryGetValue(def.Tier, out var mcost)) {
                    mcost = unlockCost;
                }
                mcost = Maths.Max(mcost, unlockCost);
                tierCostsMax[def.Tier] = mcost;

                defsWithCost[def] = unlockCost;
            }

            var sb = Caches.StringBuilder;
            sb.Clear();
            if (print) {
                sb.AppendLine("Name,Tier,Cost,Basic(1),Tech(2),Phyto(3),Bio(4),Energy(4),Synth(5),HighTech(6),Nuclear(7),Alien(8),Quantum(9)");
            }
            // Validation loop
            foreach (var d in defsWithCost.OrderBy(v => v.Key.Tier).ThenBy(v => v.Value)) {
                var def = d.Key;
                float unlockCost = d.Value;

                // 1 - 1 - 15
                // 2 - 12 - 25
                // 3 - 20 - 40
                // 1 -> 15
                // 2 -> 12
                // 1 ->  15


                // RULE: every item should not cost more than half of cheapest item in a tier above
                if (tierCostsMin.TryGetValue(def.Tier + 1, out var minCostUpper1)) {
                    if (unlockCost > minCostUpper1 * 1.25f) {
                        D.Err("1: Unbalanced cost for {0}. Tier {1} costs {2} while min cost of Tier {3} is {4}",
                            def.Id, def.Tier, unlockCost, def.Tier + 1, minCostUpper1);
                    }
                }
                // RULE: every item should not cost less than half of most expensive item in a tier below
                if (tierCostsMax.TryGetValue(def.Tier - 1, out var maxCostLower1)) {
                    if (unlockCost < maxCostLower1 / 1.25f) {
                        D.Err("2: Unbalanced cost for {0}. Tier {1} costs {2} while max cost of Tier {3} is {4}",
                            def.Id, def.Tier, unlockCost, def.Tier - 1, maxCostLower1);
                    }
                }


                // RULE: every item should cost less than +2 tiers / 1.2
                if (tierCostsMin.TryGetValue(def.Tier + 2, out var minCostUpper2)) {
                    if (unlockCost > minCostUpper2 * 1.5f) {
                        D.Err("3: Unbalanced cost for {0}. Tier {1} costs {2} while min cost of Tier {3} is {4}",
                            def.Id, def.Tier, unlockCost, def.Tier + 2, minCostUpper2);
                    }
                }
                // RULE: every item should cost more than -2 tiers * 1.2
                if (tierCostsMax.TryGetValue(def.Tier - 2, out var maxCostLower2)) {
                    if (unlockCost < maxCostLower2 / 1.5f) {
                        D.Err("4: Unbalanced cost for {0}. Tier {1} costs {2} while max cost of Tier {3} is {4}",
                            def.Id, def.Tier, unlockCost, def.Tier - 2, maxCostLower2);
                    }
                }
                var costs = new Dictionary<int, int>();
                if (def.Researchable.DatoidCosts != null) {
                    foreach (var dc in def.Researchable.AdjustedDatoidCosts(S.Difficulty)) {
                        costs.GetIncrement(dc.Type.IdH, dc.StackSize);
                    }
                }
                if (print) {
                    sb.AppendFormat("{0},{1},{2}", def.NameT, def.Tier, unlockCost);
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidBasic, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidTech, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidPhyto, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidBiotic, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidEnergy, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidSynthetic, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidTechHigh, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidNuclear, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidAlien, 0));
                    sb.AppendFormat(",{0}", costs.Get(MatIdH.DatoidQuantum, 0));
                    sb.AppendLine();
                }
            }
            if (print) {
                D.Warn(sb.ToString());
            }
        }
    }
}
