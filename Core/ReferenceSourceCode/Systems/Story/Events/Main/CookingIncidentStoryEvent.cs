using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Crafting;
using Game.Systems.Creatures;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Signals;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class CookingIncidentStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new CookingIncidentStoryEvent());
        }

        public const string EventId = "CookingIncident";
        public override string Id => EventId;

        private static Def cookingStationDef;
        private const string K_Dishes = "Dishes";

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var cookingStation = ctx.S.Components.FindFirstMatching<MultiCrafterComp>(
                cr => cr.Tile.Definition.IdH == DefIdsH.ObjectsDevicesCookingStation
                    && cr.CurrentOrder != null
                    && cr.CurrentOrder.State != CraftingOrderState.ProducedEnough);
            return cookingStation != null;
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            ListenToCookingEvent(ctx, data);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            ListenToCookingEvent(ctx, data);
        }

        private void ListenToCookingEvent(StoryContext ctx, StoryEventData data) {
            if (data.EventListener != null) { return; }
            cookingStationDef ??= The.Defs.FastGet(DefIdsH.ObjectsDevicesCookingStation);
            var listener = ctx.S.Sig.CrafterProducedItem.AddListener(
                (order, count) => OnCraftItem(ctx, data, order, count));
            data.SetState(ctx.S, StoryEventState.WaitingForHook);
            data.EventListener = listener;
            D.Warn("Listening to crafter produced item: {0}", data);
        }

        private void OnCraftItem(StoryContext ctx, StoryEventData data, CraftingOrder order, int count) {
            if (data.State != StoryEventState.WaitingForHook) { return; }
            if (order.Crafter.Tile.Definition != cookingStationDef) { return; }
            var def = data.Def;
            // The scripted first incident waits for a few dishes, so the kitchen has been
            // used before it burns. Counted in the event data, which is saved with it
            var dishesBeforeFire = def.Vars.Get(PropertyIdH.DishesBeforeFire, 1);
            if (dishesBeforeFire > 1) {
                var store = data.GetOrCreateData();
                var dishes = store.GetInt(K_Dishes, 0) + count;
                store.SetInt(K_Dishes, dishes);
                if (dishes < dishesBeforeFire) { return; }
            }
            var mat = order.Demand.Product.MatType;
            var cook = order.Crafter.Operatable.Operator;
            if (cook == null) { return; }
            var rng = data.SeededRng;
            if (cook.Data.Mind.Jobs.CanDo(JobTypeIdH.Cooking, out byte lvl)) {
                // Read per def (the Vars cache it): two defs share this implementation and
                // the first incident's curve differs from the regular one
                var failCurve = def.Vars.GetCurve(PropertyIdH.SkillFailChanceCurve);
                var chanceToFail = failCurve.Evaluate(lvl);
                if (!rng.Chance(chanceToFail)) {
                    // saved by the skill
                    return;
                }
            }
            var tile = order.Crafter.Tile;
            if (!ctx.S.Sys.Firex.CanIgnite(tile.Flammable)) {
                // station just burned and got extinguished during this incident,
                // wait for a later dish instead of announcing a fire that won't start
                return;
            }
            data.TargetBeing = cook.Data;
            data.SetState(ctx.S, StoryEventState.Ending);
            data.TargetTile = tile;
            NotifyFailure(data, tile, cook.Data, mat);

            var fuel = 0.25f;
            fuel *= ctx.S.Difficulty.Reduction;

            // Unboosted: the scripted first kitchen fire is a lesson, meant to be put out
            var canBoost = !def.Vars.Get(PropertyIdH.Unboosted, false);
            if (!ctx.S.Sys.Firex.TrySetOnFire(tile, Fire.CreateBalanced(ctx.S, fuel: fuel),
                    canBoost)) {
                D.Err("Failed to set cooking station on fire!");
            }
            if (ctx.S.Difficulty.Level > 0.75f && def.Vars.Get(PropertyIdH.IgnitesCook, true)) {
                if (rng.Chance(ctx.S.Difficulty.FireChanceScale * 0.25f)) {
                    ctx.S.Sys.Firex.TrySetOnFire(cook,
                        Fire.CreateBalanced(ctx.S, str: 0.5f, fuel: fuel * 0.5f));
                }
            }

            if (data.EventListener is Signal2<CraftingOrder, int>.SignalListener listener) {
                D.Warn("No longer listening to crafter produced item: {0}", data);
                ctx.S.Sig.CrafterProducedItem.RemoveListener(listener);
            }
        }

        private void BuildLogEntry(StoryEventData data, Tile tile, BeingData cook, MatType meal) {
            var evDesc = T.EvtCookingIncidentDesc_V3.F(cook.Name, meal.Def.CodexLink, tile.Definition.CodexLink);
            data.CreateLogEntry(IconId.CNoSparkles, T.EvtCookingIncident, evDesc);
        }

        private void NotifyFailure(StoryEventData data, Tile tile, BeingData cook, MatType meal) {
            data.TargetTile = tile;
            data.TargetBeing = cook;
            BuildLogEntry(data, tile, cook, meal);
            var S = tile.S;

            Notification.BuildFor(tile, IconId.CFire, data.LogEntry.Title, Consts.TicksPerDay)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithOnClick(() => EntityUtils.CameraFocusOn(tile))
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            var msg = T.EvtCookingIncidentDesc_V3.F(
                Texts.LinkToBeing(cook),
                meal.Def.CodexLink,
                tile.NameLink);
            S.Sig.ShowFanfare.Send(msg);
            S.Sys.Log.AddLine(msg);
        }
    }
}
