using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.Systems;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Data.Economy;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public sealed class ExpeditionFoundMarketItemStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionFoundMarketItem";
        private const string Var_LootDefId = "LootDefId";
        private const string Var_LootAmt = "LootAmt";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ExpeditionFoundMarketItemStoryEvent());
        }

        public override bool IsAvoidable(StoryEventData data, Expedition exp) {
            return false;
        }

        public override bool IsCancellable(StoryEventData data, Expedition exp) {
            return false;
        }

        public override string Title => null;

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((e) => ChooseLootAct(data, e));
            }
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;

            data.Def.Vars.TryStringSet(PropertyIdH.MarketGroups, out var mgs);
            if (Arrays.IsEmpty(mgs)) {
                D.Err("Empty 'MarketGroups' string array in Properties {0}!", data);
                mgs = new string[] { MarketGroupId.Material };
            }
            var sectorDiff = SOUtils.SectorDifficultyOf(data.TargetSO, A.PlayerSector);
            var valueMul = data.Def.Vars.Get(PropertyIdH.LootValueMul, 1f);
            if (!PickLoot(S, mgs, sectorDiff, valueMul, data.SeededRng,
                    out var item, out var amount)) {
                data.FailEvent(S, "No market loot in groups");
                return;
            }
            var vars = data.GetOrCreateData();
            vars.SetString("LootDefId", item.Id);
            vars.SetInt("LootAmt", amount);
        }

        public static bool PickLoot(GameState S, string[] groups, float sectorDiff,
                float valueMul, Rng rng, out Def def, out int amount) {
            var budget = LootBudget(sectorDiff, valueMul);
            if (!MarketLootPool.TryPick(S, groups, sectorDiff, budget, null, rng, out var mi)) {
                def = null;
                amount = 0;
                return false;
            }
            def = mi.Def;
            amount = Maths.Clamp((int) (budget / MarketLootPool.UnitValue(S, mi)), 1, 1000);
            return true;
        }

        // Sized by risk (sector difficulty), not by the player's wealth: raids are priced on
        // wealth, so wealth-sized loot fed them
        public static float LootBudget(float sectorDiff, float valueMul) {
            return CurveDef.FastGet(CurveIdH.Economy_ExpeditionLootValue).Evaluate(sectorDiff)
                * valueMul;
        }

        private void ChooseLootAct(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            var vars = data.Data;
            var objId = vars.GetString(Var_LootDefId);
            var quantity = vars.GetInt(Var_LootAmt);
            var def = The.Defs.Get(objId);
            var rng = new Rng(Hashes.Quick(exp.Seed, Primes.Prime022));
            var failChance = data.Def.Vars.Get(PropertyIdH.FailureChance, 0.01f)
                * rng.Range(0.9f, 1.1f);
            var chance = 1f - failChance;
            var name = rng.Chance(0.3f) ? def.CodexLink : T.Unknown;
            var msg = T.ExpFoundObjStart_V3.F(exp.Shuttle.Name, name,
                Units.Percentage(failChance));
            WPopup.Create(false, IconId.CQuestion, T.ExpeditionFoundObject, msg)
                .WithAction(T.Recover, () => {
                    if (AttemptToRecover(exp, rng, chance)) {
                        var id = objId;
                        D.Warn("Recovered loot!: {0}", id);
                        var def = The.Defs.Get(id);
                        if (def != null) {
                            if (def.MatType != null) {
                                if (exp.Shuttle.Tile.MaterialStorage.IsFull) {
                                    // Fall back to loot obj
                                    exp.AddObjLoot(id);
                                } else {
                                    exp.Shuttle.Store(def.MatType, quantity);
                                }
                            } else {
                                exp.AddObjLoot(id);
                            }
                        } else {
                            D.Err("Recovered loot, but there is no such def by id: {0}", id);
                        }
                    } else {
                        D.Warn("Loot recovery failed!");
                    }
                    exp.Context.ClearRequiredAction();
                    data.CreateLogEntry(IconId.COpenPackage, T.ExpeditionFoundObject, msg);
                    data.SetState(exp.S, StoryEventState.Ending);

                }).WithAction2(T.Ignore, () => {
                    exp.Context.ClearRequiredAction();
                    data.HideLogEntry();
                    data.SetState(exp.S, StoryEventState.Ending);
                }).Spawn(true);
        }
    }
}
