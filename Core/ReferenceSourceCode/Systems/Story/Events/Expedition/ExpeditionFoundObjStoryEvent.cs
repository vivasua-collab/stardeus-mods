using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public sealed class ExpeditionFoundObjStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionFoundObj";
        public override string Id => EventId;

        public override bool IsAvoidable(StoryEventData data, Expedition exp) {
            return false;
        }
        public override bool IsCancellable(StoryEventData data, Expedition exp) {
            return false;
        }


        [SelfInit]
        private static void Register() {
            Register(new ExpeditionFoundObjStoryEvent());
        }

        public override string Title => null;

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((e) => ChooseLootAct(data, e));
            }
        }

        private void ChooseLootAct(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            if (!data.Def.Vars.TryString(PropertyIdH.LootDefId, out var lootDef)) {
                data.FailEvent(exp.S, "Loot not defined!");
                return;
            }
            var failChance = data.Def.Vars.Get(PropertyIdH.FailureChance, 0.01f);
            var amtFrom = data.Def.Vars.Get(PropertyIdH.MinQuantity, 1);
            var amtTo = data.Def.Vars.Get(PropertyIdH.MaxQuantity, 1);
            var def = The.Defs.Get(lootDef);
            if (def == null) {
                D.Err("ExpeditionFoundObj: no def by id '{0}' — failing event", lootDef);
                data.FailEvent(exp.S, "Loot def not found: " + lootDef);
                return;
            }
            var rng = new Rng(Hashes.Quick(exp.Seed, Primes.Prime045));
            failChance *= rng.Range(0.9f, 1.1f);
            var chance = 1f - failChance;
            var msg = T.ExpFoundObjStart_V3.F(exp.Shuttle.Name,
                Texts.Accented(T.Unknown),
                Units.Percentage(failChance));
            WPopup.Create(false, IconId.CQuestion, T.ExpeditionFoundObject, msg)
                .WithAction(T.Recover, () => {
                    if (AttemptToRecover(exp, rng, chance)) {
                        var id = def.Id;
                        D.Warn("Recovered loot!: {0}", id);
                        if (def.MatType != null) {
                            var from = Maths.Max(1, amtFrom);
                            var to = Maths.Max(1, amtTo);
                            if (exp.Shuttle.Tile.MaterialStorage.IsFull) {
                                // Fall back to loot obj
                                exp.AddObjLoot(id);
                            } else {
                                int amt = from == to ? to : rng.Range(from, to);
                                if (amt < 1) {
                                    amt = 1;
                                }
                                exp.Shuttle.Store(def.MatType, amt);
                            }
                        } else {
                            exp.AddObjLoot(id);
                        }
                    } else {
                        D.Warn("Loot recovery failed!");
                    }
                    Coroutines.OnNextFrame(exp.Context.ClearRequiredAction);
                    data.CreateLogEntry(IconId.COpenPackage, T.ExpeditionFoundObject, msg);
                    data.SetState(exp.S, StoryEventState.Ending);
                }).WithAction2(T.Ignore, () => {
                    Coroutines.OnNextFrame(exp.Context.ClearRequiredAction);
                    data.HideLogEntry();
                    data.SetState(exp.S, StoryEventState.Ending);
                }).Spawn(true);
        }
    }
}
